using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;

namespace LifeOs.Infrastructure.Integrations;

/// <summary>
/// Oura's API v2 over HTTP (activity plan §3.1, §3.3): a small hand-written client. For now the
/// OAuth calls; the data collections arrive with the sync (A2).
/// <list type="bullet">
/// <item>Token calls post a form with the client id and secret, as Oura documents.</item>
/// <item>A 400, 401 or 403 from the token endpoint is <see cref="OuraGrantException"/>;
/// <c>invalid_grant</c> (or a 401 that isn't about the client) means the grant is spent.</item>
/// <item>A 429, a 5xx, anything else unexpected, a timeout or no connection is
/// <see cref="OuraUnavailableException"/>.</item>
/// <item>No message ever carries a token or the client secret.</item>
/// </list>
/// </summary>
public sealed class OuraHttpClient(HttpClient http, OuraOptions options, IClock clock) : IOuraClient
{
    /// <summary>How long a token lasts when Oura doesn't say (<c>expires_in</c>).</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

    private const int MaxDetail = 200;

    /// <summary>An <see cref="HttpClient"/> for Oura: pooled connections that notice DNS changes, a 30-second timeout.</summary>
    public static HttpClient CreateHttpClient()
        => new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

    public Uri AuthorizeUrl(string state, string codeChallenge, Uri redirectUri)
    {
        var query = Query(
            ("response_type", "code"),
            ("client_id", options.ClientId),
            ("redirect_uri", redirectUri.AbsoluteUri),
            ("scope", string.Join(' ', OuraConnectionService.Scopes)),
            ("state", state),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256"));
        return new UriBuilder(options.AuthorizeUrl) { Query = query }.Uri;
    }

    public Task<OAuthTokens> ExchangeCodeAsync(
        string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken = default)
        => PostTokenAsync(
            options.TokenUrl,
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", redirectUri.AbsoluteUri),
                new("code_verifier", codeVerifier),
                new("client_id", options.ClientId),
                new("client_secret", options.ClientSecret),
            ],
            OuraConnectionService.Scopes,
            cancellationToken);

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        => PostTokenAsync(
            options.RefreshUrl,
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("client_id", options.ClientId),
                new("client_secret", options.ClientSecret),
            ],
            [],
            cancellationToken);

    public async Task RevokeAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var url = new UriBuilder(options.RevokeUrl) { Query = Query(("access_token", accessToken)) }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request, "revoke the token", cancellationToken);

        // A token Oura no longer knows is as good as revoked.
        if (response.IsSuccessStatusCode
            || response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            return;
        }

        throw Unavailable(response, "revoke the token");
    }

    private async Task<OAuthTokens> PostTokenAsync(
        Uri url, KeyValuePair<string, string>[] form, IReadOnlyList<string> requestedScopes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await SendAsync(request, "sign in", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var (error, detail) = ParseError(body);
            var code = error ?? $"http_{(int)response.StatusCode}";
            var spent = error == "invalid_grant"
                || (response.StatusCode == HttpStatusCode.Unauthorized && error != "invalid_client");
            var message = detail is null
                ? $"Oura refused the sign-in ({code})."
                : $"Oura refused the sign-in ({code}: {detail}).";
            throw new OuraGrantException(code, message, spent);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw Unavailable(response, "sign in");
        }

        return ParseTokens(body, requestedScopes);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, string doing, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new OuraUnavailableException($"Oura couldn't be reached to {doing}; try again later.", null, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OuraUnavailableException($"Oura didn't answer in time to {doing}; try again later.", null, ex);
        }
    }

    private OAuthTokens ParseTokens(string body, IReadOnlyList<string> requestedScopes)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var access = Text(root, "access_token");
            var refresh = Text(root, "refresh_token");
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
            {
                throw new OuraUnavailableException("Oura's sign-in answer had no access or refresh token.");
            }

            var lifetime = Seconds(root, "expires_in") is { } seconds and > 0
                ? TimeSpan.FromSeconds(seconds)
                : DefaultLifetime;
            var scope = Text(root, "scope");
            var scopes = scope is null ? requestedScopes : NormalizeScopes(scope);
            return new OAuthTokens(access, refresh, clock.UtcNow + lifetime, scopes);
        }
        catch (JsonException ex)
        {
            throw new OuraUnavailableException("Oura's sign-in answer wasn't JSON.", null, ex);
        }
    }

    /// <summary>
    /// Oura's granted scopes without its <c>extapi:</c> prefix, each once, in the order given
    /// (<c>extapi:daily extapi:workout</c> → <c>daily</c>, <c>workout</c>).
    /// </summary>
    public static IReadOnlyList<string> NormalizeScopes(string scope)
        => scope.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.StartsWith("extapi:", StringComparison.OrdinalIgnoreCase) ? s["extapi:".Length..] : s)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static OuraUnavailableException Unavailable(HttpResponseMessage response, string doing)
    {
        var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var wait = RetryAfter(response);
            return new OuraUnavailableException(
                wait is { } after
                    ? $"Oura is limiting requests; try again in {Math.Max(1, (int)Math.Ceiling(after.TotalSeconds))} seconds."
                    : "Oura is limiting requests; try again in a few minutes.",
                wait);
        }

        return new OuraUnavailableException($"Oura answered {status} when asked to {doing}; try again later.");
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private static (string? Error, string? Detail) ParseError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var detail = Text(root, "error_description") ?? Text(root, "detail") ?? Text(root, "message");
            return (Text(root, "error"), detail is null ? null : Trim(detail));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Seconds(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static string Trim(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= MaxDetail ? flat : flat[..MaxDetail] + "…";
    }

    private static string Query(params (string Name, string Value)[] pairs)
    {
        var builder = new StringBuilder();
        foreach (var (name, value) in pairs)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }
}

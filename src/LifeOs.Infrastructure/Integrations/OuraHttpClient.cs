using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Domain;

namespace LifeOs.Infrastructure.Integrations;

/// <summary>
/// Oura's API v2 over HTTP (activity plan §3.1, §3.3, §3.9): a small hand-written client for the
/// OAuth calls and the collections LifeOS keeps, read into trimmed records.
/// <list type="bullet">
/// <item>Token calls post a form with the client id and secret, as Oura documents.</item>
/// <item>A 400, 401 or 403 from the token endpoint is <see cref="OuraGrantException"/>;
/// <c>invalid_grant</c> (or a 401 that isn't about the client) means the grant is spent.</item>
/// <item>A collection read that Oura refuses is <see cref="OuraAccessDeniedException"/>: 401
/// for the access token, 403 for access itself, with Oura's <c>detail</c>.</item>
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

    // A runaway next_token can't loop forever: a year of daily records is a few pages.
    private const int MaxPages = 500;

    /// <summary>An <see cref="HttpClient"/> for Oura: pooled connections that notice DNS changes, a 30-second timeout.</summary>
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // As Oura's documented clients do: say who's asking, and ask for JSON.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LifeOS/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

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

    public Task<IReadOnlyList<OuraActivityDay>> GetDailyActivityAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetCollectionAsync(accessToken, OuraCollections.DailyActivity.Name, start, end, ReadActivity, cancellationToken);

    public Task<IReadOnlyList<OuraWorkout>> GetWorkoutsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetCollectionAsync(accessToken, OuraCollections.Workout.Name, start, end, ReadWorkout, cancellationToken);

    public Task<IReadOnlyList<OuraSleepScore>> GetDailySleepAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetCollectionAsync(accessToken, OuraCollections.DailySleep.Name, start, end, ReadSleepScore, cancellationToken);

    public Task<IReadOnlyList<OuraReadiness>> GetDailyReadinessAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetCollectionAsync(accessToken, OuraCollections.DailyReadiness.Name, start, end, ReadReadiness, cancellationToken);

    public Task<IReadOnlyList<OuraSleepPeriod>> GetSleepPeriodsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetCollectionAsync(accessToken, OuraCollections.Sleep.Name, start, end, ReadSleepPeriod, cancellationToken);

    public Task<IReadOnlyList<OuraTag>> GetTagsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetCollectionAsync(accessToken, OuraCollections.Tag.Name, start, end, ReadTag, cancellationToken);

    private async Task<IReadOnlyList<T>> GetCollectionAsync<T>(
        string accessToken, string collection, DateOnly start, DateOnly end, Func<JsonElement, T?> read,
        CancellationToken cancellationToken)
        where T : class
    {
        var records = new List<T>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string? next = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var pairs = new List<(string Name, string Value)>
            {
                ("start_date", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("end_date", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            };
            if (next is not null)
            {
                pairs.Add(("next_token", next));
            }

            var url = new Uri(options.CollectionsUrl, collection + "?" + Query([.. pairs]));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await SendAsync(request, $"read {collection}", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Oura says why: an expired or revoked token, or (third-party reports) a scope
                // the collection needs. Kept, so the reconnect reason can say it.
                var (_, said) = ParseError(body);
                var detail = said ?? Challenge(response);
                throw new OuraAccessDeniedException(401,
                    $"Oura refused the access token for {collection}{(detail is null ? "." : $": “{detail}”")}",
                    collection,
                    detail);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var (_, detail) = ParseError(body);
                throw new OuraAccessDeniedException(403,
                    $"Oura refused access to {collection}{(detail is null ? "" : $" ({detail})")}. "
                    + "The Oura Membership may have lapsed, or a scope is missing: reconnecting asks for the scopes again.",
                    collection,
                    detail);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Unavailable(response, $"read {collection}");
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    throw new OuraUnavailableException($"Oura's answer for {collection} had no data list.");
                }

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && read(item) is { } record)
                    {
                        records.Add(record);
                    }
                }

                next = Text(root, "next_token");
            }
            catch (JsonException ex)
            {
                throw new OuraUnavailableException($"Oura's answer for {collection} wasn't JSON.", null, ex);
            }

            if (string.IsNullOrEmpty(next) || !tokens.Add(next))
            {
                return records;
            }
        }

        throw new OuraUnavailableException($"Oura's answer for {collection} kept paging past {MaxPages} pages.");
    }

    private static OuraActivityDay? ReadActivity(JsonElement item)
        => Day(item) is not { } day ? null : new OuraActivityDay(
            day,
            Text(item, "id"),
            Time(item, "timestamp"),
            Int(item, "steps"),
            Int(item, "active_calories"),
            Int(item, "total_calories"),
            Int(item, "equivalent_walking_distance"),
            Int(item, "high_activity_time"),
            Int(item, "medium_activity_time"),
            Int(item, "low_activity_time"),
            Int(item, "sedentary_time"),
            Int(item, "resting_time"),
            Int(item, "non_wear_time"),
            Int(item, "score"));

    private static OuraWorkout? ReadWorkout(JsonElement item)
        => Text(item, "id") is not { Length: > 0 } id
           || Day(item) is not { } day
           || Time(item, "start_datetime") is not { } start
           || Time(item, "end_datetime") is not { } end
            ? null
            : new OuraWorkout(
                id,
                day,
                Text(item, "activity") ?? "unknown",
                Text(item, "label"),
                start,
                end,
                Int(item, "calories"),
                Int(item, "distance"),
                Text(item, "intensity"),
                Text(item, "source") ?? "unknown");

    private static OuraSleepScore? ReadSleepScore(JsonElement item)
        => Day(item) is not { } day ? null : new OuraSleepScore(
            day, Text(item, "id"), Time(item, "timestamp"), Int(item, "score"), Contributors(item));

    private static OuraReadiness? ReadReadiness(JsonElement item)
        => Day(item) is not { } day ? null : new OuraReadiness(
            day,
            Text(item, "id"),
            Time(item, "timestamp"),
            Int(item, "score"),
            Number(item, "temperature_deviation"),
            Number(item, "temperature_trend_deviation"),
            Contributors(item));

    private static OuraSleepPeriod? ReadSleepPeriod(JsonElement item)
        => Text(item, "id") is not { Length: > 0 } id
           || Day(item) is not { } day
           || Time(item, "bedtime_start") is not { } start
           || Time(item, "bedtime_end") is not { } end
            ? null
            : new OuraSleepPeriod(
                id,
                day,
                Text(item, "type") ?? "unknown",
                start,
                end,
                Int(item, "time_in_bed"),
                Int(item, "total_sleep_duration"),
                Int(item, "deep_sleep_duration"),
                Int(item, "light_sleep_duration"),
                Int(item, "rem_sleep_duration"),
                Int(item, "awake_time"),
                Int(item, "latency"),
                Int(item, "efficiency"),
                Int(item, "average_hrv"),
                Number(item, "average_heart_rate") is { } hr ? Math.Round(hr, 1) : null,
                Int(item, "lowest_heart_rate"));

    // An Enhanced Tag (spec 1.42's EnhancedTagModel): its day is the one it starts on.
    private static OuraTag? ReadTag(JsonElement item)
        => Text(item, "id") is not { Length: > 0 } id
           || Day(item, "start_day") is not { } day
           || Time(item, "start_time") is not { } start
            ? null
            : new OuraTag(
                id,
                day,
                Day(item, "end_day"),
                Text(item, "tag_type_code"),
                Text(item, "custom_name"),
                start,
                Time(item, "end_time"),
                Text(item, "comment"));

    private static DateOnly? Day(JsonElement item, string name = "day")
        => Text(item, name) is { } text
           && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    private static DateTimeOffset? Time(JsonElement item, string name)
        => Text(item, name) is { } text
           && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time
            : null;

    private static double? Number(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static int? Int(JsonElement item, string name)
        => Number(item, name) is { } number ? (int)Math.Round(number, MidpointRounding.AwayFromZero) : null;

    // Sorted by name, so the same contributors always read (and hash) the same.
    private static IReadOnlyDictionary<string, int?> Contributors(JsonElement item)
    {
        var contributors = new SortedDictionary<string, int?>(StringComparer.Ordinal);
        if (item.TryGetProperty("contributors", out var value) && value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                contributors[property.Name] = property.Value.ValueKind == JsonValueKind.Number
                    ? (int)Math.Round(property.Value.GetDouble(), MidpointRounding.AwayFromZero)
                    : null;
            }
        }

        return contributors;
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

    // A bearer challenge's words (error="insufficient_scope", …) when the body gave none.
    private static string? Challenge(HttpResponseMessage response)
    {
        var challenge = string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        return challenge.Length == 0 ? null : Trim(challenge);
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

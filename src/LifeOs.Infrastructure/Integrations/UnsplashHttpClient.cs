using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Infrastructure.Integrations;

/// <summary>
/// Unsplash's API over HTTP (cover-images plan §3.2): search, one photo, and the download
/// counter, read into trimmed <see cref="UnsplashPhoto"/>s.
/// <list type="bullet">
/// <item>Every request sends <c>Authorization: Client-ID &lt;key&gt;</c> and
/// <c>Accept-Version: v1</c>, and only to <see cref="UnsplashOptions.ApiUrl"/>'s host: a
/// download link anywhere else is refused, so the key never leaves for another site.</item>
/// <item>A photo whose image isn't on <c>images.unsplash.com</c> (an Unsplash+ photo, say) or
/// that lacks its id, size or credit is dropped from a search, and refused by id.</item>
/// <item>Errors are <see cref="UnsplashException"/> in words: 401 a bad key, 403 with no
/// requests left (or 429) the hourly limit, 404 a missing photo, anything else, a timeout or
/// no connection Unsplash being unavailable. No message carries the key.</item>
/// </list>
/// </summary>
public sealed class UnsplashHttpClient(HttpClient http, UnsplashOptions options) : IUnsplashClient
{
    private const int MaxDetail = 200;

    /// <summary>An <see cref="HttpClient"/> for Unsplash: pooled connections that notice DNS changes, a 20-second timeout.</summary>
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("LifeOS/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    public string AppName => options.AppName;

    public async Task<UnsplashSearchPage> SearchAsync(
        string query, int page, int perPage, CancellationToken cancellationToken = default)
    {
        var url = new Uri(options.ApiUrl, "search/photos?" + Query(
            ("query", query),
            ("page", page.ToString(CultureInfo.InvariantCulture)),
            ("per_page", perPage.ToString(CultureInfo.InvariantCulture)),
            ("orientation", "landscape"),
            ("content_filter", "high")));
        var body = await GetAsync(url, "search Unsplash", notFound: null, cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                throw Unreadable("search");
            }

            var photos = new List<UnsplashPhoto>();
            foreach (var item in results.EnumerateArray())
            {
                if (ReadPhoto(item) is { } photo)
                {
                    photos.Add(photo);
                }
            }

            return new UnsplashSearchPage(Int(root, "total") ?? photos.Count, Int(root, "total_pages") ?? 1, photos);
        }
        catch (JsonException ex)
        {
            throw Unreadable("search", ex);
        }
    }

    public async Task<UnsplashPhoto> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default)
    {
        var url = new Uri(options.ApiUrl, "photos/" + Uri.EscapeDataString(photoId));
        const string gone = "That photo is no longer on Unsplash.";
        var body = await GetAsync(url, "look the photo up", notFound: gone, cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(body);
            return ReadPhoto(document.RootElement)
                   ?? throw new UnsplashException(UnsplashFailure.NotFound,
                       "That photo can't be a cover: Unsplash didn't give an image LifeOS can show, with its size and credit.");
        }
        catch (JsonException ex)
        {
            throw Unreadable("the photo", ex);
        }
    }

    public async Task TrackDownloadAsync(Uri downloadLocation, CancellationToken cancellationToken = default)
    {
        if (!downloadLocation.IsAbsoluteUri
            || downloadLocation.Scheme != options.ApiUrl.Scheme
            || !string.Equals(downloadLocation.Host, options.ApiUrl.Host, StringComparison.OrdinalIgnoreCase)
            || downloadLocation.Port != options.ApiUrl.Port)
        {
            throw new UnsplashException(UnsplashFailure.Unavailable,
                $"Unsplash's download link wasn't on {options.ApiUrl.Host}, so it wasn't followed.");
        }

        await GetAsync(downloadLocation, "count the download", notFound: "Unsplash no longer has that photo.", cancellationToken);
    }

    // The answer's body when it's a success; otherwise the failure in words.
    private async Task<string> GetAsync(Uri url, string doing, string? notFound, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", options.AccessKey);
        request.Headers.Add("Accept-Version", "v1");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new UnsplashException(UnsplashFailure.Unavailable,
                $"Unsplash couldn't be reached to {doing}; try again later.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UnsplashException(UnsplashFailure.Unavailable,
                $"Unsplash didn't answer in time to {doing}; try again later.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            throw Failure(response, body, doing, notFound);
        }
    }

    private static UnsplashException Failure(HttpResponseMessage response, string body, string doing, string? notFound)
    {
        var said = Said(body);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new UnsplashException(UnsplashFailure.BadKey,
                "Unsplash refused the Access Key: check Unsplash__AccessKey on this server.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests
            || (response.StatusCode == HttpStatusCode.Forbidden
                && (Remaining(response) == 0 || said?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true)))
        {
            return new UnsplashException(UnsplashFailure.RateLimited,
                "Unsplash's hourly limit is used up. Try again in a few minutes.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound && notFound is not null)
        {
            return new UnsplashException(UnsplashFailure.NotFound, notFound);
        }

        var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
        return new UnsplashException(UnsplashFailure.Unavailable,
            said is null
                ? $"Unsplash answered {status} when asked to {doing}; try again later."
                : $"Unsplash answered {status} when asked to {doing} (“{said}”); try again later.");
    }

    // X-Ratelimit-Remaining, when Unsplash sent it.
    private static int? Remaining(HttpResponseMessage response)
        => response.Headers.TryGetValues("X-Ratelimit-Remaining", out var values)
           && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)
            ? left
            : null;

    // Unsplash's own words: {"errors": ["…"]}, or a plain-text body ("Rate Limit Exceeded").
    private static string? Said(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                var words = string.Join("; ", errors.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()));
                return words.Length == 0 ? null : Trim(words);
            }

            return null;
        }
        catch (JsonException)
        {
            return Trim(body);
        }
    }

    // A photo LifeOS can show and credit, or null.
    private static UnsplashPhoto? ReadPhoto(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || Text(item, "id") is not { } id
            || !Cover.IsPhotoId(id)
            || Child(item, "urls") is not { } urls
            || Text(urls, "raw") is not { } raw
            || !Cover.IsImageUrl(raw)
            || Int(item, "width") is not { } width
            || width <= 0
            || Int(item, "height") is not { } height
            || height <= 0
            || Child(item, "user") is not { } user
            || Text(user, "name") is not { } name
            || string.IsNullOrWhiteSpace(name)
            || Child(user, "links") is not { } userLinks
            || Text(userLinks, "html") is not { } profile
            || !Cover.IsSiteUrl(profile)
            || Child(item, "links") is not { } links
            || Text(links, "html") is not { } page
            || !Cover.IsSiteUrl(page))
        {
            return null;
        }

        var download = Text(links, "download_location") is { } location
                       && Uri.TryCreate(location, UriKind.Absolute, out var uri)
            ? uri
            : null;
        // Names and descriptions as people read them: one space between words.
        return new UnsplashPhoto(
            id, raw, Text(item, "color"), width, height, Squeeze(Text(item, "description")),
            Squeeze(Text(item, "alt_description")), Squeeze(name)!, profile, page, download);
    }

    private static UnsplashException Unreadable(string what, Exception? inner = null)
        => new(UnsplashFailure.Unavailable, $"Unsplash's answer for {what} couldn't be read.", inner);

    private static JsonElement? Child(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? Text(JsonElement item, string name)
        => item.ValueKind == JsonValueKind.Object
           && item.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement item, string name)
        => item.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? Squeeze(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? null
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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

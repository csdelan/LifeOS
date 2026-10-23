using System.Net;
using System.Reflection;
using System.Text;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure.Integrations;
using Microsoft.AspNetCore.WebUtilities;

namespace LifeOs.Tests;

/// <summary>
/// Unsplash's API over HTTP (cover-images plan §3.2, C1), against a stub handler: what LifeOS
/// sends, how it reads Unsplash's answers (shaped like its documentation's examples, in
/// <c>UnsplashFixtures</c>), which photos it drops, and how each refusal or outage surfaces —
/// never with the Access Key in the message, and never sending the key to another host.
/// </summary>
public sealed class UnsplashHttpClientTests
{
    private const string Key = "the-access-key";

    private static readonly UnsplashOptions Options = new(Key, "lifeos", UnsplashOptions.DefaultApiUrl);

    private static (UnsplashHttpClient Client, StubHandler Handler) Create(params Func<HttpRequestMessage, HttpResponseMessage>[] answers)
    {
        var handler = new StubHandler(answers);
        return (new UnsplashHttpClient(new HttpClient(handler), Options), handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(HttpStatusCode status, string json)
        => _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Func<HttpRequestMessage, HttpResponseMessage> Text(HttpStatusCode status, string text, int? remaining = null)
        => _ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
            if (remaining is { } left)
            {
                response.Headers.Add("X-Ratelimit-Remaining", left.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return response;
        };

    internal static string Fixture(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"UnsplashFixtures.{name}.json")
                           ?? throw new InvalidOperationException($"No Unsplash fixture {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_asks_for_landscape_photos_with_the_strict_filter_and_the_key()
    {
        var (client, handler) = Create(Json(HttpStatusCode.OK, Fixture("search")));

        await client.SearchAsync("mountain sunrise & sea", 2, 24, Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        var uri = new Uri(sent.Uri);
        Assert.Equal("https://api.unsplash.com/search/photos", uri.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("mountain sunrise & sea", query["query"]);
        Assert.Equal("2", query["page"]);
        Assert.Equal("24", query["per_page"]);
        Assert.Equal("landscape", query["orientation"]);
        Assert.Equal("high", query["content_filter"]);
        Assert.Equal($"Client-ID {Key}", sent.Authorization);
        Assert.Equal("v1", sent.AcceptVersion);
    }

    [Fact]
    public async Task Search_reads_the_photos_and_drops_any_it_cant_show_or_credit()
    {
        var (client, _) = Create(Json(HttpStatusCode.OK, Fixture("search")));

        var page = await client.SearchAsync("coffee", 1, 24, Ct);

        Assert.Equal(133, page.Total);
        Assert.Equal(7, page.TotalPages);
        // The Unsplash+ photo (plus.unsplash.com) and the one without a photographer are dropped.
        Assert.Equal(["eOLpJytrbsQ", "xBRQfR2bqNI"], page.Photos.Select(p => p.Id));

        var first = page.Photos[0];
        Assert.Equal("https://images.unsplash.com/photo-1416339306562-f3d12fefd36f?ixid=M3w2NTQ3fDB8MXxzZWFyY2h8MXx8", first.RawUrl);
        Assert.Equal("#A7A2A1", first.Color);
        Assert.Equal(4000, first.Width);
        Assert.Equal(3000, first.Height);
        Assert.Equal("A man drinking a coffee.", first.Description);
        Assert.Null(first.AltDescription);
        Assert.Equal("Jeff Sheldon", first.Photographer);
        Assert.Equal("https://unsplash.com/@ugmonk", first.PhotographerUrl);
        Assert.Equal("https://unsplash.com/photos/eOLpJytrbsQ", first.PhotoUrl);
        Assert.Equal(new Uri("https://api.unsplash.com/photos/eOLpJytrbsQ/download"), first.DownloadLocation);

        // "Annie  Spratt" reads with one space.
        Assert.Equal("Annie Spratt", page.Photos[1].Photographer);
    }

    [Fact]
    public async Task A_photo_is_read_by_its_id()
    {
        var (client, handler) = Create(Json(HttpStatusCode.OK, Fixture("photo")));

        var photo = await client.GetPhotoAsync("Dwu85P9SOIk", Ct);

        Assert.Equal("https://api.unsplash.com/photos/Dwu85P9SOIk", Assert.Single(handler.Requests).Uri);
        Assert.Equal("Dwu85P9SOIk", photo.Id);
        Assert.StartsWith("https://images.unsplash.com/photo-1417325384643-aac51acc9e5d?ixid=", photo.RawUrl);
        Assert.Equal("man drinking coffee", photo.AltDescription);
        Assert.Equal("Joe Example", photo.Photographer);
        Assert.Equal("https://unsplash.com/@exampleuser", photo.PhotographerUrl);
        Assert.Equal(new Uri("https://api.unsplash.com/photos/Dwu85P9SOIk/download?ixid=M3w2NTQ3"), photo.DownloadLocation);
    }

    [Fact]
    public async Task A_photo_whose_image_is_off_unsplash_cant_be_a_cover()
    {
        var plus = Fixture("photo").Replace("https://images.unsplash.com/", "https://plus.unsplash.com/", StringComparison.Ordinal);
        var (client, _) = Create(Json(HttpStatusCode.OK, plus));

        var ex = await Assert.ThrowsAsync<UnsplashException>(() => client.GetPhotoAsync("Dwu85P9SOIk", Ct));

        Assert.Equal(UnsplashFailure.NotFound, ex.Failure);
        Assert.Contains("can't be a cover", ex.Message);
    }

    [Fact]
    public async Task A_missing_photo_is_not_found_in_words()
    {
        var (client, _) = Create(Json(HttpStatusCode.NotFound, """{"errors":["Couldn't find Photo"]}"""));

        var ex = await Assert.ThrowsAsync<UnsplashException>(() => client.GetPhotoAsync("gone", Ct));

        Assert.Equal(UnsplashFailure.NotFound, ex.Failure);
        Assert.Equal("That photo is no longer on Unsplash.", ex.Message);
    }

    [Fact]
    public async Task A_refused_key_says_so_without_the_key()
    {
        var (client, _) = Create(Json(HttpStatusCode.Unauthorized, """{"errors":["OAuth error: The access token is invalid"]}"""));

        var ex = await Assert.ThrowsAsync<UnsplashException>(() => client.SearchAsync("sea", 1, 24, Ct));

        Assert.Equal(UnsplashFailure.BadKey, ex.Failure);
        Assert.Contains("Unsplash__AccessKey", ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
    }

    [Fact]
    public async Task The_hourly_limit_is_said_plainly_whichever_way_unsplash_signals_it()
    {
        var limits = new[]
        {
            Text(HttpStatusCode.Forbidden, "Rate Limit Exceeded"),
            Text(HttpStatusCode.Forbidden, "", remaining: 0),
            Text(HttpStatusCode.TooManyRequests, ""),
        };

        foreach (var answer in limits)
        {
            var (client, _) = Create(answer);
            var ex = await Assert.ThrowsAsync<UnsplashException>(() => client.SearchAsync("sea", 1, 24, Ct));
            Assert.Equal(UnsplashFailure.RateLimited, ex.Failure);
            Assert.Equal("Unsplash's hourly limit is used up. Try again in a few minutes.", ex.Message);
        }
    }

    [Fact]
    public async Task Any_other_failure_is_unavailable_with_unsplashs_words()
    {
        var (client, _) = Create(Json(HttpStatusCode.InternalServerError, """{"errors":["Something went wrong"]}"""));

        var ex = await Assert.ThrowsAsync<UnsplashException>(() => client.SearchAsync("sea", 1, 24, Ct));

        Assert.Equal(UnsplashFailure.Unavailable, ex.Failure);
        Assert.Contains("500", ex.Message);
        Assert.Contains("Something went wrong", ex.Message);
    }

    [Fact]
    public async Task No_connection_and_an_unreadable_answer_are_unavailable()
    {
        var (offline, _) = Create(_ => throw new HttpRequestException("No such host is known."));
        var down = await Assert.ThrowsAsync<UnsplashException>(() => offline.SearchAsync("sea", 1, 24, Ct));
        Assert.Equal(UnsplashFailure.Unavailable, down.Failure);
        Assert.Contains("couldn't be reached", down.Message);

        var (garbled, _) = Create(Text(HttpStatusCode.OK, "<html>not json</html>"));
        var unreadable = await Assert.ThrowsAsync<UnsplashException>(() => garbled.SearchAsync("sea", 1, 24, Ct));
        Assert.Equal(UnsplashFailure.Unavailable, unreadable.Failure);
        Assert.Contains("couldn't be read", unreadable.Message);
    }

    [Fact]
    public async Task Counting_a_download_calls_the_photos_download_location_with_the_key()
    {
        var (client, handler) = Create(Json(HttpStatusCode.OK, """{"url":"https://image.example/file.jpg"}"""));
        var location = new Uri("https://api.unsplash.com/photos/Dwu85P9SOIk/download?ixid=M3w2NTQ3");

        await client.TrackDownloadAsync(location, Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(location.AbsoluteUri, sent.Uri);
        Assert.Equal($"Client-ID {Key}", sent.Authorization);
    }

    [Theory]
    [InlineData("https://example.com/photos/Dwu85P9SOIk/download")]
    [InlineData("http://api.unsplash.com/photos/Dwu85P9SOIk/download")]
    [InlineData("https://api.unsplash.com:8443/photos/Dwu85P9SOIk/download")]
    public async Task A_download_link_off_unsplashs_api_is_never_followed(string location)
    {
        var (client, handler) = Create(Json(HttpStatusCode.OK, "{}"));

        var ex = await Assert.ThrowsAsync<UnsplashException>(() => client.TrackDownloadAsync(new Uri(location), Ct));

        Assert.Equal(UnsplashFailure.Unavailable, ex.Failure);
        Assert.Contains("wasn't followed", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void The_app_name_comes_from_the_settings()
    {
        var (client, _) = Create();

        Assert.Equal("lifeos", client.AppName);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Accept-Version", out var versions) ? versions.Single() : null));
            return Task.FromResult(answers[Math.Min(Requests.Count - 1, answers.Length - 1)](request));
        }
    }

    private sealed record SentRequest(HttpMethod Method, string Uri, string? Authorization, string? AcceptVersion);
}

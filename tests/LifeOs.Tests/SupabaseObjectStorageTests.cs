using System.Net;
using System.Text;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure.Storage;

namespace LifeOs.Tests;

/// <summary>
/// The Supabase Storage adapter against a stubbed HTTP server: the requests it sends, how it
/// reads Storage's answers (some errors come as HTTP 400 with the real status in the body),
/// creating a missing bucket, and its settings.
/// </summary>
public sealed class SupabaseObjectStorageTests
{
    private const string SecretKey = "sb_secret_test0123456789";
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (SupabaseObjectStorage Storage, StubHandler Handler) Create(
        string key = SecretKey, params Func<HttpRequestMessage, HttpResponseMessage>[] answers)
    {
        var handler = new StubHandler(answers);
        var options = new SupabaseStorageOptions("https://abc.supabase.co", key, "lifeos-artifacts");
        return (new SupabaseObjectStorage(new HttpClient(handler), options), handler);
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, object? body = null) => new(status)
    {
        Content = new StringContent(body is null ? "" : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task An_upload_posts_the_bytes_with_the_secret_key_and_refuses_to_overwrite()
    {
        var (storage, handler) = Create(answers: _ => Answer(HttpStatusCode.OK, new { Key = "lifeos-artifacts/artifacts/x" }));

        await storage.PutAsync($"artifacts/{Sha}", [1, 2, 3], "image/jpeg", Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://abc.supabase.co/storage/v1/object/lifeos-artifacts/artifacts/{Sha}", request.Uri);
        Assert.Equal(SecretKey, request.Headers["apikey"]);
        Assert.Equal("false", request.Headers["x-upsert"]);
        // A secret key is not a JWT: Supabase refuses it as a bearer token.
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.Equal("image/jpeg", request.ContentType);
        Assert.Equal([1, 2, 3], request.Body);
        Assert.Equal(("supabase", "lifeos-artifacts"), (storage.Store, storage.Bucket));
    }

    [Fact]
    public async Task A_legacy_service_role_key_also_goes_as_the_bearer_token()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.sig";
        var (storage, handler) = Create(jwt, _ => Answer(HttpStatusCode.OK));

        await storage.PutAsync("artifacts/a", [1], "image/png", Ct);

        Assert.Equal($"Bearer {jwt}", Assert.Single(handler.Requests).Headers["Authorization"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)] // Storage's older answer: the real status is in the body
    [InlineData(HttpStatusCode.Conflict)]
    public async Task An_object_already_at_the_key_is_success(HttpStatusCode status)
    {
        var (storage, handler) = Create(answers: _ => Answer(status,
            new { statusCode = "409", error = "Duplicate", message = "The resource already exists" }));

        await storage.PutAsync($"artifacts/{Sha}", [1], "image/jpeg", Ct);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_missing_bucket_is_created_private_and_the_upload_tried_again()
    {
        var (storage, handler) = Create(answers:
        [
            _ => Answer(HttpStatusCode.BadRequest, new { statusCode = "404", error = "Bucket not found", message = "Bucket not found" }),
            _ => Answer(HttpStatusCode.OK, new { name = "lifeos-artifacts" }),
            _ => Answer(HttpStatusCode.OK),
        ]);

        await storage.PutAsync("artifacts/a", [7], "image/webp", Ct);

        Assert.Equal(3, handler.Requests.Count);
        var create = handler.Requests[1];
        Assert.Equal("https://abc.supabase.co/storage/v1/bucket", create.Uri);
        using var body = JsonDocument.Parse(create.Body);
        Assert.Equal("lifeos-artifacts", body.RootElement.GetProperty("id").GetString());
        Assert.False(body.RootElement.GetProperty("public").GetBoolean());
        Assert.Equal(handler.Requests[0].Uri, handler.Requests[2].Uri);
    }

    [Fact]
    public async Task A_refused_upload_says_why()
    {
        var (storage, _) = Create(answers: _ => Answer(HttpStatusCode.BadRequest,
            new { statusCode = "403", error = "Unauthorized", message = "new row violates row-level security policy" }));

        var ex = await Assert.ThrowsAsync<ObjectStorageException>(() => storage.PutAsync("artifacts/a", [1], "image/jpeg", Ct));

        Assert.Contains("403 new row violates row-level security policy", ex.Message);
    }

    [Fact]
    public async Task A_download_returns_the_bytes_or_null_when_missing()
    {
        var (storage, handler) = Create(answers:
        [
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([4, 5, 6]) },
            _ => Answer(HttpStatusCode.BadRequest, new { statusCode = "404", error = "not_found", message = "Object not found" }),
            _ => Answer(HttpStatusCode.NotFound, new { statusCode = "404", error = "Bucket not found" }),
        ]);

        Assert.Equal([4, 5, 6], await storage.GetAsync("old-bucket", "artifacts/a b", Ct));
        Assert.Null(await storage.GetAsync("old-bucket", "artifacts/b", Ct));
        Assert.Null(await storage.GetAsync("gone", "artifacts/c", Ct));

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("https://abc.supabase.co/storage/v1/object/old-bucket/artifacts/a%20b", handler.Requests[0].Uri);
    }

    [Fact]
    public async Task An_unreachable_or_failing_store_is_an_object_storage_error()
    {
        var (down, _) = Create(answers: _ => throw new HttpRequestException("No such host is known."));
        Assert.Contains("couldn't be reached", (await Assert.ThrowsAsync<ObjectStorageException>(
            () => down.GetAsync("b", "k", Ct))).Message);

        var (failing, _) = Create(answers: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("upstream exploded")
        });
        Assert.Contains("500 upstream exploded", (await Assert.ThrowsAsync<ObjectStorageException>(
            () => failing.GetAsync("b", "k", Ct))).Message);
    }

    [Fact]
    public void Settings_need_a_secret_key_and_a_url()
    {
        static SupabaseStorageOptions? Resolve(Dictionary<string, string?> values)
            => SupabaseStorageOptions.Resolve(key => values.GetValueOrDefault(key));

        // No key: bytes stay in the database.
        Assert.Null(Resolve(new() { [SupabaseStorageOptions.UrlKey] = "https://abc.supabase.co" }));

        // The URL defaults to the SPA's, down to its origin; the bucket to lifeos-artifacts.
        var options = Resolve(new()
        {
            [SupabaseStorageOptions.SecretKeyKey] = SecretKey,
            [SupabaseStorageOptions.SpaUrlKey] = "https://abc.supabase.co/",
        })!;
        Assert.Equal(("https://abc.supabase.co", SecretKey, "lifeos-artifacts"), (options.Url, options.SecretKey, options.Bucket));
        Assert.DoesNotContain(SecretKey, options.ToString());

        Assert.Equal("photos", Resolve(new()
        {
            [SupabaseStorageOptions.SecretKeyKey] = SecretKey,
            [SupabaseStorageOptions.UrlKey] = "https://abc.supabase.co",
            [SupabaseStorageOptions.SpaUrlKey] = "https://other.supabase.co",
            [SupabaseStorageOptions.BucketKey] = "photos",
        })!.Bucket);

        Assert.Contains("no Supabase URL", Assert.Throws<InvalidOperationException>(
            () => Resolve(new() { [SupabaseStorageOptions.SecretKeyKey] = SecretKey })).Message);
        Assert.Contains("publishable", Assert.Throws<InvalidOperationException>(() => Resolve(new()
        {
            [SupabaseStorageOptions.SecretKeyKey] = "sb_publishable_abc",
            [SupabaseStorageOptions.UrlKey] = "https://abc.supabase.co",
        })).Message);
        Assert.Throws<InvalidOperationException>(() => Resolve(new()
        {
            [SupabaseStorageOptions.SecretKeyKey] = SecretKey,
            [SupabaseStorageOptions.UrlKey] = "abc.supabase.co",
        }));
        Assert.Throws<InvalidOperationException>(() => Resolve(new()
        {
            [SupabaseStorageOptions.SecretKeyKey] = SecretKey,
            [SupabaseStorageOptions.UrlKey] = "https://abc.supabase.co",
            [SupabaseStorageOptions.BucketKey] = "Has Spaces",
        }));
    }

    /// <summary>Answers each request with the next answer (the last repeats) and keeps what was sent.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return answers[Math.Min(Requests.Count - 1, answers.Length - 1)](request);
        }
    }

    private sealed record SentRequest(
        HttpMethod Method, string Uri, Dictionary<string, string> Headers, string? ContentType, byte[] Body);
}

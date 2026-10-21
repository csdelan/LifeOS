using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Abstractions;

namespace LifeOs.Infrastructure.Storage;

/// <summary>
/// <see cref="IObjectStorage"/> over the Supabase Storage REST API (<c>{url}/storage/v1</c>),
/// with a secret key, so it reads and writes a private bucket whatever its policies say.
/// <list type="bullet">
/// <item>Upload is <c>POST /object/{bucket}/{key}</c> with <c>x-upsert: false</c>: an object
/// already at the key is a duplicate, which is success (keys are content-addressed).</item>
/// <item>A missing bucket is created, private, and the upload tried once more.</item>
/// <item>Download is <c>GET /object/{bucket}/{key}</c>; not found is <c>null</c>.</item>
/// </list>
/// Storage answers some errors as HTTP 400 with the real status in the body's
/// <c>statusCode</c> ("404", "409"), so that wins over the HTTP status when present.
/// Secret keys (<c>sb_secret_…</c>) travel on the <c>apikey</c> header only; the legacy
/// <c>service_role</c> JWT also goes as the bearer token, as Supabase asks.
/// </summary>
public sealed class SupabaseObjectStorage : IObjectStorage
{
    private readonly HttpClient http;
    private readonly Uri api;
    private readonly string secretKey;

    public SupabaseObjectStorage(HttpClient http, SupabaseStorageOptions options)
    {
        this.http = http;
        api = new Uri($"{options.Url.TrimEnd('/')}/storage/v1/");
        secretKey = options.SecretKey;
        Bucket = options.Bucket;
    }

    /// <summary>A long-lived client for the composition root: connections recycle so DNS changes land.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public string Store => "supabase";

    public string Bucket { get; }

    public async Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = Request(HttpMethod.Post, $"object/{Path(Bucket)}/{Path(key)}");
            request.Headers.Add("x-upsert", "false");
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type)
                ? type
                : new MediaTypeHeaderValue("application/octet-stream");

            using var response = await SendAsync(request, "upload", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var error = await ErrorAsync(response, cancellationToken);
            switch (error.Status)
            {
                case HttpStatusCode.Conflict:
                    return;
                case HttpStatusCode.NotFound when attempt == 1:
                    await CreateBucketAsync(cancellationToken);
                    continue;
                default:
                    throw new ObjectStorageException($"Supabase Storage refused to store {key} in {Bucket}: {error}.");
            }
        }
    }

    public async Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get, $"object/{Path(bucket)}/{Path(key)}");
        using var response = await SendAsync(request, "download", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        var error = await ErrorAsync(response, cancellationToken);
        return error.Status == HttpStatusCode.NotFound
            ? null
            : throw new ObjectStorageException($"Supabase Storage refused to read {key} from {bucket}: {error}.");
    }

    private async Task CreateBucketAsync(CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "bucket");
        request.Content = JsonContent.Create(new { id = Bucket, name = Bucket, @public = false });
        using var response = await SendAsync(request, "create the bucket", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await ErrorAsync(response, cancellationToken);
        if (error.Status != HttpStatusCode.Conflict)
        {
            throw new ObjectStorageException($"Supabase Storage refused to create the bucket {Bucket}: {error}.");
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(api, path));
        request.Headers.Add("apikey", secretKey);
        if (secretKey.StartsWith("eyJ", StringComparison.Ordinal))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        }

        return request;
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
            throw new ObjectStorageException($"Supabase Storage couldn't be reached to {doing}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ObjectStorageException($"Supabase Storage didn't answer in time to {doing}.", ex);
        }
    }

    private static async Task<StorageError> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = response.StatusCode;
        string? message = null;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("statusCode", out var code)
                    && int.TryParse(code.ValueKind == JsonValueKind.Number ? code.GetRawText() : code.GetString(), out var parsed)
                    && parsed is >= 100 and <= 599)
                {
                    status = (HttpStatusCode)parsed;
                }

                message = Text(root, "message") ?? Text(root, "error");
            }
        }
        catch (JsonException)
        {
            // Not JSON: the HTTP status and the start of the body say enough.
        }

        message ??= body.Length > 200 ? body[..200] : body;
        return new StorageError(status, message);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Path(string value) =>
        string.Join('/', value.Split('/').Select(Uri.EscapeDataString));

    private sealed record StorageError(HttpStatusCode Status, string Message)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(Message) ? $"{(int)Status} {Status}" : $"{(int)Status} {Message}";
    }
}

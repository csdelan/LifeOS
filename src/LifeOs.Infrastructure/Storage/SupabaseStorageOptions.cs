using System.Text.RegularExpressions;

namespace LifeOs.Infrastructure.Storage;

/// <summary>
/// Where artifact bytes go when a host keeps them in Supabase Storage rather than the
/// database (migration 0035, binary-artifacts.md): the project URL, a <em>secret</em> key and
/// the bucket. The secret key bypasses row-level security, so it lives only on the server —
/// a Fly secret, never in the SPA.
/// <list type="bullet">
/// <item><c>Storage:SupabaseUrl</c> — the project URL; defaults to <c>VITE_SUPABASE_URL</c>.</item>
/// <item><c>Storage:SupabaseSecretKey</c> — a secret key (<c>sb_secret_…</c>) or the legacy
/// <c>service_role</c> JWT. Without it, bytes stay in the database.</item>
/// <item><c>Storage:Bucket</c> — defaults to <see cref="DefaultBucket"/>; created private on first use.</item>
/// </list>
/// </summary>
public sealed partial record SupabaseStorageOptions(string Url, string SecretKey, string Bucket)
{
    public const string UrlKey = "Storage:SupabaseUrl";
    public const string SecretKeyKey = "Storage:SupabaseSecretKey";
    public const string BucketKey = "Storage:Bucket";

    /// <summary>The SPA's Supabase URL, which the storage URL defaults to.</summary>
    public const string SpaUrlKey = "VITE_SUPABASE_URL";

    public const string DefaultBucket = "lifeos-artifacts";

    /// <summary>
    /// Reads the settings through <paramref name="get"/> (a configuration lookup by
    /// <c>Storage:…</c> key), or <c>null</c> when no secret key is set. Throws
    /// <see cref="InvalidOperationException"/> for a key without a URL, a URL that isn't
    /// http(s), a publishable key where the secret key belongs, or a malformed bucket name,
    /// so a bad deploy fails at startup rather than at the first photo.
    /// </summary>
    public static SupabaseStorageOptions? Resolve(Func<string, string?> get)
    {
        var secret = get(SecretKeyKey)?.Trim();
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        if (secret.StartsWith("sb_publishable_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{SecretKeyKey} holds a publishable key. Storage needs a secret key (sb_secret_…) from Supabase → Project Settings → API Keys.");
        }

        var configured = get(UrlKey)?.Trim();
        var url = string.IsNullOrEmpty(configured) ? get(SpaUrlKey)?.Trim() : configured;
        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException(
                $"{SecretKeyKey} is set but there is no Supabase URL: set {UrlKey} (or {SpaUrlKey}).");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"{UrlKey} must be an http(s) URL, got '{url}'.");
        }

        var bucket = get(BucketKey)?.Trim();
        bucket = string.IsNullOrEmpty(bucket) ? DefaultBucket : bucket;
        if (!BucketName().IsMatch(bucket))
        {
            throw new InvalidOperationException(
                $"{BucketKey} must be lowercase letters, digits, dots, dashes or underscores, got '{bucket}'.");
        }

        return new SupabaseStorageOptions(uri.GetLeftPart(UriPartial.Authority), secret, bucket);
    }

    /// <summary>Reads the settings from environment variables (<c>Storage__SupabaseSecretKey</c>, …).</summary>
    public static SupabaseStorageOptions? FromEnvironment() =>
        Resolve(key => Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal)));

    // Never print the key.
    public override string ToString() => $"SupabaseStorageOptions {{ Url = {Url}, Bucket = {Bucket} }}";

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,62}$")]
    private static partial Regex BucketName();
}

using LifeOs.Domain;

namespace LifeOs.Infrastructure.Integrations;

/// <summary>
/// The Unsplash app LifeOS searches as (cover-images plan §3.2). Server-only: the Access Key
/// lives in <c>.env</c> and as a Fly secret, never in a <c>VITE_</c> variable.
/// <list type="bullet">
/// <item><c>Unsplash:AccessKey</c> — from the app registered at unsplash.com/developers.
/// Without it, covers are off: the picker and <c>bsk cover</c> say Unsplash isn't set up.</item>
/// <item><c>Unsplash:AppName</c> — the <c>utm_source</c> on links back to Unsplash;
/// <see cref="Cover.DefaultAppName"/> by default. It should match the registered app's name.</item>
/// <item><c>Unsplash:ApiUrl</c> — Unsplash's API by default; only tests and a local fake move it.
/// The key is only ever sent there.</item>
/// </list>
/// </summary>
public sealed record UnsplashOptions(string AccessKey, string AppName, Uri ApiUrl)
{
    public const string AccessKeyKey = "Unsplash:AccessKey";
    public const string AppNameKey = "Unsplash:AppName";
    public const string ApiUrlKey = "Unsplash:ApiUrl";

    public static readonly Uri DefaultApiUrl = new("https://api.unsplash.com/");

    /// <summary>
    /// Reads the settings through <paramref name="get"/>, or null when there's no Access Key.
    /// Throws <see cref="InvalidOperationException"/> for a key with spaces in it, an app name
    /// that can't be a <c>utm_source</c>, or an API URL that isn't an absolute http(s) URL.
    /// </summary>
    public static UnsplashOptions? Resolve(Func<string, string?> get)
    {
        var key = get(AccessKeyKey)?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (key.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException($"{AccessKeyKey} can't contain spaces.");
        }

        var appName = get(AppNameKey)?.Trim();
        appName = string.IsNullOrEmpty(appName) ? Cover.DefaultAppName : appName;
        if (!Cover.IsAppName(appName))
        {
            throw new InvalidOperationException(
                $"{AppNameKey} must be lowercase letters, digits, dashes or underscores (it's a utm_source), got '{appName}'.");
        }

        return new UnsplashOptions(key, appName, ResolveApiUrl(get));
    }

    // Never print the key.
    public override string ToString() => $"UnsplashOptions {{ AppName = {AppName}, ApiUrl = {ApiUrl} }}";

    private static Uri ResolveApiUrl(Func<string, string?> get)
    {
        var text = get(ApiUrlKey)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return DefaultApiUrl;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"{ApiUrlKey} must be an absolute http(s) URL, got '{text}'.");
        }

        // A base the paths are resolved against: it must end in a slash.
        return uri.AbsolutePath.EndsWith('/') ? uri : new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;
    }
}

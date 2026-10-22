using Microsoft.Extensions.Configuration;

namespace LifeOs.Infrastructure.Integrations;

/// <summary>
/// The Oura app LifeOS signs in as (activity plan §3.1). Server-only: the client secret lives
/// in <c>.env</c> and as a Fly secret, never in a <c>VITE_</c> variable.
/// <list type="bullet">
/// <item><c>Oura:ClientId</c>, <c>Oura:ClientSecret</c> — from the app registered at
/// developer.ouraring.com. Without a client id, Oura is off.</item>
/// <item><c>Oura:AuthorizeUrl</c>, <c>Oura:TokenUrl</c>, <c>Oura:RevokeUrl</c> — Oura's
/// documented endpoints by default.</item>
/// <item><c>Oura:RefreshUrl</c> — where refreshes go; the token URL by default. A report says
/// apps from Oura's new portal must refresh elsewhere, so it can be moved without a release.</item>
/// <item><c>Oura:CollectionsUrl</c> — where the data collections are read; Oura's
/// <c>/v2/usercollection/</c> by default (its sandbox is <c>/v2/sandbox/usercollection/</c>).</item>
/// </list>
/// </summary>
public sealed record OuraOptions(
    string ClientId, string ClientSecret, Uri AuthorizeUrl, Uri TokenUrl, Uri RefreshUrl, Uri RevokeUrl,
    Uri CollectionsUrl)
{
    public const string ClientIdKey = "Oura:ClientId";
    public const string ClientSecretKey = "Oura:ClientSecret";
    public const string AuthorizeUrlKey = "Oura:AuthorizeUrl";
    public const string TokenUrlKey = "Oura:TokenUrl";
    public const string RefreshUrlKey = "Oura:RefreshUrl";
    public const string RevokeUrlKey = "Oura:RevokeUrl";
    public const string CollectionsUrlKey = "Oura:CollectionsUrl";

    public static readonly Uri DefaultAuthorizeUrl = new("https://cloud.ouraring.com/oauth/authorize");
    public static readonly Uri DefaultTokenUrl = new("https://api.ouraring.com/oauth/token");
    public static readonly Uri DefaultRevokeUrl = new("https://api.ouraring.com/oauth/revoke");
    public static readonly Uri DefaultCollectionsUrl = new("https://api.ouraring.com/v2/usercollection/");

    /// <summary>
    /// Reads the settings through <paramref name="get"/>, or null when there's no client id or
    /// secret. Throws <see cref="InvalidOperationException"/> for one without the other, a value
    /// with spaces in it, or an endpoint that isn't an absolute http(s) URL.
    /// </summary>
    public static OuraOptions? Resolve(Func<string, string?> get)
    {
        var clientId = get(ClientIdKey)?.Trim();
        var secret = get(ClientSecretKey)?.Trim();
        if (string.IsNullOrEmpty(clientId) && string.IsNullOrEmpty(secret))
        {
            return null;
        }

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"Set both {ClientIdKey} and {ClientSecretKey} (from developer.ouraring.com), or neither to leave Oura off.");
        }

        if (clientId.Any(char.IsWhiteSpace) || secret.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException($"{ClientIdKey} and {ClientSecretKey} can't contain spaces.");
        }

        var tokenUrl = Endpoint(get, TokenUrlKey, DefaultTokenUrl);
        return new OuraOptions(
            clientId,
            secret,
            Endpoint(get, AuthorizeUrlKey, DefaultAuthorizeUrl),
            tokenUrl,
            Endpoint(get, RefreshUrlKey, tokenUrl),
            Endpoint(get, RevokeUrlKey, DefaultRevokeUrl),
            Folder(Endpoint(get, CollectionsUrlKey, DefaultCollectionsUrl)));
    }

    // Never print the secret.
    public override string ToString()
        => $"OuraOptions {{ ClientId = {ClientId}, AuthorizeUrl = {AuthorizeUrl}, TokenUrl = {TokenUrl}, RefreshUrl = {RefreshUrl}, RevokeUrl = {RevokeUrl}, CollectionsUrl = {CollectionsUrl} }}";

    // A base a collection's name is resolved against: it must end in a slash.
    private static Uri Folder(Uri uri)
        => uri.AbsolutePath.EndsWith('/') ? uri : new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;

    private static Uri Endpoint(Func<string, string?> get, string key, Uri fallback)
    {
        var text = get(key)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"{key} must be an absolute http(s) URL, got '{text}'.");
        }

        return uri;
    }
}

/// <summary>
/// Everything a host needs to connect outside services (activity plan §3.1): the Oura app, and
/// the key its tokens are encrypted under. Oura is optional — without <c>Oura:ClientId</c> the
/// host runs and the Activity page says Oura isn't set up — but a client id without
/// <c>Integrations:TokenKey</c> fails startup, since tokens would have nowhere safe to go.
/// </summary>
public sealed record IntegrationSettings(OuraOptions? Oura, TokenCipher? TokenCipher)
{
    public static readonly IntegrationSettings None = new(null, null);

    /// <summary>Reads and checks the settings through <paramref name="get"/> (a configuration lookup by <c>Oura:…</c> key).</summary>
    public static IntegrationSettings Resolve(Func<string, string?> get)
    {
        var cipher = TokenCipher.Resolve(get);
        var oura = OuraOptions.Resolve(get);
        if (oura is not null && cipher is null)
        {
            throw new InvalidOperationException(
                $"{OuraOptions.ClientIdKey} is set but {TokenCipher.KeyKey} isn't, so Oura's tokens can't be stored. "
                + $"Make a key with {TokenCipher.MakeKeyCommand}");
        }

        return new IntegrationSettings(oura, cipher);
    }

    /// <summary>
    /// Reads the settings from environment variables (<c>Oura__ClientId</c>,
    /// <c>Integrations__TokenKey</c>, …), as the CLI does. Names match without regard to case.
    /// </summary>
    public static IntegrationSettings FromEnvironment()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        return Resolve(key => configuration[key]);
    }
}

using System.Globalization;

namespace LifeOs.Infrastructure.Integrations;

/// <summary>
/// Where LifeOS's server reaches SuperJournal (D19). Server-only: the browser never calls it.
/// <list type="bullet">
/// <item><c>SuperJournal:BaseUrl</c> — SuperJournal's root, under which its <c>api/v1/…</c>
/// paths are: on the BlueSkies app that is <c>https://…/api/journal/</c>. Without it, SuperJournal
/// is off and the journal endpoints say it isn't set up. It must be https, or http to this
/// machine (a local SuperJournal for development), since every call carries a signed token.</item>
/// <item><c>SuperJournal:TimeoutSeconds</c> — how long a call may take, 15 by default (1–120). An
/// upload or download may take up to <see cref="TransferTimeout"/> instead.</item>
/// <item><c>SuperJournal:SigningKey</c> — the ES256 private key LifeOS signs its tokens with (a
/// Fly secret; SuperJournal holds the public key). Read by the API host, which signs.</item>
/// </list>
/// </summary>
public sealed record SuperJournalOptions(Uri BaseUrl, TimeSpan Timeout)
{
    public const string BaseUrlKey = "SuperJournal:BaseUrl";
    public const string TimeoutKey = "SuperJournal:TimeoutSeconds";
    public const string SigningKeyKey = "SuperJournal:SigningKey";

    /// <summary>
    /// LifeOS's app at SuperJournal: the <c>App</c> of the <c>lifeos</c> issuer in its
    /// <c>Jwt:Issuers</c>. Lists are narrowed to it, so they see only LifeOS's journals.
    /// </summary>
    public const string App = "lifeos";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The most an upload or a download may take: 25 MB over a slow phone connection.</summary>
    public static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads the settings through <paramref name="get"/>, or null when there's no base URL.
    /// Throws <see cref="InvalidOperationException"/> for a base URL that isn't https (or http to
    /// this machine), or a timeout that isn't a whole number of seconds from 1 to 120.
    /// </summary>
    public static SuperJournalOptions? Resolve(Func<string, string?> get)
    {
        var text = get(BaseUrlKey)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{BaseUrlKey} must be an absolute https URL (or http to localhost), with no query, got '{text}'.");
        }

        var timeout = DefaultTimeout;
        var seconds = get(TimeoutKey)?.Trim();
        if (!string.IsNullOrEmpty(seconds))
        {
            if (!int.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 120)
            {
                throw new InvalidOperationException($"{TimeoutKey} must be a whole number of seconds from 1 to 120, got '{seconds}'.");
            }

            timeout = TimeSpan.FromSeconds(value);
        }

        // A base the api/v1/… paths are resolved against: it must end in a slash.
        var folder = uri.AbsolutePath.EndsWith('/') ? uri : new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;
        return new SuperJournalOptions(folder, timeout);
    }
}

/// <summary>
/// Signs the bearer token each SuperJournal call carries, for the person it's made for. The API
/// host's signer (ES256, <c>iss</c> <c>lifeos</c>, <c>aud</c> <c>superjournal</c>, 5 minutes) is the
/// one implementation; tests use their own.
/// </summary>
public interface ISuperJournalTokens
{
    /// <summary>A fresh token whose <c>sub</c> is <paramref name="subject"/>, the person's Supabase user id.</summary>
    string TokenFor(string subject);
}

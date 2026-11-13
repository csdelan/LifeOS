using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// Outside services: Oura and Unsplash, and the connections and tokens LifeOS keeps for them.

/// <summary>
/// Tokens from an OAuth token endpoint (activity plan §3.1). Secrets: never logged and never
/// answered by the API, so <see cref="ToString"/> leaves them out.
/// </summary>
/// <param name="Scopes">The scopes granted, without Oura's <c>extapi:</c> prefix.</param>
public sealed record OAuthTokens(
    string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, IReadOnlyList<string> Scopes)
{
    public override string ToString()
        => $"OAuthTokens {{ ExpiresAt = {ExpiresAt:O}, Scopes = [{string.Join(' ', Scopes)}] }}";
}

/// <summary>
/// Oura's API v2 (activity plan §3.1, §3.3): the OAuth calls and the collections LifeOS keeps
/// (§3.9 added the tags). Errors: <see cref="OuraGrantException"/> when Oura refuses a grant,
/// <see cref="OuraAccessDeniedException"/> when it refuses an access token (401) or access
/// (403), <see cref="OuraUnavailableException"/> when it can't be reached or fails.
/// <para>
/// A collection read asks for the days [<c>start</c>, <c>end</c>] as Oura filters them —
/// which differs by collection — and pages through <c>next_token</c>; the caller widens the
/// window and filters on each record's day. A record without what identifies it (its day, or
/// a workout's, sleep period's or tag's id) is skipped.
/// </para>
/// </summary>
public interface IOuraClient
{
    /// <summary>
    /// Where to send the browser to approve LifeOS: Oura's authorize URL with this client,
    /// the scopes LifeOS reads, <paramref name="state"/> and the S256 PKCE challenge.
    /// </summary>
    Uri AuthorizeUrl(string state, string codeChallenge, Uri redirectUri);

    /// <summary>Trades the code Oura sent back for tokens.</summary>
    Task<OAuthTokens> ExchangeCodeAsync(
        string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken = default);

    /// <summary>Spends <paramref name="refreshToken"/> (single-use) for a new pair.</summary>
    Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Asks Oura to revoke the grant behind <paramref name="accessToken"/>.</summary>
    Task RevokeAsync(string accessToken, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraActivityDay>> GetDailyActivityAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraWorkout>> GetWorkoutsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraSleepScore>> GetDailySleepAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraReadiness>> GetDailyReadinessAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraSleepPeriod>> GetSleepPeriodsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>The tags Chris added in the Oura app (<c>enhanced_tag</c>); needs the <c>tag</c> scope.</summary>
    Task<IReadOnlyList<OuraTag>> GetTagsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);
}

/// <summary>
/// Unsplash's API (cover-images plan §3.2): search, one photo, and the download counter. Only
/// photos whose image is on <c>images.unsplash.com</c> come back, so nothing can be chosen
/// that LifeOS won't show. Errors are <see cref="UnsplashException"/>, in words, never with
/// the key.
/// </summary>
public interface IUnsplashClient
{
    /// <summary>The <c>utm_source</c> on every link back to Unsplash (<c>Unsplash:AppName</c>).</summary>
    string AppName { get; }

    /// <summary>
    /// Landscape photos matching <paramref name="query"/>, a page at a time (1-based), with
    /// Unsplash's strict content filter.
    /// </summary>
    Task<UnsplashSearchPage> SearchAsync(
        string query, int page, int perPage, CancellationToken cancellationToken = default);

    /// <summary>One photo by its id; <see cref="UnsplashFailure.NotFound"/> when Unsplash no longer has it.</summary>
    Task<UnsplashPhoto> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells Unsplash a photo was used (its <c>download_location</c>), as its guidelines ask
    /// when a photo becomes a header. A counter for the photographer, nothing more.
    /// </summary>
    Task TrackDownloadAsync(Uri downloadLocation, CancellationToken cancellationToken = default);
}

/// <summary>
/// A photo as Unsplash describes it, trimmed to what a cover needs. <see cref="RawUrl"/> is
/// <c>urls.raw</c>; <see cref="DownloadLocation"/> is <c>links.download_location</c>.
/// </summary>
public sealed record UnsplashPhoto(
    string Id,
    string RawUrl,
    string? Color,
    int Width,
    int Height,
    string? Description,
    string? AltDescription,
    string Photographer,
    string PhotographerUrl,
    string PhotoUrl,
    Uri? DownloadLocation);

/// <summary>One page of a search: how many photos and pages Unsplash has, and this page's photos.</summary>
public sealed record UnsplashSearchPage(int Total, int TotalPages, IReadOnlyList<UnsplashPhoto> Photos);

/// <summary>
/// A connect in progress (activity plan §3.1): the OAuth <c>state</c> sent to Oura, the PKCE
/// verifier and redirect URI the code exchange needs, and when it lapses.
/// </summary>
public sealed record PendingConnect(string State, string CodeVerifier, Uri RedirectUri, DateTimeOffset Until)
{
    public override string ToString() => $"PendingConnect {{ RedirectUri = {RedirectUri}, Until = {Until:O} }}";
}

/// <summary>
/// A connection's state as stored (<c>bsk_ops.integration</c>, migration 0036), without its
/// tokens.
/// </summary>
/// <param name="Connected">Tokens are stored (they may still need reconnecting).</param>
/// <param name="HistoryFrom">The earliest day the history backfill has reached.</param>
/// <param name="HistoryDone">The backfill found where the history starts.</param>
public sealed record IntegrationState(
    string Service,
    bool Connected,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    bool NeedsReconnect,
    string? ReconnectReason,
    DateTimeOffset? LastSyncedAt,
    string? LastSyncError,
    DateOnly? HistoryFrom,
    bool HistoryDone);

/// <summary>
/// Connections to outside services (<c>bsk_ops.integration</c>, migration 0036). Tokens are
/// encrypted at rest; the repository encrypts and decrypts them, so callers only ever see
/// <see cref="OAuthTokens"/>.
/// </summary>
public interface IIntegrationRepository
{
    /// <summary>The connection's state, or null when the service has never been connected.</summary>
    Task<IntegrationState?> GetAsync(string service, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps <paramref name="pending"/> as the connect in progress, replacing any earlier one.
    /// Stored tokens are left alone until the new ones arrive.
    /// </summary>
    Task BeginConnectAsync(string service, PendingConnect pending, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the connect in progress whose state is <paramref name="state"/>, clearing it so it
    /// can't be used twice: null when there's none with that state. The caller checks
    /// <see cref="PendingConnect.Until"/>.
    /// </summary>
    Task<PendingConnect?> TakePendingAsync(string service, string state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores newly granted tokens: connected as of <paramref name="connectedAt"/>, no longer
    /// needing to reconnect. A grant with a scope the stored one didn't have starts the history
    /// backfill over, so the new collection's past comes in (activity plan §3.9).
    /// </summary>
    Task SaveConnectionAsync(
        string service, OAuthTokens tokens, DateTimeOffset connectedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the connection's row until the lock commits or is disposed (rolled back), so a
    /// single-use refresh token is spent once: null when the service has no row.
    /// </summary>
    Task<IIntegrationLock?> LockAsync(string service, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a sync that finished at <paramref name="syncedAt"/>, clearing the last error. The
    /// history only grows: <paramref name="historyFrom"/> moves it earlier, never later, and
    /// once done it stays done. Nothing happens when the service has no row.
    /// </summary>
    Task RecordSyncAsync(
        string service, DateTimeOffset syncedAt, DateOnly? historyFrom, bool historyDone,
        CancellationToken cancellationToken = default);

    /// <summary>Records why the last sync failed, in words; the last good sync stays as it was.</summary>
    Task RecordSyncErrorAsync(string service, string error, CancellationToken cancellationToken = default);
}

/// <summary>
/// A connection locked for a refresh or a disconnect (<see cref="IIntegrationRepository.LockAsync"/>).
/// Changes take effect at <see cref="CommitAsync"/>; disposing without it rolls them back.
/// </summary>
public interface IIntegrationLock : IAsyncDisposable
{
    /// <summary>The stored tokens, or null when there are none or they can't be read.</summary>
    OAuthTokens? Tokens { get; }

    /// <summary>Tokens are stored but can't be decrypted with this host's token key.</summary>
    bool TokensUnreadable { get; }

    bool NeedsReconnect { get; }

    /// <summary>Replaces the stored tokens with a refreshed pair.</summary>
    Task SaveTokensAsync(OAuthTokens tokens, CancellationToken cancellationToken = default);

    /// <summary>Marks the connection as needing to reconnect, saying why.</summary>
    Task MarkReconnectAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>Forgets the tokens and any connect in progress (a disconnect). What was synced stays.</summary>
    Task ClearTokensAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

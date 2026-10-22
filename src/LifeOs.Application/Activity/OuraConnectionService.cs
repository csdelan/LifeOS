using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>
/// The Oura connection (ACTIVITY-1, activity plan §3.1): connecting with OAuth (server-side,
/// with PKCE), keeping the access token fresh, and disconnecting.
/// <list type="bullet">
/// <item><b>Connect</b> is two steps. <see cref="BeginConnectAsync"/> keeps a random
/// <c>state</c> and PKCE verifier for ten minutes and answers Oura's authorize URL;
/// <see cref="ConnectAsync"/> takes the code Oura sent back, checks the state (once only),
/// and trades the code for tokens.</item>
/// <item><b>Refresh tokens are single-use</b>, so <see cref="GetAccessTokenAsync"/> refreshes
/// under a lock on the connection's row and saves the new pair before letting go: the API and
/// the CLI can't both spend one.</item>
/// <item><b>Oura refusing the stored sign-in</b> (<c>invalid_grant</c>) marks the connection
/// as needing to reconnect, and nothing calls Oura until it's connected again. Oura being
/// down leaves everything as it was.</item>
/// <item><b>Disconnect</b> revokes the token at Oura, if it can, and forgets it. What was
/// synced stays: the record is append-only.</item>
/// </list>
/// Without an <see cref="IOuraClient"/> (no <c>Oura:ClientId</c> on this host) the status says
/// Oura isn't set up, and everything else refuses in words.
/// </summary>
public sealed class OuraConnectionService(
    IIntegrationRepository integrations, IOuraClient? client, IClock clock, string service = Integrations.Oura)
{
    /// <summary>The scopes LifeOS asks for: daily summaries (activity, sleep, readiness) and workouts.</summary>
    public static readonly IReadOnlyList<string> Scopes = ["daily", "workout"];

    /// <summary>The SPA route Oura sends the browser back to.</summary>
    public const string CallbackPath = "/activity/connected";

    /// <summary>How long a connect in progress waits for Oura to send the browser back.</summary>
    public static readonly TimeSpan PendingFor = TimeSpan.FromMinutes(10);

    /// <summary>An access token this close to expiring is refreshed before it's used.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private const int MaxCodeLength = 2048;

    public bool Configured => client is not null;

    /// <summary>The connection as the Activity page shows it. Never carries a token.</summary>
    public async Task<OuraStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => OuraStatus.From(Configured, await integrations.GetAsync(service, cancellationToken));

    /// <summary>
    /// Starts connecting: keeps a new state and PKCE verifier for <see cref="PendingFor"/> and
    /// answers the URL to send the browser to. <paramref name="redirectUri"/> is the SPA's
    /// <see cref="CallbackPath"/> (registered with Oura): https, or http on this machine.
    /// </summary>
    public async Task<Uri> BeginConnectAsync(Uri redirectUri, CancellationToken cancellationToken = default)
    {
        var oura = RequireClient();
        CheckRedirect(redirectUri);

        var state = RandomToken();
        var verifier = RandomToken();
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        await integrations.BeginConnectAsync(
            service, new PendingConnect(state, verifier, redirectUri, clock.UtcNow + PendingFor), cancellationToken);
        return oura.AuthorizeUrl(state, challenge, redirectUri);
    }

    /// <summary>
    /// Finishes connecting with the <paramref name="code"/> and <paramref name="state"/> Oura
    /// sent back: the state must match the connect in progress and still be fresh, and can be
    /// used once. Stores the tokens and answers the new status.
    /// </summary>
    public async Task<OuraStatus> ConnectAsync(string code, string state, CancellationToken cancellationToken = default)
    {
        var oura = RequireClient();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("Connecting Oura takes the code and state Oura sent back.");
        }

        if (code.Length > MaxCodeLength || state.Length > MaxCodeLength)
        {
            throw new ArgumentException("That code or state is too long to be Oura's.");
        }

        var pending = await integrations.TakePendingAsync(service, state, cancellationToken);
        if (pending is null || pending.Until <= clock.UtcNow)
        {
            throw new OuraConnectionException(
                "That Oura sign-in has expired or doesn't match the one LifeOS started. Press Connect Oura again.");
        }

        OAuthTokens tokens;
        try
        {
            tokens = await oura.ExchangeCodeAsync(code, pending.CodeVerifier, pending.RedirectUri, cancellationToken);
        }
        catch (OuraGrantException ex)
        {
            throw new OuraConnectionException($"{ex.Message} Press Connect Oura again.", ex);
        }

        await integrations.SaveConnectionAsync(service, tokens, clock.UtcNow, cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    /// <summary>
    /// An access token good for at least <see cref="RefreshMargin"/>, refreshing first when the
    /// stored one is closer to expiring than that.
    /// </summary>
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => UseTokensAsync(force: false, cancellationToken);

    /// <summary>Refreshes the tokens now, however fresh they are (<c>bsk oura refresh</c>), and answers the new status.</summary>
    public async Task<OuraStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await UseTokensAsync(force: true, cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    /// <summary>
    /// Disconnects: revokes the token at Oura when it can (a refusal or an outage there doesn't
    /// stop it) and forgets the tokens. Answers the new status.
    /// </summary>
    public async Task<OuraStatus> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await using (var locked = await integrations.LockAsync(service, cancellationToken))
        {
            if (locked is not null)
            {
                if (client is not null && locked.Tokens is { } tokens)
                {
                    try
                    {
                        await client.RevokeAsync(tokens.AccessToken, cancellationToken);
                    }
                    catch (Exception ex) when (ex is OuraConnectionException or OuraUnavailableException)
                    {
                        // Best effort: the token is forgotten here either way, and lapses at Oura.
                    }
                }

                await locked.ClearTokensAsync(cancellationToken);
                await locked.CommitAsync(cancellationToken);
            }
        }

        return await GetStatusAsync(cancellationToken);
    }

    private async Task<string> UseTokensAsync(bool force, CancellationToken cancellationToken)
    {
        var oura = RequireClient();
        await using var locked = await integrations.LockAsync(service, cancellationToken)
            ?? throw NotConnected();

        if (locked.NeedsReconnect)
        {
            throw new OuraConnectionException("Oura needs reconnecting: press Reconnect Oura on the Activity page.");
        }

        if (locked.TokensUnreadable)
        {
            const string reason = "The stored sign-in can't be read with this server's Integrations:TokenKey.";
            await locked.MarkReconnectAsync(reason, cancellationToken);
            await locked.CommitAsync(cancellationToken);
            throw new OuraConnectionException($"{reason} Reconnect Oura.");
        }

        var tokens = locked.Tokens ?? throw NotConnected();
        if (!force && tokens.ExpiresAt > clock.UtcNow + RefreshMargin)
        {
            return tokens.AccessToken;
        }

        OAuthTokens fresh;
        try
        {
            fresh = await oura.RefreshAsync(tokens.RefreshToken, cancellationToken);
        }
        catch (OuraGrantException ex) when (ex.SpentGrant)
        {
            await locked.MarkReconnectAsync(ex.Message, cancellationToken);
            await locked.CommitAsync(cancellationToken);
            throw new OuraConnectionException($"{ex.Message} Reconnect Oura on the Activity page.", ex);
        }

        // A refresh may not repeat the scopes; they're the original grant's.
        if (fresh.Scopes.Count == 0)
        {
            fresh = fresh with { Scopes = tokens.Scopes };
        }

        await locked.SaveTokensAsync(fresh, cancellationToken);
        await locked.CommitAsync(cancellationToken);
        return fresh.AccessToken;
    }

    private IOuraClient RequireClient()
        => client ?? throw new OuraConnectionException(
            "Oura isn't set up on this server: it needs Oura__ClientId, Oura__ClientSecret and Integrations__TokenKey.");

    private static OuraConnectionException NotConnected()
        => new("Oura isn't connected: press Connect Oura on the Activity page.");

    private static void CheckRedirect(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        var local = redirectUri.IsAbsoluteUri && redirectUri.IsLoopback;
        if (!redirectUri.IsAbsoluteUri
            || !(redirectUri.Scheme == Uri.UriSchemeHttps || (redirectUri.Scheme == Uri.UriSchemeHttp && local))
            || redirectUri.AbsolutePath != CallbackPath
            || redirectUri.Query.Length > 0
            || redirectUri.Fragment.Length > 0
            || redirectUri.UserInfo.Length > 0)
        {
            throw new ArgumentException(
                $"Oura sends the browser back to the Activity page's {CallbackPath} over https (http only on this machine), got '{redirectUri}'.");
        }
    }

    // 32 random bytes in base64url: 43 characters, a valid PKCE verifier and an unguessable state.
    private static string RandomToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}

/// <summary>
/// The Oura connection as the Activity page shows it (activity plan §3.1). Never carries a token.
/// </summary>
/// <param name="State">
/// <c>not_configured</c> (no Oura app on this server), <c>not_connected</c>, <c>reconnect</c>
/// (Oura refused the stored sign-in) or <c>connected</c>.
/// </param>
/// <param name="MissingScopes">Scopes LifeOS asked for that Oura didn't grant.</param>
public sealed record OuraStatus(
    string State,
    bool Configured,
    bool Connected,
    bool NeedsReconnect,
    string? ReconnectReason,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> MissingScopes,
    DateTimeOffset? LastSyncedAt,
    string? LastSyncError)
{
    public const string NotConfigured = "not_configured";
    public const string NotConnected = "not_connected";
    public const string Reconnect = "reconnect";
    public const string ConnectedState = "connected";

    public static OuraStatus From(bool configured, IntegrationState? stored)
    {
        var connected = stored?.Connected ?? false;
        var reconnect = stored?.NeedsReconnect ?? false;
        var scopes = stored?.Scopes ?? [];
        var state = !configured ? NotConfigured
            : reconnect ? Reconnect
            : connected ? ConnectedState
            : NotConnected;
        var missing = connected
            ? OuraConnectionService.Scopes.Where(s => !scopes.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList()
            : [];
        return new OuraStatus(
            state, configured, connected, reconnect, stored?.ReconnectReason, stored?.ConnectedAt,
            stored?.ExpiresAt, scopes, missing, stored?.LastSyncedAt, stored?.LastSyncError);
    }
}

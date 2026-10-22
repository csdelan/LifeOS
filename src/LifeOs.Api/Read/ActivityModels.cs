using LifeOs.Application.Activity;

namespace LifeOs.Api.Read;

/// <summary>
/// <c>GET /api/oura/status</c> (ACTIVITY-1, activity plan §3.1): the Oura connection as the
/// Activity page shows it. It never carries a token.
/// </summary>
public sealed class OuraStatusRead
{
    /// <summary>
    /// <c>not_configured</c> (no Oura app on this server), <c>not_connected</c>,
    /// <c>reconnect</c> (Oura refused the stored sign-in) or <c>connected</c>.
    /// </summary>
    public string State { get; set; } = "";

    public bool Configured { get; set; }
    public bool Connected { get; set; }
    public bool NeedsReconnect { get; set; }

    /// <summary>Why Oura needs reconnecting, in words.</summary>
    public string? ReconnectReason { get; set; }

    public DateTimeOffset? ConnectedAt { get; set; }

    /// <summary>When the current access token lapses; it's refreshed before then.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>The scopes Oura granted (<c>daily</c>, <c>workout</c>).</summary>
    public IReadOnlyList<string> Scopes { get; set; } = [];

    /// <summary>Scopes LifeOS asked for that Oura didn't grant: the page asks to reconnect with them.</summary>
    public IReadOnlyList<string> MissingScopes { get; set; } = [];

    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }

    public static OuraStatusRead From(OuraStatus status) => new()
    {
        State = status.State,
        Configured = status.Configured,
        Connected = status.Connected,
        NeedsReconnect = status.NeedsReconnect,
        ReconnectReason = status.ReconnectReason,
        ConnectedAt = status.ConnectedAt,
        ExpiresAt = status.ExpiresAt,
        Scopes = status.Scopes,
        MissingScopes = status.MissingScopes,
        LastSyncedAt = status.LastSyncedAt,
        LastSyncError = status.LastSyncError,
    };
}

/// <summary><c>POST /api/oura/authorize</c>: where to send the browser to approve LifeOS at Oura.</summary>
public sealed class OuraAuthorizeRead
{
    public string AuthorizeUrl { get; set; } = "";
}

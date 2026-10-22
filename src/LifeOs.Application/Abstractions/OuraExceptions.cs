namespace LifeOs.Application.Abstractions;

/// <summary>
/// The Oura connection can't do what was asked: Oura isn't set up on this server, isn't
/// connected, needs reconnecting, or refused a sign-in. The message says what to do, in words,
/// and never carries a token. The API answers 409.
/// </summary>
public class OuraConnectionException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Oura's token endpoint refused a grant (an OAuth error such as <c>invalid_grant</c>).
/// <see cref="SpentGrant"/> says whether the grant itself is dead, so the connection needs
/// reconnecting; otherwise (a bad client id or secret, say) the stored tokens are untouched.
/// </summary>
public sealed class OuraGrantException(string error, string message, bool spentGrant)
    : OuraConnectionException(message)
{
    /// <summary>The OAuth <c>error</c> code, or <c>http_&lt;status&gt;</c> when Oura gave none.</summary>
    public string Error { get; } = error;

    /// <summary>The refresh token or code is spent, revoked or expired: only reconnecting helps.</summary>
    public bool SpentGrant { get; } = spentGrant;
}

/// <summary>
/// Oura couldn't be reached, timed out, failed (5xx), limited requests (429) or answered
/// something LifeOS can't read. Nothing about it is the caller's fault and nothing stored has
/// changed, so the API answers 502.
/// </summary>
public sealed class OuraUnavailableException(string message, TimeSpan? retryAfter = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>How long Oura asked LifeOS to wait (a 429's <c>Retry-After</c>), when it said.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

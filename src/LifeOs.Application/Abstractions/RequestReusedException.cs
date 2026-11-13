namespace LifeOs.Application.Abstractions;

/// <summary>
/// A request id (an event's external id, which a door such as <c>/mcp</c> sends so a retried
/// request lands on its first write) already used for a different write (code review CR68).
/// Nothing was written. A conflict, so the API answers 409.
/// </summary>
public sealed class RequestReusedException(string message) : KernelRefusalException(RefusalKind.Conflict, message)
{
    /// <summary>
    /// Refuses <paramref name="written"/> when it's an earlier event of another kind than
    /// <paramref name="kind"/> under <paramref name="externalId"/>.
    /// </summary>
    public static void ThrowIfOtherKind(string written, string kind, string? externalId)
    {
        if (written != kind)
        {
            throw new RequestReusedException(
                $"request_id '{externalId}' was already used for a different write. Send a new UUID for each write.");
        }
    }
}

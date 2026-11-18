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

/// <summary>
/// A write its source already made under this request id, found as it was written: a retry that
/// raced its first call. Nothing was written. The service that made the write catches it and
/// answers the first write, so it reaches a door only from a store used without one. A conflict,
/// so the API answers 409.
/// </summary>
public sealed class RequestAlreadyWrittenException(string sourceId, string externalId)
    : KernelRefusalException(RefusalKind.Conflict, $"request_id '{externalId}' was already written; read it again.")
{
    public string SourceId { get; } = sourceId;

    public string ExternalId { get; } = externalId;
}

namespace LifeOs.Application.Abstractions;

/// <summary>
/// What kind of refusal a <see cref="KernelRefusalException"/> is. Each door says it its own way
/// from one table (<c>KernelRefusals</c> in the API: an HTTP status for <c>/api</c>, an error in
/// words for <c>/mcp</c>), so a new refusal says its kind and needs nothing added to a door.
/// </summary>
public enum RefusalKind
{
    /// <summary>The request can't be done as asked: a bad value, or a state that doesn't allow it. 400.</summary>
    Invalid,

    /// <summary>What the request names isn't there. 404.</summary>
    NotFound,

    /// <summary>
    /// It clashes with what is there now: a duplicate, a name that matches more than one thing,
    /// or a write another write changed first. Nothing was written; reading again and retrying
    /// is safe. 409.
    /// </summary>
    Conflict,

    /// <summary>Something this server needs isn't set up or connected (Oura, Unsplash). 409.</summary>
    NotSetUp,

    /// <summary>
    /// A service LifeOS depends on (object storage, Oura, Unsplash) failed or couldn't be reached.
    /// Nothing about it is the caller's fault and nothing stored has changed. 502.
    /// </summary>
    Unavailable,
}

/// <summary>
/// The kernel refusing a request in words (code review CR26): the message is for the person or
/// agent who asked, and <see cref="Kind"/> says how each door answers. Every exception the
/// Application layer defines derives from this; a test checks that one it adds does too.
/// </summary>
/// <remarks>
/// A plain <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/> thrown by
/// LifeOS's own code is also read as an <see cref="RefusalKind.Invalid"/> refusal, the convention
/// most services follow. The same types thrown by a library (Npgsql, the framework) are faults.
/// </remarks>
public abstract class KernelRefusalException(RefusalKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public RefusalKind Kind { get; } = kind;
}

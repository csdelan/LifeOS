using System.Reflection;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;

namespace LifeOs.Api.Http;

/// <summary>A refusal as a door says it: the HTTP status, the words, and any candidates to choose from.</summary>
public sealed record Refusal(int Status, string Message, object? Candidates = null);

/// <summary>
/// The one table from kernel refusals to answers (code review CR26). <c>/api</c> answers with
/// <see cref="Refusal.Status"/> and the words (<see cref="KernelExceptionHandler"/>); <c>/mcp</c>
/// answers with an error in the same words (<c>McpHost</c>'s call filter). Anything that isn't a
/// refusal is a fault: <c>/api</c> answers 500 with <see cref="FaultMessage"/>, not the exception's
/// own text, and <c>/mcp</c> lets the SDK say its generic error. Both log it.
/// </summary>
public static class KernelRefusals
{
    /// <summary>What a fault says. The exception itself goes to the log, where its text belongs.</summary>
    public const string FaultMessage = "Something went wrong on the server. It has been logged.";

    /// <summary>The refusal <paramref name="exception"/> is, or null when it's a fault.</summary>
    public static Refusal? Describe(Exception? exception) => exception switch
    {
        AmbiguousSubjectException ambiguous => new(
            StatusFor(ambiguous.Kind),
            ambiguous.Message,
            ambiguous.Candidates.Select(c => new { id = c.Id, urn = c.Urn, type = c.Type, title = c.Title }).ToList()),
        KernelRefusalException refusal => new(StatusFor(refusal.Kind), refusal.Message),
        BadHttpRequestException bad => new(bad.StatusCode, bad.Message),
        ArgumentException or InvalidOperationException when ThrownByLifeOs(exception) => new(
            StatusCodes.Status400BadRequest, exception.Message),
        _ => null,
    };

    /// <summary>The HTTP status for each <see cref="RefusalKind"/>.</summary>
    public static int StatusFor(RefusalKind kind) => kind switch
    {
        RefusalKind.Invalid => StatusCodes.Status400BadRequest,
        RefusalKind.NotFound => StatusCodes.Status404NotFound,
        RefusalKind.Conflict or RefusalKind.NotSetUp => StatusCodes.Status409Conflict,
        RefusalKind.Unavailable => StatusCodes.Status502BadGateway,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A refusal kind with no status."),
    };

    /// <summary>
    /// Whether LifeOS's own code threw <paramref name="exception"/>: the services refuse with a
    /// plain <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/> by
    /// convention, and those words are for the caller. Npgsql and the framework throw the same
    /// types for bugs (a closed connection, an empty sequence), and those are faults. So is a
    /// guard such as <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/>, which
    /// throws from the framework.
    /// </summary>
    private static bool ThrownByLifeOs(Exception exception)
        => exception.TargetSite?.DeclaringType?.Assembly is { } assembly && IsLifeOs(assembly);

    private static bool IsLifeOs(Assembly assembly)
        => assembly.GetName().Name is { } name
           && (name == "LifeOs" || name.StartsWith("LifeOs.", StringComparison.Ordinal));
}

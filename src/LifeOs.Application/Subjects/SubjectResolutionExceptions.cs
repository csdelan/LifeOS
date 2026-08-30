using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>Thrown when a subject reference (urn, short id, or title) matches nothing.</summary>
public sealed class SubjectNotFoundException(string reference)
    : Exception($"No subject matches '{reference}'.")
{
    public string Reference { get; } = reference;
}

/// <summary>
/// Thrown when a fuzzy reference matches more than one subject. Carries the
/// candidates so the caller can show them rather than guess which was meant.
/// </summary>
public sealed class AmbiguousSubjectException(string reference, IReadOnlyList<SubjectRef> candidates)
    : Exception(BuildMessage(reference, candidates))
{
    public string Reference { get; } = reference;

    public IReadOnlyList<SubjectRef> Candidates { get; } = candidates;

    private static string BuildMessage(string reference, IReadOnlyList<SubjectRef> candidates)
    {
        var lines = candidates.Select(c => $"  {c.Urn}  ({c.Type}) {c.Title}");
        return $"'{reference}' is ambiguous; {candidates.Count} subjects match:\n"
               + string.Join('\n', lines);
    }
}

using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Logs an activity and relates it to a Commitment as either evidence or a breach
/// (<c>bsk log activity</c>) — the mechanism behind breach detection.
///
/// The event carries only its text; the evidences/violates edges are recorded in
/// <c>bsk.subject_event</c> as event → subject edges, because an activity is an
/// event, not a subject. This is the domain-generality seam — later, imported trade
/// rows become the same <c>activity</c> events, some evidencing and some violating a
/// Commitment such as "never add to a losing position", answered by the same SQL
/// over <c>subject_event WHERE relation = 'violates'</c>.
/// </summary>
public sealed class ActivityService(SubjectService subjects, IActivityWriter writer, IClock clock, string sourceId)
{
    public async Task<ActivityResult> LogAsync(
        ActivityInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
        {
            throw new ArgumentException("Activity text must not be empty.", nameof(input));
        }

        // Resolve (and type-check) the referenced Commitments before writing anything.
        // Ids are de-duplicated so naming the same commitment twice — e.g. once by urn
        // and once by title — records one edge rather than tripping the unique index.
        var evidences = await ResolveCommitmentsAsync(input.Evidences, cancellationToken);
        var violates = await ResolveCommitmentsAsync(input.Violates, cancellationToken);

        var edges = new List<SubjectEventEdge>(evidences.Count + violates.Count);
        edges.AddRange(evidences.Select(id =>
            new SubjectEventEdge(id, SubjectEventRelations.Evidences, Provenances.Declared)));
        edges.AddRange(violates.Select(id =>
            new SubjectEventEdge(id, SubjectEventRelations.Violates, Provenances.Declared)));

        var payload = JsonSerializer.Serialize(new { text = input.Text.Trim() });
        var now = clock.UtcNow;
        var activityEvent = new NewEvent(
            Kind: EventKinds.Activity,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload);

        // The event and its edges land together, or not at all (the stream is
        // append-only, so a half-written activity could never be repaired).
        var eventId = await writer.WriteAsync(activityEvent, edges, cancellationToken);
        return new ActivityResult(eventId, evidences, violates);
    }

    // Each named subject must be a Commitment — evidence and breaches are recorded
    // against commitments, so a mistyped reference is a clear error, not a silent
    // edge to the wrong kind of subject. De-duplicates on the resolved id so distinct
    // references to the same commitment collapse to one.
    private async Task<IReadOnlyList<Guid>> ResolveCommitmentsAsync(
        IReadOnlyList<string>? references, CancellationToken cancellationToken)
    {
        if (references is not { Count: > 0 })
        {
            return [];
        }

        var ids = new List<Guid>(references.Count);
        var seen = new HashSet<Guid>();
        foreach (var reference in references)
        {
            var subject = await subjects.ResolveAsync(reference, cancellationToken);
            if (subject.Type != SubjectTypes.Commitment)
            {
                throw new InvalidOperationException(
                    $"'{subject.Urn}' is a {subject.Type}, not a Commitment; " +
                    "activities evidence or violate Commitments.");
            }

            if (seen.Add(subject.Id))
            {
                ids.Add(subject.Id);
            }
        }

        return ids;
    }
}

/// <summary>The inputs to an activity log: free text plus optional commitment references.</summary>
public sealed record ActivityInput(
    string Text,
    IReadOnlyList<string>? Evidences = null,
    IReadOnlyList<string>? Violates = null);

/// <summary>The outcome of a logged activity: the event and the commitments it touches.</summary>
public sealed record ActivityResult(
    Guid EventId, IReadOnlyList<Guid> EvidencesCommitmentIds, IReadOnlyList<Guid> ViolatesCommitmentIds);

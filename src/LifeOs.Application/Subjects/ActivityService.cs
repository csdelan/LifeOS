using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Logs an activity and relates it to a Commitment as either evidence or a breach
/// (<c>bsk log activity</c>) — the mechanism behind breach detection.
///
/// The evidences/violates linkage lives in the event <em>payload</em>, not in the
/// subject–subject relation table: an activity is an event, not a subject, and this
/// is the domain-generality seam — later, imported trade rows become the same
/// <c>activity</c> events, some evidencing and some violating a Commitment such as
/// "never add to a losing position", answered by the same SQL over
/// <c>payload-&gt;'violates'</c>. The payload shape is
/// <c>{ "text": ..., "evidences": [commitment-id...], "violates": [commitment-id...] }</c>.
/// </summary>
public sealed class ActivityService(SubjectService subjects, IEventStore events, IClock clock, string sourceId)
{
    public async Task<ActivityResult> LogAsync(
        ActivityInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
        {
            throw new ArgumentException("Activity text must not be empty.", nameof(input));
        }

        var evidences = await ResolveCommitmentsAsync(input.Evidences, cancellationToken);
        var violates = await ResolveCommitmentsAsync(input.Violates, cancellationToken);

        var payload = new JsonObject { ["text"] = input.Text.Trim() };
        if (evidences.Count > 0)
        {
            payload["evidences"] = ToJsonArray(evidences);
        }

        if (violates.Count > 0)
        {
            payload["violates"] = ToJsonArray(violates);
        }

        var now = clock.UtcNow;
        var newEvent = new NewEvent(
            Kind: EventKinds.Activity,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload.ToJsonString());

        var eventId = await events.AppendAsync(newEvent, cancellationToken);
        return new ActivityResult(eventId, evidences, violates);
    }

    // Each named subject must be a Commitment — evidence and breaches are recorded
    // against commitments, so a mistyped reference is a clear error, not a silent
    // link to the wrong kind of subject.
    private async Task<IReadOnlyList<Guid>> ResolveCommitmentsAsync(
        IReadOnlyList<string>? references, CancellationToken cancellationToken)
    {
        if (references is not { Count: > 0 })
        {
            return [];
        }

        var ids = new List<Guid>(references.Count);
        foreach (var reference in references)
        {
            var subject = await subjects.ResolveAsync(reference, cancellationToken);
            if (subject.Type != SubjectTypes.Commitment)
            {
                throw new InvalidOperationException(
                    $"'{subject.Urn}' is a {subject.Type}, not a Commitment; " +
                    "activities evidence or violate Commitments.");
            }

            ids.Add(subject.Id);
        }

        return ids;
    }

    private static JsonArray ToJsonArray(IEnumerable<Guid> ids)
        => new(ids.Select(id => (JsonNode?)JsonValue.Create(id.ToString())).ToArray());
}

/// <summary>The inputs to an activity log: free text plus optional commitment references.</summary>
public sealed record ActivityInput(
    string Text,
    IReadOnlyList<string>? Evidences = null,
    IReadOnlyList<string>? Violates = null);

/// <summary>The outcome of a logged activity: the event and the commitments it touches.</summary>
public sealed record ActivityResult(
    Guid EventId, IReadOnlyList<Guid> EvidencesCommitmentIds, IReadOnlyList<Guid> ViolatesCommitmentIds);

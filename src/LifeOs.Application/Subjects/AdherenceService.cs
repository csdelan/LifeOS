using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Records how a habit occurrence turned out — the write path behind
/// <c>bsk adhere</c> (GEN-3 / D2). Each recording is an append-only activity event
/// carrying {habit, occurrence date, result, note}, plus an <c>evidences</c> edge
/// (followed / partial) or <c>violates</c> edge (missed) so the raw record attaches
/// to the habit. Occurrences and streaks are a projection over these events, so a
/// correction is simply a newer recording for the same date (GEN-5, latest wins);
/// nothing is mutated. Partial is fixed half-credit and is only allowed when the
/// habit's <c>allows_partial</c> flag is set.
/// </summary>
public sealed class AdherenceService(
    SubjectService subjects, ISubjectRepository repository, IActivityWriter writer, IClock clock, string sourceId)
{
    public async Task<AdherenceResult> RecordAsync(
        string habitReference, string result, DateOnly occurrence, string? note = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedResult = (result ?? "").Trim().ToLowerInvariant();
        if (!AdherenceResults.All.Contains(normalizedResult))
        {
            throw new ArgumentException(
                $"Unknown adherence result '{result}'. Expected one of: {string.Join(", ", AdherenceResults.All)}.",
                nameof(result));
        }

        var habit = await subjects.ResolveAsync(habitReference, cancellationToken);
        if (habit.Type != SubjectTypes.Habit)
        {
            throw new InvalidOperationException(
                $"'{habit.Urn}' is a {habit.Type}, not a Habit; adherence is recorded against Habits.");
        }

        if (normalizedResult == AdherenceResults.Partial && !await AllowsPartialAsync(habit.Id, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Habit '{habit.Urn}' does not allow partial credit. Set attributes.allows_partial to enable it.");
        }

        // Followed and partial support the habit; a miss violates it. The full/partial
        // distinction lives in the payload, keeping the edge vocabulary binary (D2).
        var relation = normalizedResult == AdherenceResults.Missed
            ? SubjectEventRelations.Violates
            : SubjectEventRelations.Evidences;

        var payload = JsonSerializer.Serialize(new
        {
            subject_id = habit.Id.ToString(),
            occurrence = occurrence.ToString("yyyy-MM-dd"),
            result = normalizedResult,
            note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        });

        var now = clock.UtcNow;
        var activityEvent = new NewEvent(
            Kind: EventKinds.Activity,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload);

        var eventId = await writer.WriteAsync(
            activityEvent,
            [new SubjectEventEdge(habit.Id, relation, Provenances.Declared)],
            cancellationToken);

        return new AdherenceResult(eventId, habit, normalizedResult, occurrence);
    }

    private async Task<bool> AllowsPartialAsync(Guid habitId, CancellationToken cancellationToken)
    {
        var value = await repository.GetAttributeValueAsync(habitId, "allows_partial", cancellationToken);
        return string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The outcome of recording adherence: the event, the habit, the result and the occurrence.</summary>
public sealed record AdherenceResult(Guid EventId, SubjectRef Habit, string Result, DateOnly Occurrence);

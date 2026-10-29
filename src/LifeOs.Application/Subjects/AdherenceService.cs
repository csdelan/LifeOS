using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
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
/// habit's <c>allows_partial</c> flag is set. A Planned habit isn't tracked yet, so it
/// takes no recording.
/// <para>
/// Every recording then reconciles the habit's rewards (REWARD-2, rewards plan §3.3): a
/// followed day earns, a partial one earns half, a run reaching 7 / 30 / 100 earns its
/// milestone, and a correction gives back whatever the record no longer supports.
/// </para>
/// </summary>
public sealed class AdherenceService(
    SubjectService subjects, ISubjectRepository repository, IActivityWriter writer, RewardService rewards,
    IClock clock, IHomeCalendar calendar, string sourceId)
{
    /// <summary>
    /// Records <paramref name="result"/> for the habit's occurrence on
    /// <paramref name="occurrence"/>. Without a date it's today in the home zone, never the
    /// server's: a check-off at 9 pm Pacific is that evening's, not tomorrow's in UTC.
    /// </summary>
    public async Task<AdherenceResult> RecordAsync(
        string habitReference, string result, DateOnly? occurrence = null, string? note = null,
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

        // A Planned habit isn't tracked yet (migration 0049): nothing is scheduled, and no day
        // is recorded until it moves to Building.
        if (StatusVocabulary.IsPlanned(await repository.GetStatusAsync(habit.Id, cancellationToken)))
        {
            throw new InvalidOperationException(
                $"Habit '{habit.Title}' is Planned, so it isn't tracked yet. Move it to Building to start tracking it, then record its days.");
        }

        if (normalizedResult == AdherenceResults.Partial && !await AllowsPartialAsync(habit.Id, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Habit '{habit.Urn}' does not allow partial credit. Set attributes.allows_partial to enable it.");
        }

        var now = clock.UtcNow;
        var day = occurrence ?? await calendar.DateAtAsync(now, cancellationToken);

        // Followed and partial support the habit; a miss violates it. The full/partial
        // distinction lives in the payload, keeping the edge vocabulary binary (D2).
        var relation = normalizedResult == AdherenceResults.Missed
            ? SubjectEventRelations.Violates
            : SubjectEventRelations.Evidences;

        var payload = JsonSerializer.Serialize(new
        {
            subject_id = habit.Id.ToString(),
            occurrence = day.ToString("yyyy-MM-dd"),
            result = normalizedResult,
            note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        });

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

        var earned = await rewards.ReconcileWithProgressAsync(habit, eventId, cancellationToken);
        return new AdherenceResult(eventId, habit, normalizedResult, day, earned.Awards, earned.Progress);
    }

    private async Task<bool> AllowsPartialAsync(Guid habitId, CancellationToken cancellationToken)
    {
        var value = await repository.GetAttributeValueAsync(habitId, "allows_partial", cancellationToken);
        return string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The outcome of recording adherence: the event, the habit, the result, the occurrence, any
/// ledger entries it earned or reversed, and each life domain whose level or rank that changed.
/// </summary>
public sealed record AdherenceResult(
    Guid EventId, SubjectRef Habit, string Result, DateOnly Occurrence, IReadOnlyList<Award> Awards,
    IReadOnlyList<DomainChange> Progress);

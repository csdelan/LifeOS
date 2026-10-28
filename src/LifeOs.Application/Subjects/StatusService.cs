using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Moves a subject's status by appending a <c>state_change</c> event
/// (<c>bsk status</c>). Status is never edited directly: this writes an event and
/// nothing else — the <c>subject_current</c> projection reflects it only after a
/// rebuild folds the event in (epic invariant 8). The status must be a word from the
/// subject type's vocabulary (<see cref="StatusVocabulary"/>, D7): matched
/// case-insensitively and stored in the vocabulary's casing.
/// <para>
/// Every status change then reconciles the subject's rewards (REWARD-2, rewards plan
/// §3.3): completing a Task, Project or Goal earns its bounty, and moving it off
/// Completed gives that back. This covers <c>bsk status</c>, the web's Change status and
/// the timer's Complete, which all come through here.
/// </para>
/// <para>
/// Repeating Tasks (D16): a series only repeats (Not started) or stops (Cancelled), and a
/// repetition that closes makes the next one (<see cref="TaskSeriesService"/>).
/// </para>
/// <para>
/// Closing a Task (a terminal status) stops its timer first when it's running or paused, so
/// its time is recorded up to then and no timer runs on a closed task (TASKS-2).
/// </para>
/// </summary>
public sealed class StatusService(
    SubjectService subjects, IEventStore events, RewardService rewards, IClock clock, string sourceId,
    TaskSeriesService? taskSeries = null, ITimerRepository? timers = null)
{
    public async Task<StatusResult> ChangeStatusAsync(
        string reference, string newStatus, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newStatus))
        {
            throw new ArgumentException("A status is required.", nameof(newStatus));
        }

        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        var status = VocabularyStatus(subject.Type, newStatus);

        var followUp = taskSeries is null
            ? TaskStatusFollowUp.None
            : await taskSeries.BeforeStatusAsync(subject, status, cancellationToken);

        var payload = JsonSerializer.Serialize(new { subject_id = subject.Id.ToString(), status });

        var now = clock.UtcNow;
        if (timers is not null && subject.Type == SubjectTypes.Task && StatusVocabulary.IsTerminal(status)
            && await timers.GetAsync(subject.Id, cancellationToken) is { State: TimerStates.Running or TimerStates.Paused } timer)
        {
            var stop = JsonSerializer.Serialize(new { subject_id = subject.Id.ToString(), timer = TimerActions.Stop });
            await timers.AppendAsync(
                [new TimerWrite(subject.Id, timer.State, new NewEvent(
                    Kind: EventKinds.Activity,
                    Provenance: Provenances.Declared,
                    OccurredAt: now,
                    RecordedAt: now,
                    SourceId: sourceId,
                    PayloadJson: stop))],
                cancellationToken);
        }

        var newEvent = new NewEvent(
            Kind: EventKinds.StateChange,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload);

        var eventId = await events.AppendAsync(newEvent, cancellationToken);
        var earned = await rewards.ReconcileWithProgressAsync(subject, eventId, cancellationToken);
        var made = taskSeries is null ? [] : await taskSeries.AfterStatusAsync(followUp, cancellationToken);
        return new StatusResult(eventId, subject, status, earned.Awards, earned.Progress) { Repetitions = made };
    }

    /// <summary>
    /// The vocabulary's spelling of <paramref name="newStatus"/> for a subject of
    /// <paramref name="type"/> (D7). A word outside the type's vocabulary is refused
    /// rather than stored, since rewards pay only the exact terminal word and a typo
    /// would lose one silently; a status-less type takes no status at all.
    /// </summary>
    private static string VocabularyStatus(string type, string newStatus)
    {
        var allowed = StatusVocabulary.For(type);
        if (allowed.Count == 0)
        {
            throw new ArgumentException(type == SubjectTypes.Area
                ? "An Area has no status; Areas are permanent."
                : $"{Article(type)} {type} has no status; archive it instead.");
        }

        return StatusVocabulary.Canonical(type, newStatus)
            ?? throw new ArgumentException(
                $"{Article(type)} {type}'s status is one of: {string.Join(", ", allowed)}; got '{newStatus.Trim()}'.");
    }

    private static string Article(string type) => "AEIOU".Contains(type[0]) ? "An" : "A";
}

/// <summary>
/// The outcome of a status change: the state_change event, the subject, any ledger entries it
/// earned or reversed, and each life domain whose level or rank that changed.
/// </summary>
public sealed record StatusResult(
    Guid EventId, SubjectRef Subject, string Status, IReadOnlyList<Award> Awards, IReadOnlyList<DomainChange> Progress)
{
    /// <summary>The repetitions it made, when it closed one of a repeating Task's (D16).</summary>
    public IReadOnlyList<SubjectRef> Repetitions { get; init; } = [];
}

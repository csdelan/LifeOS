using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// The optional, Toggl-style timer on a Task (TASKS-2) — the write path behind
/// <c>bsk timer</c>. Each Start / Pause / Resume / Stop appends one activity event
/// carrying <c>{subject_id, timer}</c> with a <c>concerns</c> edge to the Task; nothing
/// is mutated, and the recorded time is a fold over those events
/// (<c>bsk.v_task_timer</c>). A task never timed simply has no timer events.
/// <para>
/// One timer runs at a time, as in Toggl: starting or resuming a timer pauses whichever
/// other timer is running, in the same transaction, so time is never double-booked.
/// Pausing rather than stopping keeps the interrupted session open to resume.
/// </para>
/// <para>
/// Complete is Stop followed by moving the Task to <c>Completed</c> — the one place the
/// timer touches Status. Starting a timer does not move a Task to In progress; Status
/// otherwise changes only through Change status (TASKS-1).
/// </para>
/// </summary>
public sealed class TimerService(
    SubjectService subjects, ITimerRepository timers, StatusService status, IClock clock, string sourceId)
{
    /// <summary>The Task status Complete moves to (a terminal member of the Task vocabulary).</summary>
    public const string CompletedStatus = "Completed";

    /// <summary>The task's timer, or a stopped, zero timer when it has never run.</summary>
    public async Task<TimerSnapshot> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var task = await ResolveTaskAsync(reference, cancellationToken);
        return await timers.GetAsync(task.Id, cancellationToken) ?? Never(task);
    }

    /// <summary>
    /// Applies <paramref name="action"/> (start / pause / resume / stop) to the task's
    /// timer. Start runs from stopped, Pause from running, Resume from paused, and Stop
    /// from running or paused; anything else is refused with a reason and writes nothing.
    /// </summary>
    public async Task<TimerResult> ApplyAsync(
        string reference, string action, CancellationToken cancellationToken = default)
    {
        var normalized = (action ?? "").Trim().ToLowerInvariant();
        if (!TimerActions.All.Contains(normalized))
        {
            throw new ArgumentException(
                $"Unknown timer action '{action}'. Expected one of: {string.Join(", ", TimerActions.All)}, or complete.",
                nameof(action));
        }

        var task = await ResolveTaskAsync(reference, cancellationToken);
        var current = (await timers.GetAsync(task.Id, cancellationToken))?.State ?? TimerStates.Stopped;
        EnsureAllowed(task, normalized, current);

        var now = clock.UtcNow;
        var writes = new List<TimerWrite>();
        var paused = new List<SubjectRef>();
        if (normalized is TimerActions.Start or TimerActions.Resume)
        {
            foreach (var other in await timers.GetRunningAsync(cancellationToken))
            {
                if (other.SubjectId == task.Id)
                {
                    continue;
                }

                writes.Add(Write(other.SubjectId, TimerActions.Pause, TimerStates.Running, now));
                paused.Add(new SubjectRef(other.SubjectId, other.Urn, other.Type, other.Title));
            }
        }

        writes.Add(Write(task.Id, normalized, current, now));
        await timers.AppendAsync(writes, cancellationToken);

        var snapshot = await timers.GetAsync(task.Id, cancellationToken) ?? Never(task);
        return new TimerResult(task, snapshot, paused, Completed: false);
    }

    /// <summary>
    /// Stops the timer if it is running or paused, then moves the Task to
    /// <see cref="CompletedStatus"/>. A task whose timer never ran is simply completed.
    /// </summary>
    public async Task<TimerResult> CompleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var task = await ResolveTaskAsync(reference, cancellationToken);
        var current = (await timers.GetAsync(task.Id, cancellationToken))?.State ?? TimerStates.Stopped;
        if (current is TimerStates.Running or TimerStates.Paused)
        {
            await timers.AppendAsync(
                [Write(task.Id, TimerActions.Stop, current, clock.UtcNow)], cancellationToken);
        }

        await status.ChangeStatusAsync(task.Urn, CompletedStatus, cancellationToken);

        var snapshot = await timers.GetAsync(task.Id, cancellationToken) ?? Never(task);
        return new TimerResult(task, snapshot, [], Completed: true);
    }

    private async Task<SubjectRef> ResolveTaskAsync(string reference, CancellationToken cancellationToken)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        if (subject.Type != SubjectTypes.Task)
        {
            throw new InvalidOperationException(
                $"'{subject.Title}' is a {subject.Type}, not a Task; the timer is kept on Tasks.");
        }

        return subject;
    }

    private static void EnsureAllowed(SubjectRef task, string action, string current)
    {
        var allowed = action switch
        {
            TimerActions.Start => current == TimerStates.Stopped,
            TimerActions.Pause => current == TimerStates.Running,
            TimerActions.Resume => current == TimerStates.Paused,
            TimerActions.Stop => current is TimerStates.Running or TimerStates.Paused,
            _ => false
        };
        if (allowed)
        {
            return;
        }

        var reason = (action, current) switch
        {
            (TimerActions.Start, TimerStates.Running) => "it is already running",
            (TimerActions.Start, TimerStates.Paused) => "it is paused — resume it, or stop it first",
            (TimerActions.Resume, TimerStates.Running) => "it is already running",
            (TimerActions.Resume, TimerStates.Stopped) => "it is stopped — start it instead",
            (_, TimerStates.Stopped) => "it is not running",
            _ => $"it is {current}"
        };
        throw new InvalidOperationException($"Can't {action} the timer on '{task.Title}': {reason}.");
    }

    private TimerWrite Write(Guid subjectId, string action, string expectedState, DateTimeOffset at)
    {
        var payload = JsonSerializer.Serialize(new { subject_id = subjectId.ToString(), timer = action });
        var timerEvent = new NewEvent(
            Kind: EventKinds.Activity,
            Provenance: Provenances.Declared,
            OccurredAt: at,
            RecordedAt: at,
            SourceId: sourceId,
            PayloadJson: payload);
        return new TimerWrite(subjectId, expectedState, timerEvent);
    }

    private static TimerSnapshot Never(SubjectRef task)
        => new(task.Id, task.Urn, task.Type, task.Title, TimerStates.Stopped, null, 0, 0);
}

/// <summary>
/// The outcome of a timer action: the task, its timer afterwards, any other timers the
/// action paused (starting one pauses the running one), and whether the task was completed.
/// </summary>
public sealed record TimerResult(
    SubjectRef Task, TimerSnapshot Timer, IReadOnlyList<SubjectRef> Paused, bool Completed);

using System.Globalization;
using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Makes a repeating Task's repetitions (TASKS-3, D16). A series is a Task carrying a
/// pattern; each repetition is an ordinary Task for one date, so every Task feature (the
/// lists, Change status, the timer, the reward) works on it unchanged.
/// <para>
/// When the next one is made depends on the kind (<see cref="RepeatKind"/>):
/// <list type="bullet">
/// <item>on calendar dates: when the latest closes, or when its date passes, so every date
/// gets its own and a late one stays open beside the next (rent);</item>
/// <item>once each week: the same, for the week (do-date Monday, due Sunday);</item>
/// <item>after the last completion: when the latest closes, N after the day it closed.</item>
/// </list>
/// The first is made when the pattern is set. <see cref="SettleAsync"/> catches up dates that
/// passed while nothing ran (there's no background job: the app calls it when Tasks or Focus
/// opens). Nothing here changes a status: a late repetition waits for Chris to complete it,
/// cancel it or mark it Missed.
/// </para>
/// <para>
/// A series repeats while it's Not started; Cancelled or archived stops it, and moving it back
/// to Not started restarts it from today, so the dates it skipped while stopped aren't made.
/// </para>
/// </summary>
public sealed class TaskSeriesService(
    ITaskSeriesRepository series, ISubjectRepository subjects, IHomeCalendar calendar, IClock clock)
{
    // A settle that loses a race re-reads and plans again.
    private const int Attempts = 5;

    // Most repetitions one catch-up makes for one series: a daily chore after a year away.
    private const int MostAtOnce = 400;

    /// <summary>Makes the repetitions every repeating series is owed by today (<c>bsk tasks settle</c>).</summary>
    public async Task<TaskSettleResult> SettleAsync(CancellationToken cancellationToken = default)
    {
        var today = await TodayAsync(cancellationToken);
        var repeating = await series.RepeatingAsync(cancellationToken);
        var made = new List<SubjectRef>();
        foreach (var id in repeating)
        {
            made.AddRange(await EnsureAsync(id, today, cancellationToken));
        }

        return new TaskSettleResult(repeating.Count, made);
    }

    /// <summary>Makes the repetitions one series is owed by today.</summary>
    public async Task<IReadOnlyList<SubjectRef>> SettleSeriesAsync(
        Guid seriesId, CancellationToken cancellationToken = default)
        => await EnsureAsync(seriesId, await TodayAsync(cancellationToken), cancellationToken);

    /// <summary>True when the Task is a repeating Task's series.</summary>
    public async Task<bool> IsSeriesAsync(Guid taskId, CancellationToken cancellationToken = default)
        => (await series.GetAsync(taskId, cancellationToken))?.IsSeries ?? false;

    /// <summary>
    /// Checks a status move before it's written: a series is Not started (repeating) or
    /// Cancelled (stopped), since its repetitions are the ones done or missed. Says what to do
    /// after it (<see cref="AfterStatusAsync"/>).
    /// </summary>
    public async Task<TaskStatusFollowUp> BeforeStatusAsync(
        SubjectRef subject, string status, CancellationToken cancellationToken = default)
    {
        if (subject.Type != SubjectTypes.Task
            || await series.GetAsync(subject.Id, cancellationToken) is not { } task)
        {
            return TaskStatusFollowUp.None;
        }

        if (task.IsSeries)
        {
            if (!TaskSeries.SeriesStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"'{subject.Title}' is a repeating task: it's Not started while it repeats, or Cancelled to stop it. "
                    + "Its repetitions are the ones you complete or mark Missed.");
            }

            var restarting = StatusVocabulary.IsTerminal(task.Status ?? "") && !StatusVocabulary.IsTerminal(status);
            return new TaskStatusFollowUp(subject.Id, restarting);
        }

        return task.RepetitionOf is { } urn && await subjects.FindByUrnAsync(urn, cancellationToken) is { } owner
            ? new TaskStatusFollowUp(owner.Id, Restarting: false)
            : TaskStatusFollowUp.None;
    }

    /// <summary>
    /// After a status move: a repetition that closed makes the next; a series restarted counts
    /// from today. Returns the repetitions made.
    /// </summary>
    public async Task<IReadOnlyList<SubjectRef>> AfterStatusAsync(
        TaskStatusFollowUp followUp, CancellationToken cancellationToken = default)
    {
        if (followUp.SeriesId is not { } seriesId)
        {
            return [];
        }

        var today = await TodayAsync(cancellationToken);
        if (followUp.Restarting)
        {
            await SetStartAsync(seriesId, today, cancellationToken);
        }

        return await EnsureAsync(seriesId, today, cancellationToken);
    }

    /// <summary>A series restored from the archive repeats again, from today.</summary>
    public async Task<IReadOnlyList<SubjectRef>> AfterRestoreAsync(
        SubjectRef subject, CancellationToken cancellationToken = default)
    {
        if (subject.Type != SubjectTypes.Task
            || await series.GetAsync(subject.Id, cancellationToken) is not { IsSeries: true })
        {
            return [];
        }

        var today = await TodayAsync(cancellationToken);
        await SetStartAsync(subject.Id, today, cancellationToken);
        return await EnsureAsync(subject.Id, today, cancellationToken);
    }

    /// <summary>
    /// Checks a pattern change before it's written. A repetition doesn't take a pattern of its
    /// own, nor does a finished Task; and a series that has made repetitions stops by being
    /// Cancelled, not by losing its pattern.
    /// </summary>
    public async Task GuardPatternAsync(SubjectRef subject, bool clearing, CancellationToken cancellationToken = default)
    {
        if (subject.Type != SubjectTypes.Task
            || await series.GetAsync(subject.Id, cancellationToken) is not { } task)
        {
            return;
        }

        if (task.RepetitionOf is not null)
        {
            throw new ArgumentException(
                $"'{subject.Title}' is one repetition of a repeating task. Change the repeat on the repeating task itself.");
        }

        if (clearing)
        {
            if (task.IsSeries && task.Latest is not null)
            {
                throw new ArgumentException(
                    $"'{subject.Title}' has repetitions. To stop it repeating, set its status to Cancelled.");
            }

            return;
        }

        if (StatusVocabulary.IsTerminal(task.Status ?? "")
            && !string.Equals(task.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{subject.Title}' is {task.Status}. A finished Task doesn't start repeating: make a new one.");
        }
    }

    /// <summary>
    /// After a Task's pattern (or start) is set: a new series gets its start, the date chosen
    /// or else its due date or today, and its first repetition; an existing one re-dates its
    /// open repetition whose date hasn't passed to the new pattern. Returns the repetitions made.
    /// </summary>
    public async Task<IReadOnlyList<SubjectRef>> AfterPatternAsync(
        SubjectRef subject, DateOnly? start = null, CancellationToken cancellationToken = default)
    {
        if (subject.Type != SubjectTypes.Task)
        {
            return [];
        }

        var today = await TodayAsync(cancellationToken);
        var task = await series.GetAsync(subject.Id, cancellationToken);
        if (task is not { IsSeries: true })
        {
            return [];
        }

        if (start is { } chosen && chosen != task.Start)
        {
            await SetStartAsync(subject.Id, chosen, cancellationToken);
        }
        else if (task.Start is null)
        {
            await SetStartAsync(subject.Id, task.Due ?? today, cancellationToken);
        }

        task = await series.GetAsync(subject.Id, cancellationToken);
        if (task is { Latest: { Closed: false, Archived: false } open } && (open.Due ?? open.Occurrence) >= today)
        {
            await RedateAsync(task, open, today, cancellationToken);
        }

        return await EnsureAsync(subject.Id, today, cancellationToken);
    }

    /// <summary>
    /// Before a series' attributes are edited: its values for the copied keys among
    /// <paramref name="keys"/>, so <see cref="FollowAsync"/> can carry the edit to the open
    /// repetitions still holding them. Null for anything but a series.
    /// </summary>
    public async Task<SeriesEdit?> BeforeEditAsync(
        SubjectRef subject, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var followed = keys.Where(k => TaskSeries.CopiedKeys.Contains(k, StringComparer.Ordinal)).Distinct().ToList();
        if (subject.Type != SubjectTypes.Task
            || followed.Count == 0
            || await series.GetAsync(subject.Id, cancellationToken) is not { IsSeries: true } task)
        {
            return null;
        }

        return new SeriesEdit(
            task.Series,
            followed.ToDictionary(k => k, k => task.Copied.TryGetValue(k, out var v) ? v : null, StringComparer.Ordinal));
    }

    /// <summary>
    /// Carries a series edit to its open repetitions that still had the series' old value; one
    /// changed on its own keeps its own. Returns how many repetition fields changed.
    /// </summary>
    public async Task<int> FollowAsync(
        SeriesEdit edit, IReadOnlyDictionary<string, string?> newValues, CancellationToken cancellationToken = default)
    {
        var changed = 0;
        foreach (var (key, previous) in edit.Previous)
        {
            if (!newValues.TryGetValue(key, out var value) || string.Equals(previous, value, StringComparison.Ordinal))
            {
                continue;
            }

            changed += await series.FollowAttributeAsync(edit.Series.Urn, key, previous, value, cancellationToken);
        }

        return changed;
    }

    /// <summary>Carries a series' tag edit to its open repetitions.</summary>
    public async Task FollowTagsAsync(
        Guid subjectId, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove,
        CancellationToken cancellationToken = default)
    {
        if (await series.GetAsync(subjectId, cancellationToken) is { IsSeries: true } task)
        {
            await series.FollowTagsAsync(task.Series.Urn, add, remove, cancellationToken);
        }
    }

    /// <summary>
    /// Makes the repetitions <paramref name="seriesId"/> is owed by <paramref name="today"/>,
    /// re-planning when another writer got there first.
    /// </summary>
    private async Task<IReadOnlyList<SubjectRef>> EnsureAsync(
        Guid seriesId, DateOnly today, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var state = await series.GetAsync(seriesId, cancellationToken);
            if (state is not { IsSeries: true } || state.Stopped)
            {
                return [];
            }

            // A pattern set without one (made with its attributes, say) gets its start now, so
            // the dates it repeats on don't drift with each day it's settled.
            if (state.Start is null)
            {
                await SetStartAsync(seriesId, state.Due ?? today, cancellationToken);
                continue;
            }

            var plan = await PlanAsync(state, today, cancellationToken);
            if (plan.Count == 0)
            {
                return [];
            }

            if (await series.CreateAsync(state, state.Latest?.Occurrence, plan, cancellationToken) is { } made)
            {
                return made;
            }
        }

        throw new InvalidOperationException(
            "The repeating task's repetitions kept changing while they were being made. Try again.");
    }

    /// <summary>The repetitions a series is owed: see the class summary for when each is made.</summary>
    private async Task<IReadOnlyList<NewTaskRepetition>> PlanAsync(
        TaskSeriesState state, DateOnly today, CancellationToken cancellationToken)
    {
        var recurrence = state.RecurrenceJson!;
        var kind = TaskSeries.KindOf(recurrence);
        if (kind == RepeatKind.None)
        {
            return [];
        }

        var start = state.Start ?? today;
        var plan = new List<NewTaskRepetition>();

        // The repetition the next is dated from. An archived one counts as closed, so hiding
        // one doesn't stall the series.
        Cursor? cursor = state.Latest is { } latest
            ? new Cursor(
                latest.Occurrence,
                latest.Due ?? TaskSeries.DatesFor(kind, latest.Occurrence).Due,
                latest.Closed || latest.Archived,
                latest.ClosedOn ?? (latest.Archived ? today : null))
            : null;

        if (cursor is null)
        {
            var first = kind switch
            {
                RepeatKind.EachWeek => TaskSeries.WeekOf(start),
                RepeatKind.AfterCompletion => start,
                _ => await series.NextOccurrenceAsync(recurrence, start, start, cancellationToken)
            };
            if (first is not { } firstDate)
            {
                return [];
            }

            plan.Add(Repetition(state, kind, firstDate));
            cursor = Cursor.Open(kind, firstDate);
        }

        while (plan.Count < MostAtOnce)
        {
            DateOnly? next;
            if (kind == RepeatKind.AfterCompletion)
            {
                if (!cursor.Closed)
                {
                    break;
                }

                var counted = TaskSeries.AfterCompletion(recurrence, cursor.ClosedOn ?? today);
                next = Max(counted, Max(start, cursor.Occurrence.AddDays(1)));
            }
            else
            {
                if (!cursor.Closed && cursor.Due >= today)
                {
                    break;
                }

                next = kind == RepeatKind.EachWeek
                    ? Max(cursor.Occurrence.AddDays(7), TaskSeries.WeekOf(start))
                    : await series.NextOccurrenceAsync(
                        recurrence, start, Max(start, cursor.Occurrence.AddDays(1)), cancellationToken);
            }

            if (next is not { } nextDate)
            {
                break;
            }

            plan.Add(Repetition(state, kind, nextDate));
            cursor = Cursor.Open(kind, nextDate);
        }

        return plan;
    }

    /// <summary>Moves the open repetition to where the series' pattern now puts it.</summary>
    private async Task RedateAsync(
        TaskSeriesState state, TaskRepetition open, DateOnly today, CancellationToken cancellationToken)
    {
        var recurrence = state.RecurrenceJson!;
        var kind = TaskSeries.KindOf(recurrence);
        var start = state.Start ?? today;
        DateOnly? when;
        switch (kind)
        {
            case RepeatKind.Calendar:
                when = await series.NextOccurrenceAsync(recurrence, start, Max(start, today), cancellationToken);
                break;
            case RepeatKind.EachWeek:
                when = TaskSeries.WeekOf(Max(start, today));
                break;
            case RepeatKind.AfterCompletion:
                var repetitions = await series.RepetitionsAsync(state.Series.Urn, cancellationToken);
                var lastClosed = repetitions
                    .Where(r => r.Id != open.Id && r.Closed && r.ClosedOn is not null)
                    .MaxBy(r => r.Occurrence);
                when = lastClosed is null
                    ? start
                    : Max(start, TaskSeries.AfterCompletion(recurrence, lastClosed.ClosedOn!.Value));
                break;
            default:
                return;
        }

        if (when is not { } date)
        {
            return;
        }

        var (due, scheduled) = TaskSeries.DatesFor(kind, date);
        if (date == open.Occurrence && due == open.Due && scheduled == open.Scheduled)
        {
            return;
        }

        var patch = new JsonObject
        {
            [TaskSeries.OccurrenceKey] = Iso(date),
            [TaskSeries.DueKey] = Iso(due)
        };
        var remove = new List<string>();
        if (scheduled is { } doDate)
        {
            patch[TaskSeries.ScheduledKey] = Iso(doDate);
        }
        else if (open.Scheduled is not null)
        {
            remove.Add(TaskSeries.ScheduledKey);
        }

        // Another repetition already standing for that date keeps it; this one stays as it was.
        await series.RedateAsync(state.Series.Id, open.Id, patch.ToJsonString(), remove, cancellationToken);
    }

    private static NewTaskRepetition Repetition(TaskSeriesState state, RepeatKind kind, DateOnly occurrence)
    {
        var (due, scheduled) = TaskSeries.DatesFor(kind, occurrence);
        var attributes = new JsonObject
        {
            [TaskSeries.SeriesKey] = state.Series.Urn,
            [TaskSeries.OccurrenceKey] = Iso(occurrence),
            [TaskSeries.DueKey] = Iso(due)
        };
        if (scheduled is { } doDate)
        {
            attributes[TaskSeries.ScheduledKey] = Iso(doDate);
        }

        foreach (var (key, value) in state.Copied)
        {
            attributes[key] = value;
        }

        return new NewTaskRepetition(
            Urns.Build(SubjectTypes.Task, state.Series.Title), state.Series.Title, occurrence, attributes.ToJsonString());
    }

    private Task SetStartAsync(Guid seriesId, DateOnly start, CancellationToken cancellationToken)
        => subjects.UpdateAttributesAsync(
            seriesId, new JsonObject { [TaskSeries.StartKey] = Iso(start) }.ToJsonString(), [], cancellationToken);

    private Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
        => calendar.DateAtAsync(clock.UtcNow, cancellationToken);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The repetition the next one is dated from.</summary>
    private sealed record Cursor(DateOnly Occurrence, DateOnly Due, bool Closed, DateOnly? ClosedOn)
    {
        public static Cursor Open(RepeatKind kind, DateOnly occurrence)
            => new(occurrence, TaskSeries.DatesFor(kind, occurrence).Due, Closed: false, ClosedOn: null);
    }
}

/// <summary>What a settle did: how many series it looked at and the repetitions it made.</summary>
public sealed record TaskSettleResult(int Series, IReadOnlyList<SubjectRef> Made);

/// <summary>What follows a status move for repeating Tasks: the series to settle, and whether it restarted.</summary>
public sealed record TaskStatusFollowUp(Guid? SeriesId, bool Restarting)
{
    public static readonly TaskStatusFollowUp None = new(null, false);
}

/// <summary>A series edit in progress: the series and its values before, for the keys a repetition copies.</summary>
public sealed record SeriesEdit(SubjectRef Series, IReadOnlyDictionary<string, string?> Previous);

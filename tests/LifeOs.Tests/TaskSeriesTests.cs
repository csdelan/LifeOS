using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// A clock set to a day by hand: noon UTC (the tests' home zone), a second later on every
/// read so events written on one day still fold in the order they were written.
/// </summary>
internal sealed class DayClock : IClock
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _now = _now.AddSeconds(1);
            }
        }
    }

    public void On(DateOnly day)
    {
        lock (_gate)
        {
            _now = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        }
    }
}

/// <summary>
/// TASKS-3 / D16 (migration 0042, recurring-tasks plan RT2): repeating Tasks. A series is a
/// Task carrying a pattern; each repetition is an ordinary Task for one date.
/// <para>
/// The database is shared and a settle looks at every series, so the timelines run on a
/// <see cref="DayClock"/> set weeks ahead of today, each series pays nothing (no ledger entries
/// dated in the future), and each test leaves its series either stopped or with its latest
/// repetition open, so a settle elsewhere has nothing to make for it. The tests that pay run
/// on today's clock, since nothing earned before the wallet opened pays.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TaskSeriesTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly RealToday = DateOnly.FromDateTime(DateTime.UtcNow);

    // A Monday ten weeks out: the timelines' first day.
    private static readonly DateOnly Monday = TaskSeries.WeekOf(RealToday).AddDays(70);

    private sealed record Kernel(
        ServiceProvider Provider, DayClock Clock, SubjectService Subjects, StatusService Status,
        RecurrenceService Recurrence, AttributeService Attributes, ArchiveService Archive, TagService Tags,
        RenameService Rename, RelationService Relations, BountyService Bounties, TimerService Timers,
        TaskSeriesService Series, ITaskSeriesRepository Store);

    private Kernel Build(DateOnly? on = null, bool realClock = false)
    {
        var clock = new DayClock();
        clock.On(on ?? Monday);
        var services = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString);
        if (!realClock)
        {
            services.AddSingleton<IClock>(clock);
        }

        var provider = services.BuildServiceProvider();
        return new Kernel(
            provider, clock,
            provider.GetRequiredService<SubjectService>(),
            provider.GetRequiredService<StatusService>(),
            provider.GetRequiredService<RecurrenceService>(),
            provider.GetRequiredService<AttributeService>(),
            provider.GetRequiredService<ArchiveService>(),
            provider.GetRequiredService<TagService>(),
            provider.GetRequiredService<RenameService>(),
            provider.GetRequiredService<RelationService>(),
            provider.GetRequiredService<BountyService>(),
            provider.GetRequiredService<TimerService>(),
            provider.GetRequiredService<TaskSeriesService>(),
            provider.GetRequiredService<ITaskSeriesRepository>());
    }

    private static string Unique(string title) => $"{title} {Guid.NewGuid():N}"[..(title.Length + 9)];

    /// <summary>A repeating Task that pays nothing, its pattern set from <paramref name="start"/>.</summary>
    private async Task<(SubjectRef Series, IReadOnlyList<SubjectRef> Made)> SeriesAsync(
        Kernel k, string title, string recurrence, DateOnly? start, string attributes = "{}", bool free = true)
    {
        var series = await k.Subjects.CreateAsync(SubjectTypes.Task, Unique(title), attributes, cancellationToken: Ct);
        if (free)
        {
            await k.Bounties.SetNothingAsync(series.Urn, BountyTriggers.Completed, cancellationToken: Ct);
        }

        var set = await k.Recurrence.SetAsync(series.Urn, recurrence, start, Ct);
        return (series, set.Made);
    }

    private async Task<IReadOnlyList<TaskRepetition>> RepetitionsAsync(Kernel k, SubjectRef series)
        => await k.Store.RepetitionsAsync(series.Urn, Ct);

    private async Task<TaskRepetition> RepetitionAsync(Kernel k, SubjectRef series, DateOnly occurrence)
        => Assert.Single(await RepetitionsAsync(k, series), r => r.Occurrence == occurrence);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<string?> AttributeAsync(Guid id, string key)
    {
        await using var c = await OpenAsync();
        return await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT attributes->>@key FROM bsk.subject WHERE id = @id;", new { id, key }, cancellationToken: Ct));
    }

    private async Task<string> TitleAsync(Guid id)
    {
        await using var c = await OpenAsync();
        return await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT title FROM bsk.subject WHERE id = @id;", new { id }, cancellationToken: Ct)) ?? "";
    }

    private async Task<IReadOnlyList<string>> TagsAsync(Guid id)
    {
        await using var c = await OpenAsync();
        return (await c.QueryAsync<string>(new CommandDefinition(
            "SELECT tag FROM bsk.item_tag WHERE subject_id = @id ORDER BY tag;", new { id }, cancellationToken: Ct))).AsList();
    }

    // -----------------------------------------------------------------------------------
    // The kinds of repeat
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task Trash_on_Wednesdays_makes_each_date_its_own_and_leaves_a_late_one_open()
    {
        var k = Build();
        var wed = Monday.AddDays(2);

        var (series, made) = await SeriesAsync(k, "Take out trash", Recurrence.Weekly(["wed"]), Monday);

        // The first is made with the pattern: due that Wednesday, no do-date, not started.
        var first = Assert.Single(made);
        var rep1 = await RepetitionAsync(k, series, wed);
        Assert.Equal(first.Id, rep1.Id);
        Assert.Equal(wed, rep1.Due);
        Assert.Null(rep1.Scheduled);
        Assert.False(rep1.Closed);
        Assert.Equal(series.Title, rep1.Title);
        Assert.Equal(series.Urn, await AttributeAsync(rep1.Id, TaskSeries.SeriesKey));
        Assert.Equal(Iso(Monday), await AttributeAsync(series.Id, TaskSeries.StartKey));

        // Done on the day: the next Wednesday's is made.
        k.Clock.On(wed);
        var done = await k.Status.ChangeStatusAsync(rep1.Urn, "Completed", Ct);
        var rep2 = Assert.Single(done.Repetitions);
        Assert.Equal(wed.AddDays(7), (await RepetitionAsync(k, series, wed.AddDays(7))).Due);
        Assert.Equal(rep2.Id, (await RepetitionAsync(k, series, wed.AddDays(7))).Id);

        // Not done, and its Wednesday passes: the next is made and the late one stays open.
        k.Clock.On(wed.AddDays(8));
        var settled = await k.Series.SettleSeriesAsync(series.Id, Ct);
        Assert.Single(settled);
        Assert.False((await RepetitionAsync(k, series, wed.AddDays(7))).Closed);
        Assert.False((await RepetitionAsync(k, series, wed.AddDays(14))).Closed);

        // Away for two weeks: each Wednesday that passed gets its own, up to the next one.
        k.Clock.On(wed.AddDays(29));
        settled = await k.Series.SettleSeriesAsync(series.Id, Ct);
        Assert.Equal(3, settled.Count);
        var all = await RepetitionsAsync(k, series);
        Assert.Equal(
            Enumerable.Range(0, 6).Select(i => wed.AddDays(7 * i)),
            all.Select(r => r.Occurrence));
        Assert.All(all.Skip(1), r => Assert.False(r.Closed)); // nothing closed itself

        // Settling again makes nothing.
        Assert.Empty(await k.Series.SettleSeriesAsync(series.Id, Ct));

        // Chris closes one out as Missed: allowed on a Task, and the latest is still open,
        // so nothing more is made.
        var missed = await k.Status.ChangeStatusAsync(all[1].Urn, "Missed", Ct);
        Assert.Equal("Missed", missed.Status);
        Assert.Empty(missed.Repetitions);
        Assert.Empty(missed.Awards);
        await k.Status.ChangeStatusAsync(all[2].Urn, "Cancelled", Ct);

        // The series view counts them.
        await using var c = await OpenAsync();
        var view = await c.QuerySingleAsync<SeriesRow>(
            new CommandDefinition(
                """
                SELECT open_count AS opencount, completed_count AS completedcount,
                       missed_count AS missedcount, cancelled_count AS cancelledcount,
                       to_char(last_completed_on, 'YYYY-MM-DD') AS lastcompleted,
                       to_char(next_due, 'YYYY-MM-DD') AS nextdue,
                       to_char(latest_occurrence, 'YYYY-MM-DD') AS latest, stopped
                FROM bsk.v_task_series WHERE id = @id;
                """,
                new { id = series.Id }, cancellationToken: Ct));
        Assert.Equal((3L, 1L, 1L, 1L), (view.OpenCount, view.CompletedCount, view.MissedCount, view.CancelledCount));
        Assert.Equal(Iso(wed), view.LastCompleted);
        Assert.Equal(Iso(wed.AddDays(21)), view.NextDue);
        Assert.Equal(Iso(wed.AddDays(35)), view.Latest);
        Assert.False(view.Stopped);
    }

    private sealed class SeriesRow
    {
        public long OpenCount { get; init; }
        public long CompletedCount { get; init; }
        public long MissedCount { get; init; }
        public long CancelledCount { get; init; }
        public string? LastCompleted { get; init; }
        public string? NextDue { get; init; }
        public string? Latest { get; init; }
        public bool Stopped { get; init; }
    }

    [Fact]
    public async Task Rent_late_this_month_stays_open_beside_next_months()
    {
        var first = new DateOnly(RealToday.Year, RealToday.Month, 1).AddMonths(3);
        var k = Build(first.AddDays(-5));

        var (series, made) = await SeriesAsync(k, "Pay rent", Recurrence.MonthlyDay(1), first.AddDays(-5));
        Assert.Single(made);
        Assert.Equal(first, (await RepetitionsAsync(k, series)).Single().Due);

        // Ten days late: next month's is made and this month's is still there to pay.
        k.Clock.On(first.AddDays(10));
        Assert.Single(await k.Series.SettleSeriesAsync(series.Id, Ct));
        var open = await RepetitionsAsync(k, series);
        Assert.Equal([first, first.AddMonths(1)], open.Select(r => r.Due!.Value));
        Assert.All(open, r => Assert.False(r.Closed));

        // Paying the late one makes nothing new: next month's is already waiting.
        var paid = await k.Status.ChangeStatusAsync(open[0].Urn, "Completed", Ct);
        Assert.Empty(paid.Repetitions);
        Assert.Equal(2, (await RepetitionsAsync(k, series)).Count);
    }

    [Fact]
    public async Task Once_each_week_runs_Monday_to_Sunday_and_any_day_in_it_counts()
    {
        var thursday = Monday.AddDays(3);
        var k = Build(thursday);

        var (series, _) = await SeriesAsync(k, "Clean bathroom", Recurrence.Each("week"), thursday);

        // Set up on a Thursday: this week's, from its Monday to its Sunday.
        var week1 = Assert.Single(await RepetitionsAsync(k, series));
        Assert.Equal(Monday, week1.Occurrence);
        Assert.Equal(Monday, week1.Scheduled);
        Assert.Equal(Monday.AddDays(6), week1.Due);

        // Done on the Saturday: next week's.
        k.Clock.On(Monday.AddDays(5));
        var done = await k.Status.ChangeStatusAsync(week1.Urn, "Completed", Ct);
        Assert.Single(done.Repetitions);
        var week2 = await RepetitionAsync(k, series, Monday.AddDays(7));
        Assert.Equal(Monday.AddDays(7), week2.Scheduled);
        Assert.Equal(Monday.AddDays(13), week2.Due);

        // Week two goes by undone: on the Tuesday after, week three's is made.
        k.Clock.On(Monday.AddDays(15));
        Assert.Single(await k.Series.SettleSeriesAsync(series.Id, Ct));
        var week3 = await RepetitionAsync(k, series, Monday.AddDays(14));
        Assert.Equal(Monday.AddDays(20), week3.Due);
        Assert.False((await RepetitionAsync(k, series, Monday.AddDays(7))).Closed);
    }

    [Fact]
    public async Task The_fridge_counts_30_days_from_the_day_the_last_one_closed()
    {
        var k = Build();

        var (series, _) = await SeriesAsync(
            k, "Clean fridge", Recurrence.Interval(30, "days", Recurrence.FromCompletion), Monday);
        var rep1 = Assert.Single(await RepetitionsAsync(k, series));
        Assert.Equal(Monday, rep1.Due);

        // Cleaned four days late: the next is due 30 days after that day.
        k.Clock.On(Monday.AddDays(4));
        var done = await k.Status.ChangeStatusAsync(rep1.Urn, "Completed", Ct);
        Assert.Single(done.Repetitions);
        var rep2 = await RepetitionAsync(k, series, Monday.AddDays(34));
        Assert.Equal(Monday.AddDays(34), rep2.Due);

        // Its date passing makes nothing: it waits to be done.
        k.Clock.On(Monday.AddDays(44));
        Assert.Empty(await k.Series.SettleSeriesAsync(series.Id, Ct));

        // Cancelled and Missed count from their day too.
        await k.Status.ChangeStatusAsync(rep2.Urn, "Cancelled", Ct);
        var rep3 = await RepetitionAsync(k, series, Monday.AddDays(74));
        k.Clock.On(Monday.AddDays(80));
        await k.Status.ChangeStatusAsync(rep3.Urn, "Missed", Ct);
        Assert.Equal(Monday.AddDays(110), (await RepetitionsAsync(k, series)).Last().Due);
    }

    [Fact]
    public async Task Interval_days_from_the_start_stay_on_their_phase()
    {
        var k = Build();
        var (series, _) = await SeriesAsync(k, "Water plants", Recurrence.Interval(10, "days"), Monday);

        k.Clock.On(Monday.AddDays(25));
        await k.Series.SettleSeriesAsync(series.Id, Ct);
        Assert.Equal(
            [Monday, Monday.AddDays(10), Monday.AddDays(20), Monday.AddDays(30)],
            (await RepetitionsAsync(k, series)).Select(r => r.Occurrence));
    }

    // -----------------------------------------------------------------------------------
    // Paying
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task Every_repetition_pays_the_series_price_and_Missed_then_Completed_still_pays()
    {
        var k = Build(realClock: true);
        var (series, _) = await SeriesAsync(k, "Daily stretch", Recurrence.Daily(), RealToday, free: false);
        await k.Bounties.SetAsync(
            series.Urn, BountyTriggers.Completed,
            new Dictionary<string, long> { [Currencies.Freedom] = 0, [Currencies.Xp] = 3 }, cancellationToken: Ct);
        var today = Assert.Single(await RepetitionsAsync(k, series));

        // Missed pays nothing and makes tomorrow's.
        var missed = await k.Status.ChangeStatusAsync(today.Urn, "Missed", Ct);
        Assert.Empty(missed.Awards);
        Assert.Single(missed.Repetitions);

        // Done after all: it pays the series' price.
        var done = await k.Status.ChangeStatusAsync(today.Urn, "Completed", Ct);
        var award = Assert.Single(done.Awards);
        Assert.Equal(new Dictionary<string, long> { [Currencies.Xp] = 3 }, award.Amounts);
        Assert.Empty(done.Repetitions);

        // A repetition reads the series' price, and one priced on its own starts from it.
        var tomorrow = await RepetitionAsync(k, series, RealToday.AddDays(1));
        var read = await k.Bounties.GetAsync(tomorrow.Urn, Ct);
        Assert.Equal(
            new Dictionary<string, long> { [Currencies.Xp] = 3 },
            read.Lines.Single(l => l.Trigger == BountyTriggers.Completed).Amounts);
        var priced = await k.Bounties.SetAsync(
            tomorrow.Urn, BountyTriggers.Completed,
            new Dictionary<string, long> { [Currencies.Karma] = 1 }, cancellationToken: Ct);
        Assert.Equal(
            new Dictionary<string, long> { [Currencies.Xp] = 3, [Currencies.Karma] = 1 },
            priced.Lines.Single(l => l.Trigger == BountyTriggers.Completed).Amounts);
        await k.Bounties.SetAsync(
            tomorrow.Urn, BountyTriggers.Completed,
            new Dictionary<string, long> { [Currencies.Karma] = 0, [Currencies.Xp] = 7 }, cancellationToken: Ct);
        var own = await k.Status.ChangeStatusAsync(tomorrow.Urn, "Completed", Ct);
        Assert.Equal(new Dictionary<string, long> { [Currencies.Xp] = 7 }, Assert.Single(own.Awards).Amounts);

        // The series' price changed: the next one done pays the new price.
        await k.Bounties.SetAsync(
            series.Urn, BountyTriggers.Completed, new Dictionary<string, long> { [Currencies.Xp] = 5 }, cancellationToken: Ct);
        var next = Assert.Single(own.Repetitions);
        var later = await k.Status.ChangeStatusAsync(next.Urn, "Completed", Ct);
        Assert.Equal(new Dictionary<string, long> { [Currencies.Xp] = 5 }, Assert.Single(later.Awards).Amounts);

        await k.Status.ChangeStatusAsync(series.Urn, "Cancelled", Ct);
    }

    // -----------------------------------------------------------------------------------
    // Stopping and restarting
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_series_is_Not_started_or_Cancelled_and_restarting_counts_from_today()
    {
        var k = Build();
        var wed = Monday.AddDays(2);
        var (series, _) = await SeriesAsync(k, "Take out recycling", Recurrence.Weekly(["wed"]), Monday);

        var completing = await Assert.ThrowsAsync<ArgumentException>(
            () => k.Status.ChangeStatusAsync(series.Urn, "Completed", Ct));
        Assert.Contains("repeating task", completing.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => k.Status.ChangeStatusAsync(series.Urn, "In progress", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => k.Status.ChangeStatusAsync(series.Urn, "Missed", Ct));

        // Cancelled stops it: three Wednesdays pass and nothing is made.
        await k.Status.ChangeStatusAsync(series.Urn, "Cancelled", Ct);
        k.Clock.On(wed.AddDays(22));
        Assert.Empty(await k.Series.SettleSeriesAsync(series.Id, Ct));
        Assert.DoesNotContain(series.Id, await k.Store.RepeatingAsync(Ct));

        // Back to Not started: it repeats from today, so the stopped weeks aren't made.
        var restarted = await k.Status.ChangeStatusAsync(series.Urn, "Not started", Ct);
        Assert.Equal(Iso(wed.AddDays(22)), await AttributeAsync(series.Id, TaskSeries.StartKey));
        Assert.Single(restarted.Repetitions);
        Assert.Equal([wed, wed.AddDays(28)], (await RepetitionsAsync(k, series)).Select(r => r.Occurrence));
    }

    [Fact]
    public async Task Archiving_a_series_stops_it_and_restoring_it_repeats_from_today()
    {
        var k = Build();
        var (series, _) = await SeriesAsync(k, "Mow lawn", Recurrence.Weekly(["sat"]), Monday);
        var sat = Monday.AddDays(5);

        await k.Archive.ArchiveAsync(series.Urn, Ct);
        k.Clock.On(sat.AddDays(15));
        Assert.Empty(await k.Series.SettleSeriesAsync(series.Id, Ct));

        await k.Archive.RestoreAsync(series.Urn, Ct);
        Assert.Equal([sat, sat.AddDays(21)], (await RepetitionsAsync(k, series)).Select(r => r.Occurrence));
    }

    [Fact]
    public async Task An_archived_latest_repetition_counts_as_closed()
    {
        var k = Build();
        var (series, made) = await SeriesAsync(k, "Change filter", Recurrence.Weekly(["fri"]), Monday);

        await k.Archive.ArchiveAsync(made[0].Urn, Ct);
        Assert.Single(await k.Series.SettleSeriesAsync(series.Id, Ct));
        Assert.Equal(Monday.AddDays(11), (await RepetitionsAsync(k, series)).Last().Occurrence);
    }

    // -----------------------------------------------------------------------------------
    // Guards
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_repetition_takes_no_pattern_and_a_series_with_repetitions_is_cancelled_not_cleared()
    {
        var k = Build();
        var (series, made) = await SeriesAsync(k, "Sweep porch", Recurrence.Weekly(["sun"]), Monday);
        var rep = made[0];

        var own = await Assert.ThrowsAsync<ArgumentException>(
            () => k.Recurrence.SetAsync(rep.Urn, Recurrence.Daily(), Ct));
        Assert.Contains("one repetition", own.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => k.Attributes.SetAsync(
            rep.Urn, [new AttributeAssignment(Recurrence.AttributeKey, Recurrence.Daily())], Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => k.Attributes.SetAsync(
            rep.Urn, [new AttributeAssignment(TaskSeries.SeriesKey, "urn:bsk:task:other-abc123")], Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => k.Attributes.SetAsync(
            series.Urn, [new AttributeAssignment(TaskSeries.OccurrenceKey, "2030-01-01")], Ct));

        var clearing = await Assert.ThrowsAsync<ArgumentException>(() => k.Recurrence.ClearAsync(series.Urn, Ct));
        Assert.Contains("Cancelled", clearing.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => k.Attributes.SetAsync(
            series.Urn, [new AttributeAssignment(Recurrence.AttributeKey, null)], Ct));

        // A finished Task doesn't start repeating.
        var done = await k.Subjects.CreateAsync(SubjectTypes.Task, Unique("Done thing"), cancellationToken: Ct);
        await k.Bounties.SetNothingAsync(done.Urn, BountyTriggers.Completed, cancellationToken: Ct);
        await k.Status.ChangeStatusAsync(done.Urn, "Completed", Ct);
        var finished = await Assert.ThrowsAsync<ArgumentException>(
            () => k.Recurrence.SetAsync(done.Urn, Recurrence.Daily(), Ct));
        Assert.Contains("finished", finished.Message);

        // Once each week and after the last completion are for Tasks.
        var habit = await k.Subjects.CreateAsync(SubjectTypes.Habit, Unique("Floss"), cancellationToken: Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => k.Recurrence.SetAsync(habit.Urn, Recurrence.Each("week"), Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => k.Recurrence.SetAsync(habit.Urn, Recurrence.Interval(3, "days", Recurrence.FromCompletion), Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => k.Recurrence.SetAsync(habit.Urn, Recurrence.Daily(), Monday, Ct));
    }

    [Fact]
    public async Task A_series_is_never_timed_and_a_Task_never_repeats_on_a_cue()
    {
        var k = Build();
        var (series, _) = await SeriesAsync(k, "Dust shelves", Recurrence.Weekly(["thu"]), Monday);

        var timing = await Assert.ThrowsAsync<InvalidOperationException>(
            () => k.Timers.ApplyAsync(series.Urn, TimerActions.Start, Ct));
        Assert.Contains("repeating task", timing.Message);

        // A cue has no dates: the Task would be a series that never makes a repetition.
        var cue = await k.Subjects.CreateAsync(SubjectTypes.Task, Unique("Cue task"), cancellationToken: Ct);
        var refused = await Assert.ThrowsAsync<ArgumentException>(
            () => k.Recurrence.SetAsync(cue.Urn, Recurrence.Trigger("after dinner"), null, Ct));
        Assert.Contains("A cue is for Habits", refused.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => k.Attributes.SetAsync(
            cue.Urn, [new AttributeAssignment(Recurrence.AttributeKey, Recurrence.Trigger("after dinner"))], Ct));
        Assert.Null(await AttributeAsync(cue.Id, Recurrence.AttributeKey));
    }

    // -----------------------------------------------------------------------------------
    // Changing the series
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task Changing_the_pattern_re_dates_the_open_repetition_whose_date_has_not_passed()
    {
        var k = Build();
        var (series, made) = await SeriesAsync(k, "Laundry", Recurrence.Weekly(["wed"]), Monday);
        var rep = made[0];

        await k.Recurrence.SetAsync(series.Urn, Recurrence.Weekly(["fri"]), null, Ct);
        var friday = Assert.Single(await RepetitionsAsync(k, series));
        Assert.Equal(rep.Id, friday.Id);
        Assert.Equal((Monday.AddDays(4), Monday.AddDays(4), (DateOnly?)null), (friday.Occurrence, friday.Due!.Value, friday.Scheduled));

        await k.Recurrence.SetAsync(series.Urn, Recurrence.Each("week"), null, Ct);
        var week = Assert.Single(await RepetitionsAsync(k, series));
        Assert.Equal((Monday, Monday.AddDays(6), (DateOnly?)Monday), (week.Occurrence, week.Due!.Value, week.Scheduled));

        await k.Attributes.SetAsync(
            series.Urn, [new AttributeAssignment(Recurrence.AttributeKey, Recurrence.Interval(14, "days", Recurrence.FromCompletion))], Ct);
        var after = Assert.Single(await RepetitionsAsync(k, series));
        Assert.Equal((Monday, Monday, (DateOnly?)null), (after.Occurrence, after.Due!.Value, after.Scheduled));

        // Once its date has passed it keeps it, and the new pattern dates the next.
        await k.Recurrence.SetAsync(series.Urn, Recurrence.Weekly(["wed"]), null, Ct);
        k.Clock.On(Monday.AddDays(3));
        var changed = await k.Recurrence.SetAsync(series.Urn, Recurrence.Weekly(["fri"]), null, Ct);
        Assert.Single(changed.Made);
        Assert.Equal([Monday.AddDays(2), Monday.AddDays(4)], (await RepetitionsAsync(k, series)).Select(r => r.Occurrence));
    }

    [Fact]
    public async Task Repetitions_copy_the_series_and_its_edits_follow_to_the_open_ones()
    {
        var k = Build();
        var project = await k.Subjects.CreateAsync(SubjectTypes.Project, Unique("Keep the house"), cancellationToken: Ct);
        var series = await k.Subjects.CreateAsync(
            SubjectTypes.Task, Unique("Vacuum"),
            """{"description":"Upstairs too","priority":"High","estimated_duration":"30m"}""", cancellationToken: Ct);
        await k.Bounties.SetNothingAsync(series.Urn, BountyTriggers.Completed, cancellationToken: Ct);
        await k.Tags.ApplyAsync(series.Urn, ["chores"], [], Ct);
        await k.Relations.LinkAsync(series.Urn, "serves", project.Urn, Ct);

        var rep1 = Assert.Single((await k.Recurrence.SetAsync(series.Urn, Recurrence.Weekly(["tue"]), Monday, Ct)).Made);
        Assert.Equal("Upstairs too", await AttributeAsync(rep1.Id, "description"));
        Assert.Equal("High", await AttributeAsync(rep1.Id, "priority"));
        Assert.Equal("30m", await AttributeAsync(rep1.Id, "estimated_duration"));
        Assert.Null(await AttributeAsync(rep1.Id, Bounty.AttributeKey));
        Assert.Equal(["chores"], await TagsAsync(rep1.Id));
        await using (var c = await OpenAsync())
        {
            Assert.Equal(project.Id, await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "SELECT to_subject FROM bsk.subject_relation WHERE from_subject = @id AND relation = 'serves';",
                new { id = rep1.Id }, cancellationToken: Ct)));
        }

        // An edit follows; one changed on the repetition by hand keeps its own.
        await k.Attributes.SetAsync(rep1.Urn, [new AttributeAssignment("priority", "Low")], Ct);
        await k.Attributes.SetAsync(
            series.Urn, [new AttributeAssignment("description", "Stairs too"), new AttributeAssignment("priority", "Medium")], Ct);
        Assert.Equal("Stairs too", await AttributeAsync(rep1.Id, "description"));
        Assert.Equal("Low", await AttributeAsync(rep1.Id, "priority"));

        // A rename follows to the open one.
        var renamed = await k.Rename.RenameAsync(series.Urn, Unique("Hoover"), Ct);
        Assert.Equal(1, renamed.OccurrencesRenamed);
        Assert.Equal(renamed.Subject.Title, await TitleAsync(rep1.Id));

        // Done: the next takes the series as it is now; a later rename leaves the done one be.
        k.Clock.On(Monday.AddDays(1));
        var rep2 = Assert.Single((await k.Status.ChangeStatusAsync(rep1.Urn, "Completed", Ct)).Repetitions);
        Assert.Equal(renamed.Subject.Title, rep2.Title);
        Assert.Equal("Stairs too", await AttributeAsync(rep2.Id, "description"));
        Assert.Equal("Medium", await AttributeAsync(rep2.Id, "priority"));
        var doneTitle = renamed.Subject.Title;
        var again = await k.Rename.RenameAsync(series.Urn, Unique("Vacuum all"), Ct);
        Assert.Equal(doneTitle, await TitleAsync(rep1.Id));
        Assert.Equal(again.Subject.Title, await TitleAsync(rep2.Id));

        // Tags follow to the open one too.
        await k.Tags.ApplyAsync(series.Urn, ["weekly"], ["chores"], Ct);
        Assert.Equal(["weekly"], await TagsAsync(rep2.Id));
        Assert.Equal(["chores"], await TagsAsync(rep1.Id));
    }

    // -----------------------------------------------------------------------------------
    // The store
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task Settles_at_once_make_each_date_once()
    {
        var k = Build();
        var (series, _) = await SeriesAsync(k, "Feed fish", Recurrence.Daily(), Monday);

        k.Clock.On(Monday.AddDays(9));
        var settles = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => k.Series.SettleSeriesAsync(series.Id, Ct)));
        Assert.Equal(9, settles.Sum(s => s.Count));
        var all = await RepetitionsAsync(k, series);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => Monday.AddDays(i)), all.Select(r => r.Occurrence));

        // The store refuses a second repetition for a date.
        await using var c = await OpenAsync();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title, attributes)
            VALUES (@urn, 'Task', 'dup', jsonb_build_object('series', @series, 'occurrence', @occurrence));
            """,
            new { urn = Urns.Build(SubjectTypes.Task, "dup"), series = series.Urn, occurrence = Iso(Monday) },
            cancellationToken: Ct)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    [Fact]
    public async Task Settle_catches_up_every_series_owed_by_today()
    {
        // Set up ten days ago, and nothing has run since.
        var past = Build(RealToday.AddDays(-10));
        var (series, _) = await SeriesAsync(past, "Check mail", Recurrence.Daily(), RealToday.AddDays(-10));

        var now = Build(realClock: true);
        var result = await now.Series.SettleAsync(Ct);
        Assert.True(result.Series >= 1);
        var mine = await RepetitionsAsync(now, series);
        Assert.Equal(11, mine.Count);
        Assert.Equal(RealToday, mine[^1].Occurrence);
        Assert.Superset(mine.Skip(1).Select(r => r.Id).ToHashSet(), result.Made.Select(m => m.Id).ToHashSet());

        await now.Status.ChangeStatusAsync(series.Urn, "Cancelled", Ct);
    }

    [Fact]
    public async Task A_pattern_stored_without_a_start_gets_one_when_first_settled()
    {
        var k = Build();
        var series = await k.Subjects.CreateAsync(
            SubjectTypes.Task, Unique("Made with attributes"),
            $$"""{"recurrence":{"freq":"weekly","on":["mon"]},"due":"{{Iso(Monday.AddDays(7))}}"}""", cancellationToken: Ct);

        var made = await k.Series.SettleSeriesAsync(series.Id, Ct);
        Assert.Equal(Iso(Monday.AddDays(7)), await AttributeAsync(series.Id, TaskSeries.StartKey));
        Assert.Equal(Monday.AddDays(7), Assert.Single(await RepetitionsAsync(k, series)).Occurrence);
        Assert.Single(made);
    }

    [Fact]
    public async Task The_expander_reads_once_each_week_and_gives_no_dates_for_after_completion()
    {
        await using var c = await OpenAsync();
        async Task<List<string>> Expand(string recurrence, string anchor, string start, string end)
            => (await c.QueryAsync<string>(new CommandDefinition(
                "SELECT to_char(d, 'YYYY-MM-DD') FROM bsk.recurrence_occurrences(@r::jsonb, @anchor::date, @start::date, @end::date) d ORDER BY d;",
                new { r = recurrence, anchor, start, end }, cancellationToken: Ct))).AsList();

        // 2026-01-07 is a Wednesday: its week's Monday, and the next two that overlap the window.
        Assert.Equal(
            ["2026-01-05", "2026-01-12", "2026-01-19"],
            await Expand(Recurrence.Each("week"), "2026-01-07", "2026-01-07", "2026-01-20"));
        Assert.Empty(await Expand(Recurrence.Interval(30, "days", Recurrence.FromCompletion), "2026-01-01", "2026-01-01", "2026-12-31"));
        Assert.Empty(await Expand("""{"freq":"each","period":"month"}""", "2026-01-01", "2026-01-01", "2026-12-31"));
    }

    [Fact]
    public async Task A_series_is_never_neglected_but_a_plain_cadenced_task_is()
    {
        var k = Build();
        var (series, _) = await SeriesAsync(k, "Cadenced chore", Recurrence.Weekly(["mon"]), Monday);
        var plain = await k.Subjects.CreateAsync(SubjectTypes.Task, Unique("Cadenced plain"), cancellationToken: Ct);
        await using (var c = await OpenAsync())
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE bsk.subject
                SET attributes = attributes || '{"expected_cadence":"daily"}'::jsonb,
                    created_at = now() - interval '30 days'
                WHERE id = ANY(@ids);
                """,
                new { ids = new[] { series.Id, plain.Id } }, cancellationToken: Ct));
        }

        var report = await new DiagnosticRunner(postgres.ConnectionString).RunAsync(only: "neglect", cancellationToken: Ct);
        var findings = Assert.Single(report.Diagnostics).Findings;
        Assert.DoesNotContain(findings, f => f.Subject.Id == series.Id);
        Assert.Contains(findings, f => f.Subject.Id == plain.Id);
    }

    // -----------------------------------------------------------------------------------
    // The pattern's shapes
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_new_shapes_build_and_canonicalize()
    {
        Assert.Equal("""{"freq":"each","period":"week"}""", Recurrence.Each(" Week "));
        Assert.Equal(
            """{"freq":"interval","every":30,"unit":"days","from":"completion"}""",
            Recurrence.Interval(30, "days", "completion"));
        Assert.Equal(
            Recurrence.Each("week"),
            Recurrence.Build(Recurrence.FreqEach, null, null, null, null, null, period: "week"));
        Assert.Equal(
            Recurrence.Interval(2, "weeks", Recurrence.FromCompletion),
            Recurrence.Build(Recurrence.FreqInterval, null, 2, "weeks", null, null, from: "completion"));
        Assert.Equal(Recurrence.Each("week"), Recurrence.Canonicalize("""{"period":"week","freq":"each"}"""));
        Assert.Equal(
            Recurrence.Interval(30, "days", Recurrence.FromCompletion),
            Recurrence.Canonicalize("""{"freq":"interval","every":30,"unit":"days","from":"completion"}"""));

        Assert.Throws<ArgumentException>(() => Recurrence.Each("month"));
        Assert.Throws<ArgumentException>(() => Recurrence.Interval(3, "days", "start"));
        Assert.Throws<ArgumentException>(
            () => Recurrence.Build(Recurrence.FreqWeekly, ["mon"], null, null, null, null, from: "completion"));

        Assert.Equal(RepeatKind.EachWeek, TaskSeries.KindOf(Recurrence.Each("week")));
        Assert.Equal(RepeatKind.AfterCompletion, TaskSeries.KindOf(Recurrence.Interval(30, "days", "completion")));
        Assert.Equal(RepeatKind.Calendar, TaskSeries.KindOf(Recurrence.Interval(30, "days")));
        Assert.Equal(RepeatKind.Calendar, TaskSeries.KindOf(Recurrence.MonthlyLast()));
        Assert.Equal(RepeatKind.None, TaskSeries.KindOf(Recurrence.Trigger("after lunch")));
        Assert.Equal(RepeatKind.None, TaskSeries.KindOf(null));

        // Months after a completion land on the month's last day when it's shorter.
        Assert.Equal(new DateOnly(2026, 2, 28), TaskSeries.AfterCompletion(Recurrence.Interval(1, "months", "completion"), new DateOnly(2026, 1, 31)));
        Assert.Equal(new DateOnly(2026, 10, 19), TaskSeries.WeekOf(new DateOnly(2026, 10, 25)));
        Assert.Equal(new DateOnly(2026, 10, 19), TaskSeries.WeekOf(new DateOnly(2026, 10, 19)));
        Assert.Contains("Missed", StatusVocabulary.For(SubjectTypes.Task));
    }
}

using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Persistence;

namespace LifeOs.Api.Read;

/// <summary>
/// The Activity page's reads (ACTIVITY-2..4, ACTIVITY-6, activity plan §3.6, §3.9) on the <c>bsk_reader</c>
/// connection, like every other API read: <see cref="ActivityReads"/> over the Oura views of
/// migration 0037, so a day reads exactly as <c>bsk oura</c> prints it, and the ledger's
/// activity awards, so each day carries what it paid (activity plan A9). The step thresholds go
/// out with the days. The one write, a workout's note, goes through
/// <see cref="WorkoutNoteService"/>; syncing goes through <see cref="OuraSyncService"/>.
/// </summary>
public sealed class ActivityReader(string readerConnectionString, string homeTimeZone = HomeZone.Default, IClock? clock = null)
{
    /// <summary>The days a range covers when it gives no start: 30, ending on its last day.</summary>
    public const int DefaultDays = 30;

    /// <summary>The weeks of workouts a range covers when it gives no start: 12, Monday to Sunday.</summary>
    public const int DefaultWeeks = 12;

    private readonly ActivityReads reads = new(
        new NpgsqlActivityRepository(readerConnectionString),
        new NpgsqlHomeCalendar(readerConnectionString, homeTimeZone),
        clock ?? new SystemClock());

    private readonly ActivityPaidReads paid = new(new NpgsqlLedgerRepository(readerConnectionString, homeTimeZone));

    /// <summary>
    /// Every day from <paramref name="from"/> to <paramref name="to"/>, oldest first, at most
    /// <see cref="ActivityReads.MaxRangeDays"/>; a missing end defaults to today, a missing start
    /// to <see cref="DefaultDays"/> days before the end.
    /// </summary>
    public async Task<ActivityDaysRead> GetDaysAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var today = await reads.TodayAsync(cancellationToken);
        var end = to ?? today;
        var start = from ?? end.AddDays(1 - DefaultDays);
        var days = await reads.GetDailyAsync(start, end, cancellationToken);
        var paidByDay = await paid.GetPaidAsync(start, end, cancellationToken);
        return new ActivityDaysRead
        {
            From = ActivityRows.Day(start),
            To = ActivityRows.Day(end),
            Today = ActivityRows.Day(today),
            MinimumSteps = ActivityTargets.MinimumSteps,
            GoalSteps = ActivityTargets.GoalSteps,
            Days = days.Select(d => ActivityRows.From(d, paidByDay.GetValueOrDefault(d.Day) ?? ActivityDayPaid.None)).ToList(),
        };
    }

    /// <summary>
    /// Live workouts from <paramref name="from"/> to <paramref name="to"/>, newest first, counted
    /// or not; a missing end defaults to today, a missing start to the Monday
    /// <see cref="DefaultWeeks"/> weeks back, so the range is whole weeks.
    /// </summary>
    public async Task<ActivityWorkoutsRead> GetWorkoutsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var today = await reads.TodayAsync(cancellationToken);
        var end = to ?? today;
        var start = from ?? Monday(end).AddDays(-7 * (DefaultWeeks - 1));
        var workouts = await reads.GetWorkoutsAsync(start, end, all: true, cancellationToken);
        return new ActivityWorkoutsRead
        {
            From = ActivityRows.Day(start),
            To = ActivityRows.Day(end),
            Today = ActivityRows.Day(today),
            Workouts = workouts.Select(ActivityRows.From).ToList(),
        };
    }

    /// <summary>
    /// Live tags from the Oura app starting from <paramref name="from"/> to <paramref name="to"/>,
    /// newest first (activity plan §3.9); a missing end defaults to today, a missing start to
    /// <see cref="DefaultDays"/> days before the end.
    /// </summary>
    public async Task<ActivityTagsRead> GetTagsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var today = await reads.TodayAsync(cancellationToken);
        var end = to ?? today;
        var start = from ?? end.AddDays(1 - DefaultDays);
        var tags = await reads.GetTagsAsync(start, end, cancellationToken);
        return new ActivityTagsRead
        {
            From = ActivityRows.Day(start),
            To = ActivityRows.Day(end),
            Today = ActivityRows.Day(today),
            Tags = tags.Select(ActivityRows.From).ToList(),
        };
    }

    /// <summary>LifeOS holds a live workout with exactly that Oura id.</summary>
    public async Task<bool> HasWorkoutAsync(string id, CancellationToken cancellationToken = default)
        => await reads.GetWorkoutAsync(id, cancellationToken) is not null;

    /// <summary>One day in full (default today).</summary>
    public async Task<ActivityDayDetailRead> GetDayAsync(DateOnly? date, CancellationToken cancellationToken = default)
    {
        var today = await reads.TodayAsync(cancellationToken);
        var detail = await reads.GetDayAsync(date ?? today, cancellationToken);
        return new ActivityDayDetailRead
        {
            Day = ActivityRows.Day(detail.Daily.Day),
            Today = ActivityRows.Day(today),
            MinimumSteps = ActivityTargets.MinimumSteps,
            GoalSteps = ActivityTargets.GoalSteps,
            Summary = ActivityRows.From(detail.Daily, await paid.GetDayAsync(detail.Daily.Day, cancellationToken)),
            SleepContributors = detail.Daily.Sleep?.Contributors ?? new Dictionary<string, int?>(),
            ReadinessContributors = detail.Daily.Readiness?.Contributors ?? new Dictionary<string, int?>(),
            TemperatureDeviation = detail.Daily.Readiness?.TemperatureDeviation,
            TemperatureTrendDeviation = detail.Daily.Readiness?.TemperatureTrendDeviation,
            Night = detail.Night is { } night ? ActivityRows.From(night) : null,
            Naps = detail.Naps.Select(ActivityRows.From).ToList(),
            Workouts = detail.Workouts.Select(ActivityRows.From).ToList(),
            Tags = detail.Tags.Select(ActivityRows.From).ToList(),
        };
    }

    /// <summary>Today so far and this week against last; the connection is the caller's to add.</summary>
    public async Task<ActivitySummaryRead> GetSummaryAsync(OuraStatus oura, CancellationToken cancellationToken = default)
    {
        var summary = await reads.GetSummaryAsync(cancellationToken);
        return new ActivitySummaryRead
        {
            Today = ActivityRows.Day(summary.Today),
            MinimumSteps = ActivityTargets.MinimumSteps,
            GoalSteps = ActivityTargets.GoalSteps,
            TodaySoFar = ActivityRows.From(summary.TodaySoFar, await paid.GetDayAsync(summary.TodaySoFar.Day, cancellationToken)),
            TodayWorkouts = summary.TodayWorkouts.Select(ActivityRows.From).ToList(),
            ThisWeek = ActivityRows.From(summary.ThisWeek),
            LastWeek = ActivityRows.From(summary.LastWeek),
            Oura = OuraStatusRead.From(oura),
        };
    }

    private static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}

using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>A day's sleep and readiness side by side; either may be missing.</summary>
public sealed record SleepAndReadiness(DateOnly Day, SleepDay? Sleep, ReadinessDay? Readiness);

/// <summary>A day of everything Oura says about it: activity, sleep and readiness, any of them missing.</summary>
public sealed record ActivityDaily(DateOnly Day, ActivityDay? Activity, SleepDay? Sleep, ReadinessDay? Readiness);

/// <summary>
/// One day in full, for the page's detail sheet: the day, its night (the sleep period
/// <c>bsk.v_sleep_day</c> takes as the night) with its stages, every other period as a nap,
/// and its workouts and the tags starting on it (activity plan §3.9), each earliest first.
/// </summary>
public sealed record ActivityDayDetail(
    ActivityDaily Daily,
    SleepPeriod? Night,
    IReadOnlyList<SleepPeriod> Naps,
    IReadOnlyList<Workout> Workouts,
    IReadOnlyList<ActivityTag> Tags);

/// <summary>
/// A week, Monday to Sunday, as the Activity page compares it with the one before. Today is
/// still going, so it counts toward <see cref="GoalDays"/> once it reaches the goal, but it isn't
/// averaged or counted under the minimum until it's over.
/// </summary>
/// <param name="AverageSteps">The average over <see cref="StepDays"/>, rounded; null when there are none.</param>
/// <param name="StepDays">The days averaged: those before today with a step count.</param>
/// <param name="GoalDays">Days at the 7,500 goal, today included.</param>
/// <param name="UnderDays">Days before today under the 5,000 minimum. A day with no record isn't one.</param>
/// <param name="Sessions">Workouts that count as exercise.</param>
/// <param name="ExerciseSeconds">Their time.</param>
/// <param name="ExerciseCalories">Their active calories, where Oura has them.</param>
/// <param name="AverageSleep">The average sleep score over the days with one, rounded.</param>
/// <param name="AverageReadiness">The average readiness score over the days with one, rounded.</param>
public sealed record ActivityWeek(
    DateOnly From,
    DateOnly To,
    int? AverageSteps,
    int StepDays,
    int GoalDays,
    int UnderDays,
    int Sessions,
    int ExerciseSeconds,
    int ExerciseCalories,
    int? AverageSleep,
    int? AverageReadiness);

/// <summary>
/// The Activity page's summary: today so far (its steps, and last night's sleep and readiness,
/// which Oura puts on the morning they end), today's workouts, and this week against last.
/// </summary>
public sealed record ActivitySummary(
    DateOnly Today, ActivityDaily TodaySoFar, IReadOnlyList<Workout> TodayWorkouts, ActivityWeek ThisWeek, ActivityWeek LastWeek);

/// <summary>
/// Oura's data as LifeOS holds it (ACTIVITY-2..4, activity plan §3.6): what <c>bsk oura</c>
/// prints, and from A3 what the API's reader serves. Days are Oura's own; "today" is the home
/// zone's.
/// </summary>
public sealed class ActivityReads(IActivityRepository activity, IHomeCalendar calendar, IClock clock)
{
    /// <summary>The widest range a read answers, in days.</summary>
    public const int MaxRangeDays = 731;

    /// <summary>Today in the home zone.</summary>
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
        => calendar.DateAtAsync(clock.UtcNow, cancellationToken);

    /// <summary>The last <paramref name="days"/> days through today.</summary>
    public async Task<(DateOnly From, DateOnly To)> LastDaysAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days < 1 || days > MaxRangeDays)
        {
            throw new ArgumentException($"Ask for 1 to {MaxRangeDays} days.");
        }

        var today = await TodayAsync(cancellationToken);
        return (today.AddDays(1 - days), today);
    }

    /// <summary>Days of steps and activity in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    public Task<IReadOnlyList<ActivityDay>> GetDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        CheckRange(from, to);
        return activity.GetActivityDaysAsync(from, to, cancellationToken);
    }

    /// <summary>
    /// Live workouts on the days [<paramref name="from"/>, <paramref name="to"/>], newest first;
    /// with <paramref name="all"/> false, only those that count as exercise.
    /// </summary>
    public async Task<IReadOnlyList<Workout>> GetWorkoutsAsync(
        DateOnly from, DateOnly to, bool all = true, CancellationToken cancellationToken = default)
    {
        CheckRange(from, to);
        var workouts = await activity.GetWorkoutsAsync(from, to, cancellationToken);
        return all ? workouts : workouts.Where(w => w.Verdict.Counted).ToList();
    }

    /// <summary>The live workout with exactly that Oura id, or null.</summary>
    public Task<Workout?> GetWorkoutAsync(string id, CancellationToken cancellationToken = default)
        => activity.GetWorkoutAsync(id, cancellationToken);

    /// <summary>
    /// Live tags from the Oura app starting on the days [<paramref name="from"/>,
    /// <paramref name="to"/>], newest first (activity plan §3.9).
    /// </summary>
    public Task<IReadOnlyList<ActivityTag>> GetTagsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        CheckRange(from, to);
        return activity.GetTagsAsync(from, to, cancellationToken);
    }

    /// <summary>Each day in [<paramref name="from"/>, <paramref name="to"/>] with a sleep or readiness record, oldest first.</summary>
    public async Task<IReadOnlyList<SleepAndReadiness>> GetSleepAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        CheckRange(from, to);
        var sleep = (await activity.GetSleepDaysAsync(from, to, cancellationToken)).ToDictionary(s => s.Day);
        var readiness = (await activity.GetReadinessDaysAsync(from, to, cancellationToken)).ToDictionary(r => r.Day);
        return sleep.Keys.Union(readiness.Keys)
            .Order()
            .Select(day => new SleepAndReadiness(day, sleep.GetValueOrDefault(day), readiness.GetValueOrDefault(day)))
            .ToList();
    }

    /// <summary>
    /// Every day in [<paramref name="from"/>, <paramref name="to"/>], oldest first, with whatever
    /// LifeOS holds for it: a day with nothing is still there, so a chart shows the gap.
    /// </summary>
    public async Task<IReadOnlyList<ActivityDaily>> GetDailyAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        CheckRange(from, to);
        var days = (await activity.GetActivityDaysAsync(from, to, cancellationToken)).ToDictionary(d => d.Day);
        var sleep = (await activity.GetSleepDaysAsync(from, to, cancellationToken)).ToDictionary(s => s.Day);
        var readiness = (await activity.GetReadinessDaysAsync(from, to, cancellationToken)).ToDictionary(r => r.Day);
        return Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(from.AddDays)
            .Select(day => new ActivityDaily(
                day, days.GetValueOrDefault(day), sleep.GetValueOrDefault(day), readiness.GetValueOrDefault(day)))
            .ToList();
    }

    /// <summary>One day in full (<see cref="ActivityDayDetail"/>).</summary>
    public async Task<ActivityDayDetail> GetDayAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var daily = (await GetDailyAsync(day, day, cancellationToken))[0];
        var periods = await activity.GetSleepPeriodsAsync(day, day, cancellationToken);

        // The night as v_sleep_day chose it: the long_sleep it took its bed and wake times from.
        var night = daily.Sleep is { BedAt: { } bed, WokeAt: { } woke }
            ? periods.FirstOrDefault(p => p.Type == SleepTypes.LongSleep && p.StartedAt == bed && p.EndedAt == woke)
            : null;
        var naps = periods.Where(p => !ReferenceEquals(p, night)).ToList();
        var workouts = (await activity.GetWorkoutsAsync(day, day, cancellationToken))
            .OrderBy(w => w.StartedAt).ThenBy(w => w.Id, StringComparer.Ordinal)
            .ToList();
        var tags = (await activity.GetTagsAsync(day, day, cancellationToken))
            .OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        return new ActivityDayDetail(daily, night, naps, workouts, tags);
    }

    /// <summary>Today so far and this week (Monday to Sunday) against last (<see cref="ActivitySummary"/>).</summary>
    public async Task<ActivitySummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var today = await TodayAsync(cancellationToken);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var lastMonday = monday.AddDays(-7);
        var daily = await GetDailyAsync(lastMonday, today, cancellationToken);
        var workouts = await activity.GetWorkoutsAsync(lastMonday, today, cancellationToken);

        ActivityWeek Week(DateOnly from, DateOnly to)
        {
            var days = daily.Where(d => d.Day >= from && d.Day <= to).ToList();
            var complete = days
                .Where(d => d.Day < today && d.Activity?.Steps is not null)
                .Select(d => d.Activity!.Steps!.Value)
                .ToList();
            var counted = workouts.Where(w => w.Day >= from && w.Day <= to && w.Verdict.Counted).ToList();
            return new ActivityWeek(
                from,
                to,
                Average(complete.Select(s => (int?)s)),
                complete.Count,
                days.Count(d => d.Activity?.Steps >= ActivityTargets.GoalSteps),
                complete.Count(s => s < ActivityTargets.MinimumSteps),
                counted.Count,
                counted.Sum(w => w.Seconds),
                counted.Sum(w => w.Calories ?? 0),
                Average(days.Select(d => d.Sleep?.Score)),
                Average(days.Select(d => d.Readiness?.Score)));
        }

        return new ActivitySummary(
            today,
            daily[^1],
            workouts.Where(w => w.Day == today).ToList(),
            Week(monday, monday.AddDays(6)),
            Week(lastMonday, monday.AddDays(-1)));
    }

    private static int? Average(IEnumerable<int?> values)
    {
        var known = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return known.Count == 0 ? null : (int)Math.Round(known.Average(), MidpointRounding.AwayFromZero);
    }

    private static void CheckRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException($"The range ends ({OuraPayloads.Text(to)}) before it starts ({OuraPayloads.Text(from)}).");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            throw new ArgumentException($"Ask for up to {MaxRangeDays} days at a time.");
        }
    }
}

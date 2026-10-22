using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>A day's sleep and readiness side by side; either may be missing.</summary>
public sealed record SleepAndReadiness(DateOnly Day, SleepDay? Sleep, ReadinessDay? Readiness);

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

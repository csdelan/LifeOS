using System.Globalization;
using LifeOs.Application.Activity;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// <c>GET /api/oura/status</c> (ACTIVITY-1, activity plan §3.1): the Oura connection as the
/// Activity page shows it. It never carries a token.
/// </summary>
public sealed class OuraStatusRead
{
    /// <summary>
    /// <c>not_configured</c> (no Oura app on this server), <c>not_connected</c>,
    /// <c>reconnect</c> (Oura refused the stored sign-in) or <c>connected</c>.
    /// </summary>
    public string State { get; set; } = "";

    public bool Configured { get; set; }
    public bool Connected { get; set; }
    public bool NeedsReconnect { get; set; }

    /// <summary>Why Oura needs reconnecting, in words.</summary>
    public string? ReconnectReason { get; set; }

    public DateTimeOffset? ConnectedAt { get; set; }

    /// <summary>When the current access token lapses; it's refreshed before then.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>The scopes Oura granted (<c>daily</c>, <c>workout</c>).</summary>
    public IReadOnlyList<string> Scopes { get; set; } = [];

    /// <summary>Scopes LifeOS asked for that Oura didn't grant: the page asks to reconnect with them.</summary>
    public IReadOnlyList<string> MissingScopes { get; set; } = [];

    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }

    /// <summary>The earliest day (yyyy-MM-dd) the history has been read back to; each sync reads further until it's done.</summary>
    public string? HistoryFrom { get; set; }

    /// <summary>The history is all in: the sync found where Oura's history starts.</summary>
    public bool HistoryDone { get; set; }

    public static OuraStatusRead From(OuraStatus status) => new()
    {
        State = status.State,
        Configured = status.Configured,
        Connected = status.Connected,
        NeedsReconnect = status.NeedsReconnect,
        ReconnectReason = status.ReconnectReason,
        ConnectedAt = status.ConnectedAt,
        ExpiresAt = status.ExpiresAt,
        Scopes = status.Scopes,
        MissingScopes = status.MissingScopes,
        LastSyncedAt = status.LastSyncedAt,
        LastSyncError = status.LastSyncError,
        HistoryFrom = status.HistoryFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        HistoryDone = status.HistoryDone,
    };
}

/// <summary><c>POST /api/oura/authorize</c>: where to send the browser to approve LifeOS at Oura.</summary>
public sealed class OuraAuthorizeRead
{
    public string AuthorizeUrl { get; set; } = "";
}

/// <summary>
/// One day as the Activity page charts it (ACTIVITY-2, ACTIVITY-4, activity plan §3.6): its
/// steps and their band, its activity, and the sleep and readiness Oura puts on it (the night
/// that ended that morning). A field is null when LifeOS holds nothing for it. Times are in
/// seconds; <see cref="BedAt"/> and <see cref="WokeAt"/> are the night's, as instants.
/// </summary>
public sealed class ActivityDayRow
{
    public string Day { get; set; } = "";
    public int? Steps { get; set; }

    /// <summary><c>under</c> the 5,000 minimum, at the <c>minimum</c>, or at the <c>goal</c> of 7,500; null with no steps.</summary>
    public string? Band { get; set; }

    public int? ActiveCalories { get; set; }
    public int? TotalCalories { get; set; }
    public int? WalkingM { get; set; }
    public int? HighS { get; set; }
    public int? MediumS { get; set; }
    public int? LowS { get; set; }
    public int? NonWearS { get; set; }

    /// <summary>Oura's activity score.</summary>
    public int? ActivityScore { get; set; }

    public int? SleepScore { get; set; }
    public int? ReadinessScore { get; set; }
    public int? AsleepS { get; set; }
    public int? InBedS { get; set; }
    public DateTimeOffset? BedAt { get; set; }
    public DateTimeOffset? WokeAt { get; set; }

    /// <summary>How many naps there were.</summary>
    public int NapCount { get; set; }

    /// <summary>The naps' time asleep, in seconds.</summary>
    public int NapAsleepS { get; set; }
}

/// <summary>
/// <c>GET /api/activity/days?from&amp;to</c>: every day of the range, oldest first, at most 731,
/// with the step thresholds, so the page never hard-codes them.
/// </summary>
public sealed class ActivityDaysRead
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Today { get; set; } = "";
    public int MinimumSteps { get; set; }
    public int GoalSteps { get; set; }
    public IReadOnlyList<ActivityDayRow> Days { get; set; } = [];
}

/// <summary>
/// A live workout (ACTIVITY-3): Oura's fields, whether it counts as exercise and, when it
/// doesn't, why (in words), and Chris's note. <see cref="Activity"/>, <see cref="Intensity"/>
/// and <see cref="Source"/> are Oura's own values, which it keeps adding to.
/// </summary>
public sealed class WorkoutRow
{
    public string Id { get; set; } = "";
    public string Day { get; set; } = "";
    public string Activity { get; set; } = "";
    public string? Label { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public int Seconds { get; set; }
    public int? Calories { get; set; }
    public int? DistanceM { get; set; }
    public string? Intensity { get; set; }
    public string Source { get; set; } = "";
    public bool Counted { get; set; }

    /// <summary>Why it doesn't count, when it doesn't.</summary>
    public string? Reason { get; set; }

    public string? Note { get; set; }
    public DateTimeOffset? NoteAt { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

/// <summary><c>GET /api/activity/workouts?from&amp;to</c>: live workouts on those days, newest first, counted or not.</summary>
public sealed class ActivityWorkoutsRead
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Today { get; set; } = "";
    public IReadOnlyList<WorkoutRow> Workouts { get; set; } = [];
}

/// <summary>A sleep period: the night or a nap, with its stages. Durations are in seconds.</summary>
public sealed class SleepPeriodRow
{
    public string Id { get; set; } = "";

    /// <summary>Oura's type: <c>long_sleep</c>, <c>sleep</c> (a nap), <c>late_nap</c>, or one Oura adds later.</summary>
    public string Type { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public int? InBedS { get; set; }
    public int? AsleepS { get; set; }
    public int? DeepS { get; set; }
    public int? LightS { get; set; }
    public int? RemS { get; set; }
    public int? AwakeS { get; set; }
    public int? LatencyS { get; set; }
    public int? Efficiency { get; set; }
    public int? AverageHrv { get; set; }
    public double? AverageHr { get; set; }
    public int? LowestHr { get; set; }
}

/// <summary>
/// <c>GET /api/activity/day?date=</c>: one day in full, for the detail sheet — the day's row,
/// both scores' contributors (by Oura's names, null where Oura gave none), the temperature
/// deviation, the night with its stages, the naps, and the day's workouts, earliest first.
/// </summary>
public sealed class ActivityDayDetailRead
{
    public string Day { get; set; } = "";
    public string Today { get; set; } = "";
    public int MinimumSteps { get; set; }
    public int GoalSteps { get; set; }
    public ActivityDayRow Summary { get; set; } = new();
    public IReadOnlyDictionary<string, int?> SleepContributors { get; set; } = new Dictionary<string, int?>();
    public IReadOnlyDictionary<string, int?> ReadinessContributors { get; set; } = new Dictionary<string, int?>();

    /// <summary>Body temperature against Chris's baseline, in °C.</summary>
    public double? TemperatureDeviation { get; set; }

    public double? TemperatureTrendDeviation { get; set; }
    public SleepPeriodRow? Night { get; set; }
    public IReadOnlyList<SleepPeriodRow> Naps { get; set; } = [];
    public IReadOnlyList<WorkoutRow> Workouts { get; set; } = [];
}

/// <summary>
/// A week, Monday to Sunday (<see cref="ActivityWeek"/>). Today is still going: it counts toward
/// <see cref="GoalDays"/> once it reaches the goal, but isn't averaged or counted under the
/// minimum until it's over.
/// </summary>
public sealed class ActivityWeekRow
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";

    /// <summary>The average over <see cref="StepDays"/>; null when there are none yet (a Monday).</summary>
    public int? AverageSteps { get; set; }

    /// <summary>The days averaged: those before today with a step count.</summary>
    public int StepDays { get; set; }

    public int GoalDays { get; set; }
    public int UnderDays { get; set; }

    /// <summary>Workouts that count as exercise.</summary>
    public int Sessions { get; set; }

    /// <summary>Their time, in seconds.</summary>
    public int ExerciseSeconds { get; set; }

    /// <summary>Their active calories, where Oura has them.</summary>
    public int ExerciseCalories { get; set; }

    public int? AverageSleep { get; set; }
    public int? AverageReadiness { get; set; }
}

/// <summary>
/// <c>GET /api/activity/summary</c>: today so far — its steps, and last night's sleep and
/// readiness, which Oura puts on the morning they end — today's workouts, this week against
/// last week, and the Oura connection with its last sync.
/// </summary>
public sealed class ActivitySummaryRead
{
    public string Today { get; set; } = "";
    public int MinimumSteps { get; set; }
    public int GoalSteps { get; set; }
    public ActivityDayRow TodaySoFar { get; set; } = new();
    public IReadOnlyList<WorkoutRow> TodayWorkouts { get; set; } = [];
    public ActivityWeekRow ThisWeek { get; set; } = new();
    public ActivityWeekRow LastWeek { get; set; } = new();
    public OuraStatusRead Oura { get; set; } = new();
}

/// <summary>
/// <c>POST /api/activity/sync</c>: whether the sync ran (one on opening the page is skipped when
/// LifeOS synced under 5 minutes ago), what it changed, when LifeOS last synced, and what
/// settling activity XP wrote afterwards (activity plan §3.8): the ledger entries, empty when
/// nothing changed, and each life domain whose level or rank moved.
/// </summary>
public sealed class ActivitySyncRead
{
    public bool Ran { get; set; }

    /// <summary>The sync wrote something: the page should read its cards again.</summary>
    public bool Changed { get; set; }

    /// <summary>The first day of the recent window (yyyy-MM-dd), when it ran.</summary>
    public string? From { get; set; }

    /// <summary>The last day synced (tomorrow), when it ran.</summary>
    public string? To { get; set; }

    /// <summary>Records Oura answered for those days.</summary>
    public int Records { get; set; }

    /// <summary>Versions written: new and changed records, and withdrawals.</summary>
    public int Written { get; set; }

    /// <summary>Workouts and sleep periods withdrawn because Oura no longer has them.</summary>
    public int Voided { get; set; }

    public string? HistoryFrom { get; set; }
    public bool HistoryDone { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public IReadOnlyList<AwardRow> Awards { get; set; } = [];
    public IReadOnlyList<DomainChangeRow> Progress { get; set; } = [];
}

/// <summary>Maps <see cref="ActivityReads"/>' answers to the API's rows.</summary>
public static class ActivityRows
{
    public static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static ActivityDayRow From(ActivityDaily daily) => new()
    {
        Day = Day(daily.Day),
        Steps = daily.Activity?.Steps,
        Band = daily.Activity?.Steps is { } steps ? ActivityTargets.Band(steps) : null,
        ActiveCalories = daily.Activity?.ActiveCalories,
        TotalCalories = daily.Activity?.TotalCalories,
        WalkingM = daily.Activity?.WalkingM,
        HighS = daily.Activity?.HighS,
        MediumS = daily.Activity?.MediumS,
        LowS = daily.Activity?.LowS,
        NonWearS = daily.Activity?.NonWearS,
        ActivityScore = daily.Activity?.Score,
        SleepScore = daily.Sleep?.Score,
        ReadinessScore = daily.Readiness?.Score,
        AsleepS = daily.Sleep?.AsleepS,
        InBedS = daily.Sleep?.InBedS,
        BedAt = daily.Sleep?.BedAt,
        WokeAt = daily.Sleep?.WokeAt,
        NapCount = daily.Sleep?.Naps ?? 0,
        NapAsleepS = daily.Sleep?.NapS ?? 0,
    };

    public static WorkoutRow From(Workout workout)
    {
        var verdict = workout.Verdict;
        return new WorkoutRow
        {
            Id = workout.Id,
            Day = Day(workout.Day),
            Activity = workout.Activity,
            Label = workout.Label,
            StartedAt = workout.StartedAt,
            EndedAt = workout.EndedAt,
            Seconds = workout.Seconds,
            Calories = workout.Calories,
            DistanceM = workout.DistanceM,
            Intensity = workout.Intensity,
            Source = workout.Source,
            Counted = verdict.Counted,
            Reason = verdict.Reason,
            Note = workout.Note,
            NoteAt = workout.NoteAt,
            SyncedAt = workout.SyncedAt,
        };
    }

    public static SleepPeriodRow From(SleepPeriod period) => new()
    {
        Id = period.Id,
        Type = period.Type,
        StartedAt = period.StartedAt,
        EndedAt = period.EndedAt,
        InBedS = period.InBedS,
        AsleepS = period.AsleepS,
        DeepS = period.DeepS,
        LightS = period.LightS,
        RemS = period.RemS,
        AwakeS = period.AwakeS,
        LatencyS = period.LatencyS,
        Efficiency = period.Efficiency,
        AverageHrv = period.AverageHrv,
        AverageHr = period.AverageHr,
        LowestHr = period.LowestHr,
    };

    public static ActivityWeekRow From(ActivityWeek week) => new()
    {
        From = Day(week.From),
        To = Day(week.To),
        AverageSteps = week.AverageSteps,
        StepDays = week.StepDays,
        GoalDays = week.GoalDays,
        UnderDays = week.UnderDays,
        Sessions = week.Sessions,
        ExerciseSeconds = week.ExerciseSeconds,
        ExerciseCalories = week.ExerciseCalories,
        AverageSleep = week.AverageSleep,
        AverageReadiness = week.AverageReadiness,
    };

    public static ActivitySyncRead From(OuraPageSync sync, Earned earned)
    {
        var result = sync.Result;
        return new ActivitySyncRead
        {
            Ran = sync.Ran,
            Changed = result?.Written > 0,
            From = result is null ? null : Day(result.From),
            To = result is null ? null : Day(result.To),
            Records = result?.Records ?? 0,
            Written = result?.Written ?? 0,
            Voided = result?.Voided ?? 0,
            HistoryFrom = result?.HistoryFrom is { } history ? Day(history) : null,
            HistoryDone = result?.HistoryDone ?? false,
            LastSyncedAt = sync.LastSyncedAt,
            Awards = earned.Awards.Select(RewardRows.From).ToList(),
            Progress = earned.Progress.Select(RewardRows.From).ToList(),
        };
    }
}

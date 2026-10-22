namespace LifeOs.Domain;

/// <summary>
/// Chris's step thresholds (ACTIVITY-2, activity plan §2): a minimum of 5,000 and a goal of
/// 7,500. More is better; less is worse. Oura's API has no step goal, so LifeOS keeps its own.
/// </summary>
public static class ActivityTargets
{
    public const int MinimumSteps = 5_000;
    public const int GoalSteps = 7_500;

    /// <summary>Which band a day's steps fall in: <see cref="StepBands"/>.</summary>
    public static string Band(int steps)
        => steps >= GoalSteps ? StepBands.Goal
            : steps >= MinimumSteps ? StepBands.Minimum
            : StepBands.Under;
}

/// <summary>How a day's steps read against <see cref="ActivityTargets"/>.</summary>
public static class StepBands
{
    /// <summary>Under the 5,000 minimum.</summary>
    public const string Under = "under";

    /// <summary>The minimum met, the goal not yet.</summary>
    public const string Minimum = "minimum";

    /// <summary>The 7,500 goal met.</summary>
    public const string Goal = "goal";
}

/// <summary>Whether a workout counts as exercise, and if not, why (in words).</summary>
public sealed record ExerciseVerdict(bool Counted, string? Reason)
{
    public static readonly ExerciseVerdict Yes = new(true, null);
}

/// <summary>
/// What counts as exercise (ACTIVITY-3, activity plan §2, §3.4): only workouts Chris records
/// himself — entered in the Oura app or recorded live — and never a walk, whose steps already
/// count. Workouts Oura detected, confirmed or not, don't count. A source Oura adds later
/// doesn't count until it's classified here. Phase 2 is expected to pay on the same set.
/// </summary>
public static class ExerciseRules
{
    public const string WalkReason = "A walk: it's in your steps";
    public const string DetectedReason = "Detected by Oura";
    public const string UnknownSourceReason = "Unknown source";

    /// <summary>Workouts Chris recorded himself: entered in the app, or recorded live.</summary>
    public static readonly IReadOnlySet<string> CountedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "manual", "workout_heart_rate", "live_oura_heart_rate", "live_third_party_heart_rate"
    };

    /// <summary>Workouts Oura found on its own, whether or not Chris confirmed them.</summary>
    public static readonly IReadOnlySet<string> DetectedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "autodetected", "confirmed"
    };

    /// <summary>Activities that never count: a walk is already in the day's steps.</summary>
    public static readonly IReadOnlySet<string> ExcludedActivities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "walking"
    };

    public static ExerciseVerdict Counts(string? source, string? activity)
    {
        if (activity is not null && ExcludedActivities.Contains(activity.Trim()))
        {
            return new ExerciseVerdict(false, WalkReason);
        }

        var from = source?.Trim() ?? "";
        if (CountedSources.Contains(from))
        {
            return ExerciseVerdict.Yes;
        }

        return new ExerciseVerdict(false, DetectedSources.Contains(from) ? DetectedReason : UnknownSourceReason);
    }
}

/// <summary>
/// One of the Oura collections LifeOS keeps (D14, activity plan §3.2): where its records go in
/// an event's payload, what identifies one, and its event kind. A record LifeOS holds that Oura
/// no longer answers is voided only in a <see cref="Voidable"/> collection: daily records are
/// never withdrawn, workouts and sleep periods can be deleted in the Oura app.
/// </summary>
/// <param name="Name">Oura's collection (<c>/v2/usercollection/&lt;name&gt;</c>), also the first part of an event's <c>external_id</c>.</param>
/// <param name="PayloadKey">The payload's one key (<c>oura_activity</c>, …).</param>
/// <param name="KeyField">What identifies a record: its <c>day</c>, or Oura's <c>id</c>.</param>
public sealed record OuraCollection(string Name, string PayloadKey, string KeyField, string Kind, bool Voidable);

/// <summary>The five Oura collections LifeOS keeps (activity plan §3.2).</summary>
public static class OuraCollections
{
    public static readonly OuraCollection DailyActivity = new("daily_activity", "oura_activity", "day", EventKinds.Measurement, false);
    public static readonly OuraCollection Workout = new("workout", "oura_workout", "id", EventKinds.Activity, true);
    public static readonly OuraCollection DailySleep = new("daily_sleep", "oura_sleep_day", "day", EventKinds.Measurement, false);
    public static readonly OuraCollection DailyReadiness = new("daily_readiness", "oura_readiness", "day", EventKinds.Measurement, false);
    public static readonly OuraCollection Sleep = new("sleep", "oura_sleep", "id", EventKinds.Measurement, true);

    public static readonly IReadOnlyList<OuraCollection> All = [DailyActivity, Workout, DailySleep, DailyReadiness, Sleep];
}

/// <summary>
/// A day of Oura activity (<c>daily_activity</c>), trimmed to what LifeOS reads. Times are in
/// seconds; <see cref="Timestamp"/> is when Oura's day starts (4 a.m. local).
/// </summary>
public sealed record OuraActivityDay(
    DateOnly Day,
    string? OuraId,
    DateTimeOffset? Timestamp,
    int? Steps,
    int? ActiveCalories,
    int? TotalCalories,
    int? WalkingM,
    int? HighS,
    int? MediumS,
    int? LowS,
    int? SedentaryS,
    int? RestingS,
    int? NonWearS,
    int? Score);

/// <summary>
/// An Oura workout (<c>workout</c>). <see cref="Activity"/>, <see cref="Intensity"/> and
/// <see cref="Source"/> stay plain strings: Oura keeps adding values. Calories are active
/// calories only.
/// </summary>
public sealed record OuraWorkout(
    string Id,
    DateOnly Day,
    string Activity,
    string? Label,
    DateTimeOffset Start,
    DateTimeOffset End,
    int? Calories,
    int? DistanceM,
    string? Intensity,
    string Source);

/// <summary>A day's sleep score (<c>daily_sleep</c>) and its contributors, by Oura's names.</summary>
public sealed record OuraSleepScore(
    DateOnly Day,
    string? OuraId,
    DateTimeOffset? Timestamp,
    int? Score,
    IReadOnlyDictionary<string, int?> Contributors);

/// <summary>A day's readiness (<c>daily_readiness</c>): the score, its contributors, and the temperature deviation in °C.</summary>
public sealed record OuraReadiness(
    DateOnly Day,
    string? OuraId,
    DateTimeOffset? Timestamp,
    int? Score,
    double? TemperatureDeviation,
    double? TemperatureTrendDeviation,
    IReadOnlyDictionary<string, int?> Contributors);

/// <summary>
/// A sleep period (<c>sleep</c>): a night (<c>long_sleep</c>), a nap (<c>sleep</c>,
/// <c>late_nap</c>), or one rejected (<c>rest</c>) or deleted. Durations are in seconds. Its
/// <see cref="Day"/> is the morning it counts on.
/// </summary>
public sealed record OuraSleepPeriod(
    string Id,
    DateOnly Day,
    string Type,
    DateTimeOffset Start,
    DateTimeOffset End,
    int? InBedS,
    int? AsleepS,
    int? DeepS,
    int? LightS,
    int? RemS,
    int? AwakeS,
    int? LatencyS,
    int? Efficiency,
    int? AverageHrv,
    double? AverageHr,
    int? LowestHr);

/// <summary>Oura's sleep period types (activity plan §1).</summary>
public static class SleepTypes
{
    public const string LongSleep = "long_sleep";
    public const string Nap = "sleep";
    public const string LateNap = "late_nap";
    public const string Rest = "rest";
    public const string Deleted = "deleted";
}

/// <summary>A day of activity as LifeOS holds it (<c>bsk.v_activity_day</c>, migration 0037).</summary>
public sealed record ActivityDay(
    DateOnly Day,
    int? Steps,
    int? ActiveCalories,
    int? TotalCalories,
    int? WalkingM,
    int? HighS,
    int? MediumS,
    int? LowS,
    int? SedentaryS,
    int? RestingS,
    int? NonWearS,
    int? Score,
    DateTimeOffset SyncedAt,
    int Versions,
    Guid EventId);

/// <summary>A live workout as LifeOS holds it (<c>bsk.v_workout</c>), with Chris's note, if any.</summary>
public sealed record Workout(
    string Id,
    DateOnly Day,
    string Activity,
    string? Label,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int Seconds,
    int? Calories,
    int? DistanceM,
    string? Intensity,
    string Source,
    string? Note,
    DateTimeOffset? NoteAt,
    DateTimeOffset SyncedAt,
    Guid EventId)
{
    /// <summary>Whether it counts as exercise (<see cref="ExerciseRules"/>).</summary>
    public ExerciseVerdict Verdict => ExerciseRules.Counts(Source, Activity);
}

/// <summary>
/// A day's sleep as LifeOS holds it (<c>bsk.v_sleep_day</c>): the score, and the night — the
/// day's longest <c>long_sleep</c> — plus naps, added up separately.
/// </summary>
public sealed record SleepDay(
    DateOnly Day,
    int? Score,
    IReadOnlyDictionary<string, int?> Contributors,
    DateTimeOffset? BedAt,
    DateTimeOffset? WokeAt,
    int? InBedS,
    int? AsleepS,
    int? DeepS,
    int? LightS,
    int? RemS,
    int? AwakeS,
    int? Efficiency,
    int? AverageHrv,
    int? LowestHr,
    int Naps,
    int NapS,
    DateTimeOffset SyncedAt);

/// <summary>
/// A live sleep period as LifeOS holds it (<c>bsk.v_sleep_period</c>): a night or a nap, never
/// one Oura rejected (<c>rest</c>) or deleted. Durations are in seconds; <see cref="Day"/> is
/// the morning it counts on.
/// </summary>
public sealed record SleepPeriod(
    string Id,
    DateOnly Day,
    string Type,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int? InBedS,
    int? AsleepS,
    int? DeepS,
    int? LightS,
    int? RemS,
    int? AwakeS,
    int? LatencyS,
    int? Efficiency,
    int? AverageHrv,
    double? AverageHr,
    int? LowestHr,
    DateTimeOffset SyncedAt);

/// <summary>A day's readiness as LifeOS holds it (<c>bsk.v_readiness_day</c>).</summary>
public sealed record ReadinessDay(
    DateOnly Day,
    int? Score,
    IReadOnlyDictionary<string, int?> Contributors,
    double? TemperatureDeviation,
    double? TemperatureTrendDeviation,
    DateTimeOffset SyncedAt,
    Guid EventId);

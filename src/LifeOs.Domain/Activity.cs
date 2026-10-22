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

/// <summary>A point on one of <see cref="ActivityRules"/>' straight-line curves: <see cref="At"/> pays <see cref="Xp"/>.</summary>
public sealed record CurvePoint(int At, int Xp);

/// <summary>An exercise tier: a day with at least <see cref="FromMinutes"/> of counted exercise pays <see cref="Xp"/>.</summary>
public sealed record ExerciseTier(int FromMinutes, int Xp);

/// <summary>
/// What the activity score reads of one Oura day (activity plan §3.8): its steps, its counted
/// exercise in seconds (<see cref="ActivityRules.ExerciseSeconds"/>), and its sleep score.
/// A null is data Oura doesn't have, which is never read as 0.
/// </summary>
public sealed record ActivityDayFacts(int? Steps, long ExerciseSeconds, int? SleepScore);

/// <summary>
/// A day's activity XP before the multiplier (activity plan §3.8): each part rounded to whole
/// base Health XP, halves up, and each paid as its own award. <see cref="Steps"/> and
/// <see cref="Sleep"/> are null when Oura has no data for them.
/// </summary>
public sealed record ActivityScore(long? Steps, long Exercise, long? Sleep)
{
    /// <summary>The day's activity XP before the multiplier: all three parts.</summary>
    public long Total => (Steps ?? 0) + Exercise + (Sleep ?? 0);
}

/// <summary>
/// The activity XP rules (ACTIVITY-5, activity plan §3.8): up to <see cref="Cap"/> base Health
/// XP a day from steps, counted exercise and the sleep score. Readiness and Oura's Activity
/// Score pay nothing. Tunable numbers kept in code like <see cref="DietRules"/>, with a drift
/// test and a TypeScript port (<c>lib/activity-score.ts</c>) tested on the same table. A retune
/// never rewrites a past day: only a change to the day's Oura data re-scores it.
/// </summary>
/// <param name="StepsCurve">Steps to XP, a straight line between the points; past the last point it pays the last.</param>
/// <param name="ExerciseTiers">The day's counted minutes to XP, lowest first; under the first tier pays 0.</param>
/// <param name="SleepCurve">The sleep score to XP, a straight line between the points; below the first it pays the first, past the last the last.</param>
/// <param name="XpFrom">The first day activity pays (question 7): earlier days are shown but never pay.</param>
public sealed record ActivityRules(
    string Currency,
    IReadOnlyList<CurvePoint> StepsCurve,
    IReadOnlyList<ExerciseTier> ExerciseTiers,
    IReadOnlyList<CurvePoint> SleepCurve,
    DateOnly XpFrom)
{
    /// <summary>Chris's planning notes (2026-10-22): 25 steps + 35 exercise + 20 sleep = 80, from 2026-10-22.</summary>
    public static readonly ActivityRules Default = new(
        Currency: Currencies.HealthXp,
        StepsCurve: [new(0, 0), new(2_500, 5), new(5_000, 12), new(7_500, 20), new(10_000, 25)],
        ExerciseTiers: [new(10, 8), new(20, 15), new(30, 25), new(45, 35)],
        SleepCurve: [new(50, 0), new(60, 5), new(70, 10), new(80, 15), new(85, 20)],
        XpFrom: new DateOnly(2026, 10, 22));

    /// <summary>The most a day's steps can pay.</summary>
    public int StepsMax => StepsCurve[^1].Xp;

    /// <summary>The most a day's exercise can pay.</summary>
    public int ExerciseMax => ExerciseTiers[^1].Xp;

    /// <summary>The most a day's sleep score can pay.</summary>
    public int SleepMax => SleepCurve[^1].Xp;

    /// <summary>The most base Health XP a day's activity can pay.</summary>
    public int Cap => StepsMax + ExerciseMax + SleepMax;

    /// <summary>Whether <paramref name="day"/> can pay at all: on or after <see cref="XpFrom"/>.</summary>
    public bool Pays(DateOnly day) => day >= XpFrom;

    /// <summary>Scores one Oura day (activity plan §3.8).</summary>
    public ActivityScore Score(ActivityDayFacts facts)
        => new(
            facts.Steps is { } steps ? StepsXp(steps) : null,
            ExerciseXp(facts.ExerciseSeconds),
            facts.SleepScore is { } score ? SleepXp(score) : null);

    /// <summary>The steps part, unrounded: along <see cref="StepsCurve"/>.</summary>
    public decimal StepsPart(int steps) => Along(StepsCurve, steps);

    /// <summary>What <paramref name="steps"/> pay: the steps part, halves up.</summary>
    public long StepsXp(int steps) => DietRules.Round(StepsPart(steps));

    /// <summary>The sleep part, unrounded: along <see cref="SleepCurve"/>.</summary>
    public decimal SleepPart(int score) => Along(SleepCurve, score);

    /// <summary>What a sleep score pays: the sleep part, halves up.</summary>
    public long SleepXp(int score) => DietRules.Round(SleepPart(score));

    /// <summary>
    /// What a day's counted exercise pays: the highest tier its <paramref name="seconds"/>
    /// reach, counted to the second (10 minutes is 600 seconds), else 0.
    /// </summary>
    public long ExerciseXp(long seconds)
        => ExerciseTiers.LastOrDefault(t => seconds >= t.FromMinutes * 60L)?.Xp ?? 0;

    /// <summary>
    /// One Oura day's counted exercise in seconds: the workouts that count
    /// (<see cref="ExerciseRules"/>), with any times they overlap merged, so a workout split in
    /// two pays no more and two overlapping records count once. Only the duration counts.
    /// </summary>
    public static long ExerciseSeconds(IEnumerable<Workout> workouts)
        => MergedSeconds(workouts.Where(w => w.Verdict.Counted).Select(w => (w.StartedAt, w.EndedAt)));

    /// <summary>The seconds covered by <paramref name="spans"/>, overlaps counted once; a span that ends before it starts covers nothing.</summary>
    public static long MergedSeconds(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> spans)
    {
        var total = TimeSpan.Zero;
        DateTimeOffset? from = null, to = null;
        foreach (var (start, end) in spans.Where(s => s.End > s.Start).OrderBy(s => s.Start))
        {
            if (to is { } reach && start <= reach)
            {
                if (end > reach)
                {
                    to = end;
                }

                continue;
            }

            if (from is { } a && to is { } b)
            {
                total += b - a;
            }

            (from, to) = (start, end);
        }

        if (from is { } first && to is { } last)
        {
            total += last - first;
        }

        return (long)total.TotalSeconds;
    }

    /// <summary>A straight line through <paramref name="curve"/>'s points, flat before the first and after the last.</summary>
    private static decimal Along(IReadOnlyList<CurvePoint> curve, int x)
    {
        if (x <= curve[0].At)
        {
            return curve[0].Xp;
        }

        for (var i = 1; i < curve.Count; i++)
        {
            var (low, high) = (curve[i - 1], curve[i]);
            if (x <= high.At)
            {
                return low.Xp + (decimal)(high.Xp - low.Xp) * (x - low.At) / (high.At - low.At);
            }
        }

        return curve[^1].Xp;
    }
}

/// <summary>
/// The three activity awards a day can pay (activity plan §3.8), keyed by the Oura day they're
/// credited to. None belongs to an item, like the Food Log's (<see cref="FoodAwards"/>):
/// <list type="bullet">
/// <item><c>steps:&lt;yyyy-MM-dd&gt;</c> — the day's steps, once Oura's day is over</item>
/// <item><c>exercise:&lt;yyyy-MM-dd&gt;</c> — the day's counted exercise, once Oura's day is over</item>
/// <item><c>sleep:&lt;yyyy-MM-dd&gt;</c> — the night ending that morning, once it has a sleep score</item>
/// </list>
/// Like the food awards, a change to the Oura data a key was priced from re-prices it.
/// </summary>
public static class ActivityAwards
{
    public const string StepsTrigger = "steps";
    public const string ExerciseTrigger = "exercise";
    public const string SleepTrigger = "sleep";

    /// <summary>All three triggers.</summary>
    public static readonly IReadOnlyList<string> Triggers = [StepsTrigger, ExerciseTrigger, SleepTrigger];

    /// <summary>The hour Oura's activity day starts, local time: a day is over at this hour the next morning.</summary>
    public const int DayStartHour = 4;

    public static string StepsKey(DateOnly day) => Key(StepsTrigger, day);

    public static string ExerciseKey(DateOnly day) => Key(ExerciseTrigger, day);

    public static string SleepKey(DateOnly day) => Key(SleepTrigger, day);

    /// <summary>The key for <paramref name="trigger"/> on <paramref name="day"/>.</summary>
    public static string Key(string trigger, DateOnly day) => $"{trigger}:{day:yyyy-MM-dd}";

    /// <summary>The day an activity award key is for, or null when it isn't one.</summary>
    public static DateOnly? DayOf(string awardKey)
    {
        var separator = awardKey.IndexOf(':');
        return separator > 0
               && Triggers.Contains(awardKey[..separator])
               && DateOnly.TryParseExact(
                   awardKey[(separator + 1)..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out var day)
            ? day
            : null;
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
/// day's longest <c>long_sleep</c> — plus naps, added up separately. <see cref="ScoreEventId"/>
/// is the version of the score it read, which sleep XP cites (activity plan §3.8); null with no
/// score.
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
    DateTimeOffset SyncedAt,
    Guid? ScoreEventId = null);

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

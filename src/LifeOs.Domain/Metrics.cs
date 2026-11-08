using System.Globalization;

namespace LifeOs.Domain;

/// <summary>
/// Something Chris measures about himself (MEASURE-1, D20, measurements plan §4.1): a
/// <c>Metric</c> subject, read through <c>bsk.v_metric</c>. <paramref name="Decimals"/> (0–3) is
/// how many decimal places a reading takes; <paramref name="Target"/> is his goal, and
/// <paramref name="Better"/> (<see cref="MetricBetter"/>) which way is good. <paramref name="Catalog"/>
/// is the starter-list key it was made from (<see cref="MetricCatalog"/>), kept through a rename.
/// <paramref name="Low"/> and <paramref name="High"/> are its normal range (MEASURE-3, plan §4.8),
/// either or both, the bounds themselves normal. <paramref name="Panel"/> is the lab panel a test
/// is on (<see cref="LabPanels"/>, MEASURE-4, plan §4.9), null for a metric that isn't a lab test.
/// </summary>
public sealed record Metric(
    Guid Id,
    string Urn,
    string Name,
    string Unit,
    int Decimals,
    decimal? Target,
    string? Better,
    string? Catalog,
    DateTimeOffset? ArchivedAt,
    DateOnly? ArchivedOn,
    DateTimeOffset CreatedAt,
    decimal? Low = null,
    decimal? High = null,
    string? Panel = null)
{
    /// <summary>Whether the metric is archived now.</summary>
    public bool Archived => ArchivedAt is not null;

    /// <summary>Whether it's a lab test (MEASURE-4), listed under Lab results.</summary>
    public bool IsLabTest => Panel is not null;

    /// <summary>
    /// <see cref="MetricFlags.Low"/> below the normal range, <see cref="MetricFlags.High"/> above
    /// it, null inside it or with no range (MEASURE-3, plan §4.8).
    /// </summary>
    public string? Flag(decimal value) => MetricFlags.Of(value, Low, High);

    /// <summary>
    /// A reading's flag (plan §4.9): against the range printed beside it on its report when it
    /// has one, for good; otherwise against this metric's range as it is now.
    /// </summary>
    public string? Flag(MetricReading reading)
        => reading.HasOwnRange ? MetricFlags.Of(reading.Value, reading.Low, reading.High) : Flag(reading.Value);

    /// <summary>The normal range in words: "90–119 mmHg", "up to 40.0 in", "at least 35.0", or null with none.</summary>
    public string? RangeText() => MetricLimits.RangeText(Low, High, Decimals, Unit);

    /// <summary>A value as this metric shows it: "192.4 lb", "6 %".</summary>
    public string Format(decimal value)
        => MetricLimits.Format(value, Decimals) + (Unit.Length == 0 ? "" : " " + Unit);
}

/// <summary>
/// One reading of a metric (MEASURE-1, measurements plan §4.2): the newest version of a
/// <c>metric_reading</c> event, read through <c>bsk.v_metric_reading</c>. <paramref name="TakenAt"/>
/// is when it was taken; <paramref name="TakenOn"/> and <paramref name="TakenLocal"/> read it in
/// the home zone. <paramref name="Unit"/> is the metric's unit when the reading was written.
/// A lab result (MEASURE-4, plan §4.9) has its <paramref name="VisitId"/> and the range printed
/// beside it on the report, <paramref name="Low"/> and <paramref name="High"/> (either or both).
/// </summary>
public sealed record MetricReading(
    Guid Id,
    Guid MetricId,
    decimal Value,
    string Unit,
    string? Note,
    DateTimeOffset TakenAt,
    DateOnly TakenOn,
    DateTime TakenLocal,
    DateTimeOffset FirstLoggedAt,
    DateTimeOffset LoggedAt,
    int Versions,
    Guid EventId,
    Guid? VisitId = null,
    decimal? Low = null,
    decimal? High = null)
{
    /// <summary>Whether the reading carries a range of its own, as a lab result does.</summary>
    public bool HasOwnRange => Low is not null || High is not null;
}

/// <summary>Which way is good for a metric (measurements plan §2.1).</summary>
public static class MetricBetter
{
    public const string Lower = "lower";
    public const string Higher = "higher";

    public static readonly IReadOnlyList<string> All = [Lower, Higher];
}

/// <summary>Where a reading sits against its metric's normal range (MEASURE-3, plan §4.8).</summary>
public static class MetricFlags
{
    public const string Low = "low";
    public const string High = "high";

    /// <summary>Low below <paramref name="low"/>, high above <paramref name="high"/>, else null: the bounds are normal.</summary>
    public static string? Of(decimal value, decimal? low, decimal? high)
        => low is { } l && value < l ? Low
            : high is { } h && value > h ? High
            : null;
}

/// <summary>
/// A metric on the starter list (measurements plan §2.1): what making one by that name fills in.
/// <paramref name="Panel"/> is the lab panel a test is on (<see cref="LabPanels"/>), null for a body metric.
/// </summary>
public sealed record MetricTemplate(
    string Key, string Name, string Unit, int Decimals, string? Better, decimal? Low = null, decimal? High = null,
    string? Panel = null);

/// <summary>
/// The starter list (measurements plan §2.1, §4.8, §4.9). M1 has Weight and M3 the body metrics;
/// M4 adds the lab panels, in the order reports print them. Blood pressure is two metrics logged
/// together, paired by their keys. Of the body metrics only blood pressure comes with a normal
/// range (the American Heart Association's "normal"): the others depend on age and sex, so Chris
/// sets them. The lab tests come with MedlinePlus's common adult ranges (checked 2026-11-08),
/// HDL's left out since its low end depends on sex; each visit's own report then takes over.
/// </summary>
public static class MetricCatalog
{
    public static readonly MetricTemplate Weight = new("weight", "Weight", "lb", 1, MetricBetter.Lower);
    public static readonly MetricTemplate BodyFat = new("body_fat", "Body fat", "%", 1, MetricBetter.Lower);
    public static readonly MetricTemplate Waist = new("waist", "Waist", "in", 1, MetricBetter.Lower);
    public static readonly MetricTemplate Systolic = new("bp_systolic", "Systolic BP", "mmHg", 0, MetricBetter.Lower, 90m, 119m);
    public static readonly MetricTemplate Diastolic = new("bp_diastolic", "Diastolic BP", "mmHg", 0, MetricBetter.Lower, 60m, 79m);
    public static readonly MetricTemplate Vo2MaxLab = new("vo2max_lab", "VO2 max (lab)", "mL/kg/min", 1, MetricBetter.Higher);

    /// <summary>The comprehensive metabolic panel's 14 tests and eGFR, which reports print with it.</summary>
    public static readonly IReadOnlyList<MetricTemplate> Cmp =
    [
        Lab("glucose", "Glucose", "mg/dL", 0, null, 70m, 100m, LabPanels.Cmp),
        Lab("bun", "BUN", "mg/dL", 0, null, 6m, 20m, LabPanels.Cmp),
        Lab("creatinine", "Creatinine", "mg/dL", 2, null, 0.6m, 1.3m, LabPanels.Cmp),
        Lab("egfr", "eGFR", "mL/min/1.73m2", 0, MetricBetter.Higher, 60m, null, LabPanels.Cmp),
        Lab("sodium", "Sodium", "mmol/L", 0, null, 135m, 145m, LabPanels.Cmp),
        Lab("potassium", "Potassium", "mmol/L", 1, null, 3.7m, 5.2m, LabPanels.Cmp),
        Lab("chloride", "Chloride", "mmol/L", 0, null, 96m, 106m, LabPanels.Cmp),
        Lab("co2", "CO2", "mmol/L", 0, null, 23m, 29m, LabPanels.Cmp),
        Lab("calcium", "Calcium", "mg/dL", 1, null, 8.5m, 10.2m, LabPanels.Cmp),
        Lab("total_protein", "Total protein", "g/dL", 1, null, 6.0m, 8.3m, LabPanels.Cmp),
        Lab("albumin", "Albumin", "g/dL", 1, null, 3.4m, 5.4m, LabPanels.Cmp),
        Lab("bilirubin", "Total bilirubin", "mg/dL", 1, null, 0.1m, 1.2m, LabPanels.Cmp),
        Lab("alp", "Alkaline phosphatase", "U/L", 0, null, 20m, 130m, LabPanels.Cmp),
        Lab("ast", "AST", "U/L", 0, null, 8m, 33m, LabPanels.Cmp),
        Lab("alt", "ALT", "U/L", 0, null, 4m, 36m, LabPanels.Cmp)
    ];

    /// <summary>The lipid panel. "Below 200" is "up to 199", since the bounds are normal.</summary>
    public static readonly IReadOnlyList<MetricTemplate> Lipids =
    [
        Lab("cholesterol", "Total cholesterol", "mg/dL", 0, MetricBetter.Lower, null, 199m, LabPanels.Lipids),
        Lab("hdl", "HDL", "mg/dL", 0, MetricBetter.Higher, null, null, LabPanels.Lipids),
        Lab("triglycerides", "Triglycerides", "mg/dL", 0, MetricBetter.Lower, null, 149m, LabPanels.Lipids),
        Lab("ldl", "LDL", "mg/dL", 0, MetricBetter.Lower, null, 99m, LabPanels.Lipids),
        Lab("non_hdl", "Non-HDL cholesterol", "mg/dL", 0, MetricBetter.Lower, null, 129m, LabPanels.Lipids)
    ];

    /// <summary>A1c, below 5.7% normal.</summary>
    public static readonly MetricTemplate A1c = Lab("a1c", "A1c", "%", 1, MetricBetter.Lower, null, 5.6m, LabPanels.A1c);

    /// <summary>Every lab test on the list, in panel order.</summary>
    public static readonly IReadOnlyList<MetricTemplate> LabTests = [.. Cmp, .. Lipids, A1c];

    public static readonly IReadOnlyList<MetricTemplate> All = [Weight, BodyFat, Waist, Systolic, Diastolic, Vo2MaxLab, .. LabTests];

    /// <summary>The tests on <paramref name="panel"/>, in the order its report prints them (none for <c>other</c>).</summary>
    public static IReadOnlyList<MetricTemplate> TestsOn(string panel)
        => LabTests.Where(t => t.Panel == panel).ToList();

    /// <summary>Where a starter-list key sits on the list, for ordering a report; past the end when it's on none.</summary>
    public static int IndexOf(string? key)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Key == key)
            {
                return i;
            }
        }

        return All.Count;
    }

    /// <summary>The template a metric named <paramref name="name"/> is made from, in any case; null when none is.</summary>
    public static MetricTemplate? Find(string? name)
        => All.FirstOrDefault(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The template with key <paramref name="key"/>, or null.</summary>
    public static MetricTemplate? ByKey(string? key)
        => All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    private static MetricTemplate Lab(
        string key, string name, string unit, int decimals, string? better, decimal? low, decimal? high, string panel)
        => new(key, name, unit, decimals, better, low, high, panel);
}

/// <summary>
/// The limits on metrics and readings (measurements plan §4.1–§4.2), shared by the services, the
/// CLI and the API so they refuse the same things in the same words.
/// </summary>
public static class MetricLimits
{
    /// <summary>The payload key a reading's events carry.</summary>
    public const string ReadingKey = "metric_reading";

    public const string UnitKey = "unit";
    public const string DecimalsKey = "decimals";
    public const string TargetKey = "target";
    public const string BetterKey = "better";
    public const string CatalogKey = "catalog";
    public const string LowKey = "low";
    public const string HighKey = "high";
    public const string PanelKey = "panel";

    /// <summary>The attributes only <c>MetricService</c> writes, so <c>bsk set</c> refuses them.</summary>
    public static readonly IReadOnlyList<string> Keys = [UnitKey, DecimalsKey, TargetKey, BetterKey, CatalogKey, LowKey, HighKey, PanelKey];

    public const int MaxName = 60;
    public const int MaxUnit = 16;
    public const int MaxDecimals = 3;
    public const int DefaultDecimals = 1;
    public const int MaxNote = 140;

    /// <summary>The largest value a reading or target can have, either way.</summary>
    public const decimal MaxValue = 1_000_000m;

    /// <summary>How far ahead of now a reading's time may be, for clock skew.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(1);

    // Unicode's own line breaks, which aren't control characters.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>A metric's name as kept: trimmed, one line, 1–60 characters.</summary>
    public static string CheckName(string? name)
    {
        var text = name?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw new ArgumentException("Give the metric a name, e.g. Weight.");
        }

        if (text.Any(IsBreak))
        {
            throw new ArgumentException("A metric's name is one line: take out the line break.");
        }

        return text.Length <= MaxName
            ? text
            : throw new ArgumentException($"A metric's name is up to {MaxName} characters; that one is {text.Length}.");
    }

    /// <summary>A unit as kept: trimmed, one line, 1–16 characters ("lb", "%", "mg/dL").</summary>
    public static string CheckUnit(string? unit)
    {
        var text = unit?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw new ArgumentException("Give the metric a unit, e.g. lb, in, % or mg/dL.");
        }

        if (text.Any(IsBreak))
        {
            throw new ArgumentException("A unit is one line: take out the line break.");
        }

        return text.Length <= MaxUnit
            ? text
            : throw new ArgumentException($"A unit is up to {MaxUnit} characters; that one is {text.Length}.");
    }

    /// <summary>Decimal places as kept, or refused in words outside 0–3.</summary>
    public static int CheckDecimals(int decimals)
        => decimals is >= 0 and <= MaxDecimals
            ? decimals
            : throw new ArgumentException($"A metric takes 0 to {MaxDecimals} decimal places; got {decimals}.");

    /// <summary>Which way is better as kept (lower or higher, in any case), or refused in words.</summary>
    public static string CheckBetter(string better)
    {
        var text = better.Trim().ToLowerInvariant();
        return MetricBetter.All.Contains(text)
            ? text
            : throw new ArgumentException($"Which way is better is 'lower' or 'higher'; got '{better}'.");
    }

    /// <summary>
    /// A value as kept: within ±1,000,000 and with at most <paramref name="decimals"/> decimal
    /// places. <paramref name="what"/> names it in the refusal ("Weight", "The target").
    /// </summary>
    public static decimal CheckValue(decimal value, int decimals, string what)
    {
        if (value < -MaxValue || value > MaxValue)
        {
            throw new ArgumentException($"{what} is between -1,000,000 and 1,000,000; got {Text(value)}.");
        }

        if (decimal.Round(value, decimals) != value)
        {
            var places = decimals switch
            {
                0 => "a whole number",
                1 => "at most 1 decimal place",
                _ => $"at most {decimals} decimal places"
            };
            throw new ArgumentException($"{what} takes {places}; got {Text(value)}.");
        }

        return value;
    }

    /// <summary>
    /// A normal range as kept: each end with at most <paramref name="decimals"/> places, and the
    /// low end at or below the high end. Either can be null.
    /// </summary>
    public static (decimal? Low, decimal? High) CheckRange(decimal? low, decimal? high, int decimals)
    {
        var keptLow = low is { } l ? CheckValue(l, decimals, "The normal range's low end") : (decimal?)null;
        var keptHigh = high is { } h ? CheckValue(h, decimals, "The normal range's high end") : (decimal?)null;
        if (keptLow is { } a && keptHigh is { } b && a > b)
        {
            throw new ArgumentException(
                $"The normal range's low end ({Format(a, decimals)}) is above its high end ({Format(b, decimals)}).");
        }

        return (keptLow, keptHigh);
    }

    /// <summary>
    /// A note as kept: null when blank, else trimmed, one line, at most 140 characters.
    /// <paramref name="what"/> names it in the refusal ("A reading's note", "A visit's note").
    /// </summary>
    public static string? CheckNote(string? note, string what = "A reading's note")
        => CheckLine(note, MaxNote, what);

    /// <summary>A short optional line as kept: null when blank, else trimmed, one line, at most <paramref name="max"/> characters.</summary>
    public static string? CheckLine(string? line, int max, string what)
    {
        var text = line?.Trim() ?? "";
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Any(IsBreak))
        {
            throw new ArgumentException($"{what} is one line: take out the line break.");
        }

        return text.Length <= max
            ? text
            : throw new ArgumentException($"{what} is up to {max} characters; that one is {text.Length}.");
    }

    /// <summary>A range in words: "90–119 mmHg", "up to 40.0 in", "at least 35.0", or null with none.</summary>
    public static string? RangeText(decimal? low, decimal? high, int decimals, string unit)
    {
        var suffix = unit.Length == 0 ? "" : " " + unit;
        return (low, high) switch
        {
            ({ } l, { } h) => $"{Format(l, decimals)}–{Format(h, decimals)}{suffix}",
            (null, { } h) => $"up to {Format(h, decimals)}{suffix}",
            ({ } l, null) => $"at least {Format(l, decimals)}{suffix}",
            _ => null
        };
    }

    /// <summary>A value with exactly <paramref name="decimals"/> places, invariant: "192.4", "6".</summary>
    public static string Format(decimal value, int decimals)
        => decimal.Round(value, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool IsBreak(char ch) => char.IsControl(ch) || ch is LineSeparator or ParagraphSeparator;
}

/// <summary>One day's value of a metric: the first reading of that home-zone day (measurements plan §2.2).</summary>
public sealed record MetricDay(DateOnly Day, decimal Value, Guid ReadingId);

/// <summary>A day's value with the trend on that day.</summary>
public sealed record MetricTrendPoint(DateOnly Day, decimal Value, decimal Trend, Guid ReadingId);

/// <summary>How far a metric is from its target (measurements plan §2.3).</summary>
public static class GoalStatuses
{
    /// <summary>No target set.</summary>
    public const string NoTarget = "no_target";

    /// <summary>Fewer than 2 days with a value.</summary>
    public const string TooFew = "too_few";

    /// <summary>The trend is at or past the target, in the direction that's better.</summary>
    public const string Reached = "reached";

    /// <summary>The rate is moving toward the target, which is at most two years away.</summary>
    public const string Heading = "heading";

    /// <summary>The rate is moving toward the target, but it's more than two years away.</summary>
    public const string Far = "far";

    /// <summary>The rate is flat or moving away from the target.</summary>
    public const string NotHeading = "not_heading";
}

/// <summary>
/// Where a metric stands against its target (measurements plan §2.3). <paramref name="Now"/> is
/// the trend on the newest day; <paramref name="RatePerWeek"/> the least-squares rate over the
/// basis, a week at a time; <paramref name="ReachedBy"/> the day the rate reaches the target
/// (only when heading). The basis is the days the rate was measured over.
/// </summary>
public sealed record MetricGoal(
    string Status,
    decimal? Target,
    string? Direction,
    decimal? Now,
    decimal? RatePerWeek,
    DateOnly? ReachedBy,
    int BasisDays,
    DateOnly? BasisFrom,
    DateOnly? BasisTo);

/// <summary>
/// A metric's day values, trend and goal date (MEASURE-2, measurements plan §2.2–§2.3): pure
/// functions, worked out on every read and never stored. The web's mock store has a TypeScript
/// port with the same cases.
/// </summary>
public static class MetricTrend
{
    /// <summary>How far each day's value pulls the trend after a one-day step (§2.2).</summary>
    public const double Alpha = 0.2;

    /// <summary>The days the goal date's rate is measured over, the newest included (§2.3).</summary>
    public const int BasisDays = 28;

    /// <summary>The furthest a goal date is given, in days; past it the goal reads as far.</summary>
    public const int HorizonDays = 730;

    /// <summary>
    /// Each home-zone day's value: the reading with the earliest time that day (then the lowest
    /// id), oldest day first.
    /// </summary>
    public static IReadOnlyList<MetricDay> DayValues(IEnumerable<MetricReading> readings)
        => readings
            .GroupBy(r => r.TakenOn)
            .Select(g => g.OrderBy(r => r.TakenAt).ThenBy(r => r.Id).First())
            .OrderBy(r => r.TakenOn)
            .Select(r => new MetricDay(r.TakenOn, r.Value, r.Id))
            .ToList();

    /// <summary>
    /// The trend on each day with a value (§2.2): the first day's value, then each value pulling
    /// it <c>1 − (1 − α)^g</c> of the way, g being the days since the previous value. Rounded to
    /// <paramref name="decimals"/> + 1 places; worked out unrounded.
    /// </summary>
    public static IReadOnlyList<MetricTrendPoint> Trend(IReadOnlyList<MetricDay> days, int decimals)
    {
        var points = new List<MetricTrendPoint>(days.Count);
        double? trend = null;
        DateOnly previous = default;
        foreach (var day in days.OrderBy(d => d.Day))
        {
            var value = (double)day.Value;
            if (trend is not { } t)
            {
                trend = value;
            }
            else
            {
                var gap = day.Day.DayNumber - previous.DayNumber;
                var pull = 1 - Math.Pow(1 - Alpha, gap);
                trend = t + pull * (value - t);
            }

            previous = day.Day;
            points.Add(new MetricTrendPoint(day.Day, day.Value, Round(trend.Value, decimals + 1), day.ReadingId));
        }

        return points;
    }

    /// <summary>
    /// Where the metric stands against <paramref name="target"/> (§2.3). <paramref name="better"/>
    /// is the goal's direction; when null, lower while the trend is above the target and higher
    /// while it's below.
    /// </summary>
    public static MetricGoal Goal(IReadOnlyList<MetricDay> days, decimal? target, string? better, int decimals)
    {
        if (target is not { } goal)
        {
            return new MetricGoal(GoalStatuses.NoTarget, null, better, null, null, null, 0, null, null);
        }

        var ordered = days.OrderBy(d => d.Day).ToList();
        if (ordered.Count < 2)
        {
            var only = ordered.Count == 1 ? ordered[0] : null;
            return new MetricGoal(
                GoalStatuses.TooFew, goal, better, only?.Value, null, null, ordered.Count, only?.Day, only?.Day);
        }

        var newest = ordered[^1].Day;
        var basis = ordered.Where(d => d.Day.DayNumber > newest.DayNumber - BasisDays).ToList();
        if (basis.Count < 2)
        {
            basis = ordered.Skip(ordered.Count - 2).ToList();
        }

        var now = (double)Trend(ordered, decimals + 3)[^1].Trend;
        var slope = Slope(basis);
        var direction = better ?? ((double)goal < now ? MetricBetter.Lower : MetricBetter.Higher);
        var rate = Round(slope * 7, decimals + 1);
        var nowRounded = Round(now, decimals + 1);

        var toGo = direction == MetricBetter.Lower ? now - (double)goal : (double)goal - now;
        var toward = direction == MetricBetter.Lower ? -slope : slope;

        string status;
        DateOnly? reachedBy = null;
        if (toGo <= 0)
        {
            status = GoalStatuses.Reached;
        }
        else if (toward <= 0 || rate == 0m)
        {
            status = GoalStatuses.NotHeading;
        }
        else
        {
            var daysAway = Math.Ceiling(toGo / toward);
            if (daysAway > HorizonDays)
            {
                status = GoalStatuses.Far;
            }
            else
            {
                status = GoalStatuses.Heading;
                reachedBy = newest.AddDays((int)daysAway);
            }
        }

        return new MetricGoal(status, goal, direction, nowRounded, rate, reachedBy, basis.Count, basis[0].Day, basis[^1].Day);
    }

    /// <summary>The least-squares slope of value against day number, in units a day.</summary>
    private static double Slope(IReadOnlyList<MetricDay> points)
    {
        var n = points.Count;
        var origin = points[0].Day.DayNumber;
        double sumX = 0, sumY = 0;
        foreach (var p in points)
        {
            sumX += p.Day.DayNumber - origin;
            sumY += (double)p.Value;
        }

        var meanX = sumX / n;
        var meanY = sumY / n;
        double sxy = 0, sxx = 0;
        foreach (var p in points)
        {
            var dx = p.Day.DayNumber - origin - meanX;
            sxy += dx * ((double)p.Value - meanY);
            sxx += dx * dx;
        }

        return sxx == 0 ? 0 : sxy / sxx;
    }

    private static decimal Round(double value, int places)
        => decimal.Round((decimal)value, Math.Min(places, 28), MidpointRounding.AwayFromZero);
}

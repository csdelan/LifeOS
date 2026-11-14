using System.Globalization;
using LifeOs.Application.Measurements;
using LifeOs.Domain;

namespace LifeOs.Contracts;

/// <summary>
/// One metric (MEASURE-1, MEASURE-3): its name, unit, decimals (0–3), goal and which way is better
/// (<c>lower</c> or <c>higher</c>), the starter-list key it was made from (<c>weight</c>), when it
/// was archived, its normal range (<see cref="Low"/> and <see cref="High"/>, either or both), how
/// many readings it has and its newest one.
/// </summary>
public sealed class MetricRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public int Decimals { get; set; }
    public decimal? Target { get; set; }
    public string? Better { get; set; }
    public string? Catalog { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    /// <summary>The home-zone day it was archived (<c>yyyy-MM-dd</c>).</summary>
    public string? ArchivedOn { get; set; }
    /// <summary>The normal range's low end; a reading below it is flagged <c>low</c>.</summary>
    public decimal? Low { get; set; }
    /// <summary>The normal range's high end; a reading above it is flagged <c>high</c>.</summary>
    public decimal? High { get; set; }
    /// <summary>The lab panel a test is on (<c>cmp</c>, <c>lipids</c>, <c>a1c</c>, <c>other</c>); null for a metric that isn't a lab test.</summary>
    public string? Panel { get; set; }
    public int Readings { get; set; }
    public MetricReadingRow? LastReading { get; set; }
}

/// <summary>
/// One reading (MEASURE-1): its value in <see cref="Unit"/>, an optional note, when it was taken
/// (<see cref="TakenAt"/>, and in the home zone <see cref="TakenOn"/> <c>yyyy-MM-dd</c> and
/// <see cref="TakenLocal"/> <c>yyyy-MM-ddTHH:mm:ss</c>), when it was last written, how many
/// versions it has, and <see cref="Flag"/>: <c>low</c> or <c>high</c> outside its normal range, null
/// inside it or with no range. A lab result (MEASURE-4) has its <see cref="VisitId"/> and the range
/// printed beside it on the report (<see cref="Low"/>, <see cref="High"/>), which its flag follows
/// for good; any other reading's flag follows its metric's range as it is now.
/// </summary>
public sealed class MetricReadingRow
{
    public Guid Id { get; set; }
    public Guid MetricId { get; set; }
    public decimal Value { get; set; }
    public string Unit { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset TakenAt { get; set; }
    public string TakenOn { get; set; } = "";
    public string TakenLocal { get; set; } = "";
    public DateTimeOffset LoggedAt { get; set; }
    public int Versions { get; set; }
    public string? Flag { get; set; }
    /// <summary>The lab visit a result belongs to, null for any other reading.</summary>
    public Guid? VisitId { get; set; }
    /// <summary>The low end of the range printed beside a lab result.</summary>
    public decimal? Low { get; set; }
    /// <summary>The high end of the range printed beside a lab result.</summary>
    public decimal? High { get; set; }
}

/// <summary>One day's value (the first reading that day) and the trend on that day (measurements plan §2.2).</summary>
public sealed class MetricDayRow
{
    public string Day { get; set; } = "";
    public decimal Value { get; set; }
    public decimal Trend { get; set; }
    public Guid ReadingId { get; set; }
}

/// <summary>The days a goal's rate was measured over.</summary>
public sealed class MetricGoalBasis
{
    public int Days { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}

/// <summary>
/// Where a metric stands against its target (measurements plan §2.3). <see cref="Status"/> is
/// <c>no_target</c>, <c>too_few</c>, <c>reached</c>, <c>heading</c>, <c>far</c> or
/// <c>not_heading</c>; <see cref="Now"/> is the trend on the newest day,
/// <see cref="RatePerWeek"/> the rate over the basis, and <see cref="ReachedBy"/> the day the rate
/// reaches the target (<c>heading</c> only).
/// </summary>
public sealed class MetricGoalRow
{
    public string Status { get; set; } = "";
    public decimal? Target { get; set; }
    public string? Direction { get; set; }
    public decimal? Now { get; set; }
    public decimal? RatePerWeek { get; set; }
    public string? ReachedBy { get; set; }
    public MetricGoalBasis Basis { get; set; } = new();
}

/// <summary>
/// <c>GET /api/measurements/metrics/{id}</c>: a metric's readings from <see cref="From"/> (all of
/// them when null) to <see cref="Today"/>, newest first; its day values with the trend, oldest
/// first; and its goal, which always reads the newest 28 days.
/// </summary>
public sealed class MetricDetailRead
{
    public string Today { get; set; } = "";
    public string? From { get; set; }
    public MetricRow Metric { get; set; } = new();
    public IReadOnlyList<MetricReadingRow> Readings { get; set; } = [];
    public IReadOnlyList<MetricDayRow> Days { get; set; } = [];
    public MetricGoalRow Goal { get; set; } = new();
}

/// <summary>A reading write's answer: the reading as it now reads, or null after a delete.</summary>
public sealed class MetricReadingResult
{
    public MetricReadingRow? Reading { get; set; }
}

/// <summary>Readings logged together: each as it now reads, in the order given.</summary>
public sealed class MetricReadingsResult
{
    public IReadOnlyList<MetricReadingRow> Readings { get; set; } = [];
}

/// <summary>
/// <c>POST /api/measurements/metrics</c> and <c>…/{id}</c>: a name, a unit, decimals (0–3), a
/// target (or <see cref="ClearTarget"/>), which way is better, and the normal range's ends (or
/// <see cref="ClearLow"/> and <see cref="ClearHigh"/>). A starter-list name (Weight, Systolic BP)
/// fills in what a new one leaves out.
/// </summary>
public sealed class MetricBody
{
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public int? Decimals { get; set; }
    public decimal? Target { get; set; }
    public bool? ClearTarget { get; set; }
    public string? Better { get; set; }
    public decimal? Low { get; set; }
    public bool? ClearLow { get; set; }
    public decimal? High { get; set; }
    public bool? ClearHigh { get; set; }
}

/// <summary><c>POST /api/measurements/readings</c>: the metric (id or name), the value, when it was taken (now when left out) and a note.</summary>
public sealed class MetricReadingBody
{
    public string? Metric { get; set; }
    public decimal? Value { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>One value in <c>POST /api/measurements/readings/batch</c>: the metric (id or name) and the value.</summary>
public sealed class MetricReadingEntryBody
{
    public string? Metric { get; set; }
    public decimal? Value { get; set; }
}

/// <summary>
/// <c>POST /api/measurements/readings/batch</c>: readings of several metrics taken together
/// (blood pressure's two), with one time (now when left out) and one note. All or nothing.
/// </summary>
public sealed class MetricReadingBatchBody
{
    public IReadOnlyList<MetricReadingEntryBody>? Readings { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public string? Note { get; set; }
}

/// <summary><c>POST /api/measurements/readings/{id}</c>: any of the value, the time and the note; a blank note removes it.</summary>
public sealed class MetricReadingEditBody
{
    public decimal? Value { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>A lab visit's report (MEASURE-4): the artifact whose bytes <c>GET /api/artifacts/{id}/content</c> serves.</summary>
public sealed class LabReportRow
{
    public Guid Id { get; set; }
    public string? Filename { get; set; }
    public string ContentType { get; set; } = "";
    public long ByteSize { get; set; }
}

/// <summary>
/// One lab visit (MEASURE-4, measurements plan §4.9): the lab, a note, when the sample was
/// collected (<see cref="CollectedAt"/>, and in the home zone <see cref="CollectedOn"/>
/// <c>yyyy-MM-dd</c> and <see cref="CollectedLocal"/> <c>yyyy-MM-ddTHH:mm:ss</c>), the report,
/// its results in the order its report prints them, and how many of them are outside the range
/// printed beside them.
/// </summary>
public sealed class LabVisitRow
{
    public Guid Id { get; set; }
    public string? Lab { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
    public string CollectedOn { get; set; } = "";
    public string CollectedLocal { get; set; } = "";
    public LabReportRow? Report { get; set; }
    public IReadOnlyList<MetricReadingRow> Results { get; set; } = [];
    public int Outside { get; set; }
    public DateTimeOffset LoggedAt { get; set; }
    public int Versions { get; set; }
}

/// <summary><c>GET /api/measurements/visits</c>: today in the home zone and every visit, newest collected first.</summary>
public sealed class LabVisitsRead
{
    public string Today { get; set; } = "";
    public IReadOnlyList<LabVisitRow> Visits { get; set; } = [];
}

/// <summary>A visit write's answer: the visit as it now reads, or null after a delete.</summary>
public sealed class LabVisitResult
{
    public LabVisitRow? Visit { get; set; }
}

/// <summary>
/// One result in a visit's body: the test (a metric's id or name), its value, the range printed
/// beside it, a note, the unit for a test that isn't tracked or on the starter list, and in an
/// edit the result it keeps (<see cref="Reading"/>, its id).
/// </summary>
public sealed class LabResultBody
{
    public string? Metric { get; set; }
    public decimal? Value { get; set; }
    public decimal? Low { get; set; }
    public decimal? High { get; set; }
    public string? Note { get; set; }
    public string? Unit { get; set; }
    public string? Reading { get; set; }
}

/// <summary>
/// <c>POST /api/measurements/visits</c> and <c>…/{id}</c>: when the sample was collected (now for a
/// new visit when left out; kept by an edit), the lab and a note (an edit keeps each when left out
/// and clears it when blank), and all the visit's results.
/// </summary>
public sealed class LabVisitBody
{
    public DateTimeOffset? CollectedAt { get; set; }
    public string? Lab { get; set; }
    public string? Note { get; set; }
    public IReadOnlyList<LabResultBody>? Results { get; set; }
}

/// <summary>
/// A metric on the starter list (<see cref="MetricCatalog"/>, measurements plan §2.1): what making
/// one by that name fills in. <see cref="Panel"/> is the lab panel a test is on, null for a body
/// metric; <see cref="Low"/> and <see cref="High"/> the normal range it starts with, when it has one.
/// The web reads the list here rather than keeping a copy (code review CR73).
/// </summary>
public sealed class MetricTemplateRow
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public int Decimals { get; set; }
    public string? Better { get; set; }
    public decimal? Low { get; set; }
    public decimal? High { get; set; }
    public string? Panel { get; set; }
}

/// <summary>Builds the measurement rows from the kernel's records.</summary>
public static class MeasurementRows
{
    /// <summary>The starter list in its order: the body metrics, then the lab tests as reports print them.</summary>
    public static IReadOnlyList<MetricTemplateRow> Catalog() => MetricCatalog.All
        .Select(t => new MetricTemplateRow
        {
            Key = t.Key, Name = t.Name, Unit = t.Unit, Decimals = t.Decimals, Better = t.Better,
            Low = t.Low, High = t.High, Panel = t.Panel
        })
        .ToList();

    public static LabVisitRow Visit(LabVisitRead read) => new()
    {
        Id = read.Visit.Id,
        Lab = read.Visit.Lab,
        Note = read.Visit.Note,
        CollectedAt = read.Visit.CollectedAt,
        CollectedOn = Day(read.Visit.CollectedOn),
        CollectedLocal = read.Visit.CollectedLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        Report = read.Visit.Report is { } report
            ? new LabReportRow
            {
                Id = report.ArtifactId, Filename = report.Filename, ContentType = report.ContentType, ByteSize = report.ByteSize
            }
            : null,
        Results = read.Results.Select(r => Reading(r.Reading, r.Metric)).ToList(),
        Outside = read.Outside,
        LoggedAt = read.Visit.LoggedAt,
        Versions = read.Visit.Versions
    };

    public static MetricRow From(MetricSummary summary) => From(summary.Metric, summary.Readings, summary.Last);

    public static MetricRow From(Metric metric, int readings, MetricReading? last) => new()
    {
        Id = metric.Id,
        Urn = metric.Urn,
        Name = metric.Name,
        Unit = metric.Unit,
        Decimals = metric.Decimals,
        Target = metric.Target,
        Better = metric.Better,
        Catalog = metric.Catalog,
        ArchivedAt = metric.ArchivedAt,
        ArchivedOn = metric.ArchivedOn is { } on ? Day(on) : null,
        Low = metric.Low,
        High = metric.High,
        Panel = metric.Panel,
        Readings = readings,
        LastReading = last is null ? null : Reading(last, metric)
    };

    /// <summary>A reading as a row, flagged against <paramref name="metric"/>'s normal range when it's given.</summary>
    public static MetricReadingRow Reading(MetricReading reading, Metric? metric) => new()
    {
        Id = reading.Id,
        MetricId = reading.MetricId,
        Value = reading.Value,
        Unit = reading.Unit,
        Note = reading.Note,
        TakenAt = reading.TakenAt,
        TakenOn = Day(reading.TakenOn),
        TakenLocal = reading.TakenLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        LoggedAt = reading.LoggedAt,
        Versions = reading.Versions,
        Flag = metric?.Flag(reading),
        VisitId = reading.VisitId,
        Low = reading.Low,
        High = reading.High
    };

    public static MetricDetailRead Detail(MetricRead read, int readings, MetricReading? last) => new()
    {
        Today = Day(read.Today),
        From = read.From is { } f ? Day(f) : null,
        Metric = From(read.Metric, readings, last),
        Readings = read.Readings.Select(r => Reading(r, read.Metric)).ToList(),
        Days = read.Days.Select(p => new MetricDayRow
        {
            Day = Day(p.Day), Value = p.Value, Trend = p.Trend, ReadingId = p.ReadingId
        }).ToList(),
        Goal = new MetricGoalRow
        {
            Status = read.Goal.Status,
            Target = read.Goal.Target,
            Direction = read.Goal.Direction,
            Now = read.Goal.Now,
            RatePerWeek = read.Goal.RatePerWeek,
            ReachedBy = read.Goal.ReachedBy is { } by ? Day(by) : null,
            Basis = new MetricGoalBasis
            {
                Days = read.Goal.BasisDays,
                From = read.Goal.BasisFrom is { } bf ? Day(bf) : null,
                To = read.Goal.BasisTo is { } bt ? Day(bt) : null
            }
        }
    };

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

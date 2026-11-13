using System.Security.Cryptography;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Photos;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Measurements;

/// <summary>
/// One result of a visit being saved: the test (a metric's name or id), its value, the range
/// printed beside it, and a note. <paramref name="Unit"/> adds a test that isn't tracked or on the
/// starter list. <paramref name="Reading"/> keeps a result already in the visit (its id or the
/// start of it); without one, an edit keeps the visit's result of the same test.
/// </summary>
public sealed record LabResultEntry(
    string Test,
    decimal Value,
    decimal? Low = null,
    decimal? High = null,
    string? Note = null,
    string? Unit = null,
    string? Reading = null);

/// <summary>
/// A lab visit as saved: when the sample was collected (now for a new visit when null; kept by an
/// edit), the lab and a note (an edit keeps each when null and clears it when blank), and all its
/// results.
/// </summary>
public sealed record LabVisitEntry(
    DateTimeOffset? CollectedAt, string? Lab, string? Note, IReadOnlyList<LabResultEntry> Results);

/// <summary>A visit's result and its test, flagged against the range printed beside it.</summary>
public sealed record LabResult(MetricReading Reading, Metric Metric)
{
    public string? Flag => Metric.Flag(Reading);
}

/// <summary>A visit and its results, in the order its report prints them.</summary>
public sealed record LabVisitRead(LabVisit Visit, IReadOnlyList<LabResult> Results)
{
    /// <summary>How many results are outside their range.</summary>
    public int Outside => Results.Count(r => r.Flag is not null);
}

/// <summary>Today in the home zone and every visit, newest collected first.</summary>
public sealed record LabVisitList(DateOnly Today, IReadOnlyList<LabVisitRead> Visits);

/// <summary>
/// Lab visits (MEASURE-4, D20, measurements plan §4.9), behind <c>bsk lab</c> and
/// <c>/api/measurements/visits</c>. A visit is a <c>declared</c> <c>measurement</c> event,
/// <c>{lab_visit: {id, lab?, note?, report?}}</c>, with <c>occurred_at</c> when the sample was
/// collected; its results are readings of each test's metric carrying <c>visit</c> and the range
/// printed beside them (<c>low</c>, <c>high</c>), taken at the visit's time.
/// <list type="bullet">
/// <item>A save writes the visit and all its results as one transaction, every refusal checked
/// first. A test not tracked yet is made just before: from the starter list by its name, or under
/// <see cref="LabPanels.Other"/> with the unit given and the decimal places the values use.</item>
/// <item>An edit's results become the ones given: a result left out is deleted, and the rest are
/// written again only when they change. Moving the visit moves its results.</item>
/// <item>After any write, each test it touched takes the range of its newest result that has one,
/// so the chart shades the newest report's range and the next visit starts from it.</item>
/// <item>The report (a PDF or a photo, <see cref="LabReportChecks"/>) is stored by the version that
/// adds it; later versions carry its id. A host without file storage refuses it in words.</item>
/// </list>
/// Lab results are information only: nothing here touches the ledger or a level.
/// </summary>
public sealed class LabVisitService(
    IMetricRepository metrics,
    MetricService metricService,
    IArtifactBlobStore blobs,
    IHomeCalendar calendar,
    IClock clock,
    EventSource eventSource,
    bool reportsEnabled = true)
{
    private readonly LabVisitReads reads = new(metrics, calendar, clock);

    /// <summary>The refusal when reports can't be stored on this host.</summary>
    public const string ReportsNotSetUp =
        "Reports aren't set up on this server yet: it needs a Supabase Storage key (Storage__SupabaseSecretKey).";

    /// <summary>True when this host stores reports.</summary>
    public bool ReportsEnabled => reportsEnabled;

    /// <summary>
    /// Logs a new visit with its results. Refused, with nothing written, for a time in the future,
    /// a test that isn't tracked, on the starter list or given a unit, an archived test, a test
    /// given twice, a value or range end with more decimal places than its test takes, a range
    /// upside down, an overlong lab name or note, or more than 50 results.
    /// </summary>
    public Task<LabVisitRead> LogAsync(LabVisitEntry entry, CancellationToken cancellationToken = default)
        => SaveCoreAsync(null, entry, cancellationToken);

    /// <summary>
    /// Saves a visit as given: its time, lab and note, and its results, which become the ones in
    /// <paramref name="entry"/>. The refusals of <see cref="LogAsync"/>, and a result named by an
    /// id that isn't this visit's. Changing nothing writes nothing.
    /// </summary>
    public async Task<LabVisitRead> SaveAsync(
        string reference, LabVisitEntry entry, CancellationToken cancellationToken = default)
        => await SaveCoreAsync(await ResolveAsync(reference, cancellationToken), entry, cancellationToken);

    /// <summary>Deletes a visit and its results: voided versions, so the record keeps them. Answers the visit as it was.</summary>
    public async Task<LabVisitRead> DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var read = await GetAsync(reference, cancellationToken);
        var now = clock.UtcNow;
        var writes = new List<MetricReadingWrite> { new(Declared(now, now, VoidedVisit(read.Visit.Id)), null) };
        writes.AddRange(read.Results.Select(r => new MetricReadingWrite(
            Declared(now, now, MetricReadingService.Voided(r.Reading.Id)), r.Metric.Id)));
        await metrics.AppendReadingsAsync(writes, cancellationToken);
        await SyncRangesAsync(read.Results.Select(r => r.Metric.Id), cancellationToken);
        return read;
    }

    /// <summary>
    /// Adds <paramref name="bytes"/> as the visit's report, in place of any before it. The same
    /// bytes already its report stay as they are. Refused, with nothing stored, for bytes that
    /// aren't a PDF or a photo, over 10 MB, or on a host without file storage.
    /// </summary>
    public async Task<LabVisitRead> AttachReportAsync(
        string reference, byte[] bytes, string? filename = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!reportsEnabled)
        {
            throw new InvalidOperationException(ReportsNotSetUp);
        }

        var visit = await ResolveAsync(reference, cancellationToken);
        var contentType = LabReportChecks.Check(bytes);
        var now = clock.UtcNow;
        var stored = await blobs.PutAsync(new NewBinaryCapture(
            Kind: EventKinds.Measurement,
            Provenance: Provenances.Declared,
            OccurredAt: visit.CollectedAt,
            RecordedAt: now,
            SourceId: eventSource.Id,
            Sha256: Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Bytes: bytes,
            ContentType: contentType,
            ByteSize: bytes.Length,
            Filename: PhotoChecks.CleanName(filename),
            PayloadJson: VisitPayload(visit.Id, visit.Lab, visit.Note, report: null)), cancellationToken);

        // The same bytes were stored before (for this visit or anything else): point at them.
        if (stored.Deduplicated && visit.Report?.ArtifactId != stored.ArtifactId)
        {
            await metrics.AppendReadingsAsync(
                [new MetricReadingWrite(
                    Declared(visit.CollectedAt, now, VisitPayload(visit.Id, visit.Lab, visit.Note, stored.ArtifactId)), null)],
                cancellationToken);
        }

        return await GetAsync(visit.Id.ToString(), cancellationToken);
    }

    /// <summary>Takes the report off a visit. Its bytes stay in the record. A visit with none stays as it is.</summary>
    public async Task<LabVisitRead> RemoveReportAsync(string reference, CancellationToken cancellationToken = default)
    {
        var visit = await ResolveAsync(reference, cancellationToken);
        if (visit.Report is not null)
        {
            var now = clock.UtcNow;
            await metrics.AppendReadingsAsync(
                [new MetricReadingWrite(Declared(visit.CollectedAt, now, VisitPayload(visit.Id, visit.Lab, visit.Note, null)), null)],
                cancellationToken);
        }

        return await GetAsync(visit.Id.ToString(), cancellationToken);
    }

    /// <summary>Every visit with its results, newest collected first.</summary>
    public Task<LabVisitList> ListAsync(CancellationToken cancellationToken = default) => reads.ListAsync(cancellationToken);

    /// <summary>One visit (by its id or the start of it) with its results.</summary>
    public Task<LabVisitRead> GetAsync(string reference, CancellationToken cancellationToken = default)
        => reads.GetAsync(reference, cancellationToken);

    /// <summary>A live visit named by its id or the start of it (at least 4 hex digits).</summary>
    public Task<LabVisit> ResolveAsync(string reference, CancellationToken cancellationToken = default)
        => reads.ResolveAsync(reference, cancellationToken);

    /// <summary>A home-zone wall-clock time as an instant (for hosts reading "2026-11-09 08:15").</summary>
    public Task<DateTimeOffset> InstantAtAsync(DateTime local, CancellationToken cancellationToken = default)
        => metrics.InstantAtAsync(local, cancellationToken);

    /// <summary>Today in the home zone.</summary>
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
        => calendar.DateAtAsync(clock.UtcNow, cancellationToken);

    private async Task<LabVisitRead> SaveCoreAsync(
        LabVisit? existing, LabVisitEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Results.Count > MetricReadingService.MaxTogether)
        {
            throw new ArgumentException(
                $"A visit takes up to {MetricReadingService.MaxTogether} results; that's {entry.Results.Count}.");
        }

        var now = clock.UtcNow;
        var at = CheckTime(entry.CollectedAt ?? existing?.CollectedAt ?? now, now);
        var lab = entry.Lab is null ? existing?.Lab : LabLimits.CheckLab(entry.Lab);
        var note = entry.Note is null ? existing?.Note : LabLimits.CheckNote(entry.Note);
        var all = await metrics.GetMetricsAsync(cancellationToken);
        var current = existing is null ? [] : await metrics.GetResultsAsync(existing.Id, cancellationToken);
        var plans = Plan(entry.Results, current, all);

        // Everything is checked: make the tests that aren't tracked yet, then write it all at once.
        foreach (var plan in plans.Where(p => p.Metric is null))
        {
            plan.Metric = plan.Template is not null
                ? await metricService.AddAsync(plan.Template.Name, cancellationToken: cancellationToken)
                : await metricService.AddAsync(
                    plan.Name, plan.Unit, plan.Decimals, panel: LabPanels.Other, cancellationToken: cancellationToken);
        }

        var visitId = existing?.Id ?? Guid.NewGuid();
        var writes = new List<MetricReadingWrite>();
        if (existing is null || existing.CollectedAt != at || existing.Lab != lab || existing.Note != note)
        {
            writes.Add(new MetricReadingWrite(
                Declared(at, now, VisitPayload(visitId, lab, note, existing?.Report?.ArtifactId)), null));
        }

        foreach (var plan in plans)
        {
            var metric = plan.Metric!;
            var kept = plan.Kept;
            if (kept is not null && kept.Value == plan.Value && kept.Low == plan.Low && kept.High == plan.High
                && kept.Note == plan.Note && kept.TakenAt == at)
            {
                continue;
            }

            var reading = new ReadingPayload(
                kept?.Id ?? Guid.NewGuid(), metric.Id, plan.Value, kept?.Unit ?? metric.Unit, plan.Note, visitId, plan.Low, plan.High);
            writes.Add(new MetricReadingWrite(Declared(at, now, MetricReadingService.Payload(reading)), metric.Id));
        }

        var dropped = current.Where(c => plans.All(p => p.Kept?.Id != c.Id)).ToList();
        writes.AddRange(dropped.Select(d => new MetricReadingWrite(
            Declared(now, now, MetricReadingService.Voided(d.Id)), d.MetricId)));

        await metrics.AppendReadingsAsync(writes, cancellationToken);
        await SyncRangesAsync(plans.Select(p => p.Metric!.Id).Concat(dropped.Select(d => d.MetricId)), cancellationToken);
        return await GetAsync(visitId.ToString(), cancellationToken);
    }

    /// <summary>
    /// Each result checked and matched to its test and to the visit's result it keeps, before
    /// anything is written. A result named by id keeps that one; one without keeps the visit's
    /// result of the same test, when no other result has claimed it.
    /// </summary>
    private static List<ResultPlan> Plan(
        IReadOnlyList<LabResultEntry> entries, IReadOnlyList<MetricReading> current, IReadOnlyList<Metric> all)
    {
        var claimed = new HashSet<Guid>();
        var named = new Dictionary<LabResultEntry, MetricReading>();
        foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Reading)))
        {
            var wanted = entry.Reading!.Trim();
            var matches = current.Where(c => Guid.TryParse(wanted, out var id)
                    ? c.Id == id
                    : c.Id.ToString().StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1 || wanted.Length < 4)
            {
                throw new InvalidOperationException($"'{wanted}' isn't one of this visit's results.");
            }

            if (!claimed.Add(matches[0].Id))
            {
                throw new ArgumentException("A result is given twice: a visit has one result for each test.");
            }

            named[entry] = matches[0];
        }

        var plans = new List<ResultPlan>(entries.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var kept = named.GetValueOrDefault(entry);
            var metric = kept is not null ? all.First(m => m.Id == kept.MetricId) : Find(all, entry.Test);
            var plan = new ResultPlan { Metric = metric, Kept = kept };
            if (metric is null)
            {
                var name = MetricLimits.CheckName(entry.Test);
                plan.Template = MetricCatalog.Find(name);
                if (plan.Template is null && string.IsNullOrWhiteSpace(entry.Unit))
                {
                    throw new ArgumentException(
                        $"'{name}' isn't a test you track yet: give its unit to add it, as the report prints it (e.g. mIU/L).");
                }

                plan.Name = plan.Template?.Name ?? name;
                plan.Unit = plan.Template?.Unit ?? MetricLimits.CheckUnit(entry.Unit);
                plan.Decimals = plan.Template?.Decimals ?? Places(entry);
            }
            else
            {
                if (metric.Archived && kept is null)
                {
                    throw new InvalidOperationException($"'{metric.Name}' is archived: bring it back before adding results to it.");
                }

                plan.Name = metric.Name;
                plan.Decimals = metric.Decimals;
                if (kept is null)
                {
                    plan.Kept = current.FirstOrDefault(c => c.MetricId == metric.Id && claimed.Add(c.Id));
                }
            }

            if (!seen.Add(metric?.Id.ToString() ?? plan.Name))
            {
                throw new ArgumentException($"{plan.Name} is given twice: a visit has one result for each test.");
            }

            plan.Value = MetricLimits.CheckValue(entry.Value, plan.Decimals, plan.Name);
            (plan.Low, plan.High) = CheckRange(entry.Low, entry.High, plan.Decimals, plan.Name);
            plan.Note = MetricLimits.CheckNote(entry.Note);
            plans.Add(plan);
        }

        return plans;
    }

    /// <summary>A result's printed range as kept, refused in its test's name.</summary>
    private static (decimal? Low, decimal? High) CheckRange(decimal? low, decimal? high, int decimals, string test)
    {
        try
        {
            return MetricLimits.CheckRange(low, high, decimals);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"{test}: {e.Message}", e);
        }
    }

    /// <summary>The decimal places a new test takes: as many as its value or range uses, up to 3.</summary>
    private static int Places(LabResultEntry entry)
        => Math.Min(
            MetricLimits.MaxDecimals,
            new[] { entry.Value, entry.Low ?? 0m, entry.High ?? 0m }.Max(v => (int)decimal.Abs(v).Scale));

    /// <summary>The metric <paramref name="test"/> names, or null when it names none.</summary>
    private static Metric? Find(IReadOnlyList<Metric> all, string test)
    {
        if (string.IsNullOrWhiteSpace(test))
        {
            throw new ArgumentException("Say which test each result is for, by its name, e.g. Glucose.");
        }

        try
        {
            return MetricService.Resolve(all, test);
        }
        catch (SubjectNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Each metric's range becomes its newest result's printed one (plan §4.9), when that differs.
    /// A range the metric can no longer show (its decimal places were cut since) is left as it is.
    /// </summary>
    private async Task SyncRangesAsync(IEnumerable<Guid> metricIds, CancellationToken cancellationToken)
    {
        foreach (var id in metricIds.Distinct())
        {
            var metric = await metrics.GetMetricAsync(id, cancellationToken);
            var newest = (await metrics.GetReadingsAsync(id, null, cancellationToken))
                .Where(r => r.HasOwnRange)
                .OrderBy(r => r.TakenAt).ThenBy(r => r.Id)
                .LastOrDefault();
            if (metric is null || newest is null || (metric.Low == newest.Low && metric.High == newest.High))
            {
                continue;
            }

            try
            {
                await metricService.SetAsync(
                    id.ToString(),
                    new MetricChange(
                        Low: newest.Low, ClearLow: newest.Low is null, High: newest.High, ClearHigh: newest.High is null),
                    cancellationToken);
            }
            catch (ArgumentException)
            {
                // The result was checked against the decimals it was written with; a metric cut to
                // fewer since keeps the range it has.
            }
        }
    }

    private static DateTimeOffset CheckTime(DateTimeOffset at, DateTimeOffset now)
    {
        if (at > now + MetricLimits.Skew)
        {
            throw new ArgumentException("A lab visit is collected now or earlier, not in the future.");
        }

        return at < MetricReadingService.Earliest
            ? throw new ArgumentException("A lab visit is collected in 1900 or later.")
            : at;
    }

    private NewEvent Declared(DateTimeOffset occurredAt, DateTimeOffset now, string payload)
        => new(EventKinds.Measurement, Provenances.Declared, occurredAt, now, eventSource.Id, PayloadJson: payload);

    /// <summary>A visit version's payload; the lab, note and report left out when there are none.</summary>
    private static string VisitPayload(Guid id, string? lab, string? note, Guid? report)
    {
        var visit = new Dictionary<string, object> { ["id"] = id.ToString() };
        if (lab is not null)
        {
            visit["lab"] = lab;
        }

        if (note is not null)
        {
            visit["note"] = note;
        }

        if (report is { } r)
        {
            visit["report"] = r.ToString();
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { [LabLimits.VisitKey] = visit });
    }

    private static string VoidedVisit(Guid id)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [LabLimits.VisitKey] = new Dictionary<string, object> { ["id"] = id.ToString(), ["voided"] = true }
        });

    /// <summary>One result as it will be written: its test (made first when it's new) and the result it keeps.</summary>
    private sealed class ResultPlan
    {
        public Metric? Metric { get; set; }
        public MetricTemplate? Template { get; set; }
        public MetricReading? Kept { get; set; }
        public string Name { get; set; } = "";
        public string? Unit { get; set; }
        public int Decimals { get; set; }
        public decimal Value { get; set; }
        public decimal? Low { get; set; }
        public decimal? High { get; set; }
        public string? Note { get; set; }
    }
}

/// <summary>
/// The lab visits' reads (MEASURE-4, plan §4.9), shared by <see cref="LabVisitService"/> and the
/// API's reader connection: each visit with its results, in the order its report prints them.
/// </summary>
public sealed class LabVisitReads(IMetricRepository metrics, IHomeCalendar calendar, IClock clock)
{
    /// <summary>Every visit with its results, newest collected first.</summary>
    public async Task<LabVisitList> ListAsync(CancellationToken cancellationToken = default)
    {
        var today = await calendar.DateAtAsync(clock.UtcNow, cancellationToken);
        var visits = await metrics.GetVisitsAsync(cancellationToken);
        var results = (await metrics.GetResultsAsync(null, cancellationToken)).ToLookup(r => r.VisitId);
        var all = await metrics.GetMetricsAsync(cancellationToken);
        return new LabVisitList(today, visits.Select(v => Read(v, results[v.Id], all)).ToList());
    }

    /// <summary>One visit (by its id or the start of it) with its results.</summary>
    public async Task<LabVisitRead> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var visit = await ResolveAsync(reference, cancellationToken);
        return Read(
            visit, await metrics.GetResultsAsync(visit.Id, cancellationToken), await metrics.GetMetricsAsync(cancellationToken));
    }

    /// <summary>A live visit named by its id or the start of it (at least 4 hex digits).</summary>
    public async Task<LabVisit> ResolveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var wanted = reference?.Trim() ?? "";
        if (Guid.TryParse(wanted, out var id))
        {
            return await metrics.GetVisitAsync(id, cancellationToken)
                   ?? throw new InvalidOperationException($"No lab visit has the id {id} (it may have been deleted).");
        }

        if (wanted.Length < 4 || !wanted.All(ch => Uri.IsHexDigit(ch) || ch == '-'))
        {
            throw new ArgumentException($"'{reference}' isn't a lab visit's id: give at least its first 4 hex digits.");
        }

        var matches = await metrics.FindVisitsAsync(wanted, cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"No lab visit's id starts with '{wanted}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException($"'{wanted}' starts {matches.Count} lab visits' ids: give more of the id.")
        };
    }

    private static LabVisitRead Read(LabVisit visit, IEnumerable<MetricReading> results, IReadOnlyList<Metric> all)
    {
        var byId = all.ToDictionary(m => m.Id);
        return new LabVisitRead(
            visit,
            results
                .Where(r => byId.ContainsKey(r.MetricId))
                .Select(r => new LabResult(r, byId[r.MetricId]))
                .OrderBy(r => LabPanels.Order(r.Metric.Panel))
                .ThenBy(r => MetricCatalog.IndexOf(r.Metric.Catalog))
                .ThenBy(r => r.Metric.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }
}

/// <summary>
/// What a lab report upload must be (measurements plan §4.9): a PDF, or a JPEG, PNG or WebP photo
/// of the paper, known by its first bytes whatever the upload claims; not empty, and at most
/// <see cref="LabLimits.MaxReportBytes"/>.
/// </summary>
public static class LabReportChecks
{
    /// <summary>The report's type, or refused in words.</summary>
    public static string Check(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            throw new ArgumentException("That report is empty.");
        }

        if (bytes.Length > LabLimits.MaxReportBytes)
        {
            throw new ArgumentException(TooBig(bytes.Length));
        }

        if (bytes.AsSpan().StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        return PhotoChecks.Sniff(bytes)
               ?? throw new ArgumentException("That isn't a PDF or a photo: send the report as a PDF, or a JPEG, PNG or WebP photo of it.");
    }

    /// <summary>The refusal for a report of <paramref name="length"/> bytes, over the limit.</summary>
    public static string TooBig(long length)
        => $"That report is {length / (1024.0 * 1024.0):0.0} MB; a report can be at most {LabLimits.MaxReportBytes / (1024 * 1024)} MB.";
}

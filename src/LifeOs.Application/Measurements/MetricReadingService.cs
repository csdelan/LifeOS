using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Measurements;

/// <summary>
/// A change to one reading. Each field left null keeps its value; a blank
/// <paramref name="Note"/> removes the note.
/// </summary>
public sealed record ReadingEdit(decimal? Value = null, DateTimeOffset? TakenAt = null, string? Note = null);

/// <summary>One reading among several logged together: the metric (by name or id) and its value.</summary>
public sealed record ReadingEntry(string Metric, decimal Value);

/// <summary>Today in the home zone and the readings in the range asked for, newest first.</summary>
public sealed record ReadingList(DateOnly Today, int? Days, IReadOnlyList<MetricReading> Readings);

/// <summary>
/// Readings of the metrics (MEASURE-1, D20, measurements plan §4.2), behind <c>bsk measure</c> and
/// <c>/api/measurements/readings</c>. A reading is a <c>declared</c> <c>measurement</c> event,
/// <c>{metric_reading: {id, metric, value, unit, note?}}</c>, with a <c>concerns</c> edge to the
/// metric; <c>occurred_at</c> is when it was taken, so a past weigh-in is entered with its own
/// time. An edit writes a newer version with the same id, and a delete writes
/// <c>{id, voided: true}</c>. Readings are information only: nothing here touches the ledger or a
/// level. Refusals are in words, with nothing written.
/// </summary>
public sealed class MetricReadingService(
    IMetricRepository metrics, MetricService metricService, IHomeCalendar calendar, IClock clock, string sourceId)
{
    /// <summary>How far back a list reads by default.</summary>
    public const int DefaultDays = 90;

    /// <summary>The longest range asked for by days: ten years.</summary>
    public const int MaxDays = 3660;

    /// <summary>The earliest time a reading can have.</summary>
    public static readonly DateTimeOffset Earliest = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The most readings logged together: a lab report's worth.</summary>
    public const int MaxTogether = 50;

    /// <summary>
    /// Logs <paramref name="value"/> for <paramref name="metric"/>, taken at
    /// <paramref name="takenAt"/> (now when null). Refused for an archived or unknown metric, a
    /// value with more decimal places than the metric takes, a time in the future, or an
    /// overlong note.
    /// </summary>
    public async Task<MetricReading> LogAsync(
        string metric, decimal value, DateTimeOffset? takenAt = null, string? note = null,
        CancellationToken cancellationToken = default)
        => (await LogManyAsync([new ReadingEntry(metric, value)], takenAt, note, cancellationToken))[0];

    /// <summary>
    /// Logs readings of several metrics taken together (blood pressure's systolic and diastolic,
    /// MEASURE-3; a lab report's values, M4), all at <paramref name="takenAt"/> (now when null) with
    /// one note, in one transaction. Every one is checked before anything is written, so a refusal
    /// writes none: the refusals of <see cref="LogAsync"/>, a metric named twice, or none or more
    /// than <see cref="MaxTogether"/> at once. Answers them in the order given.
    /// </summary>
    public async Task<IReadOnlyList<MetricReading>> LogManyAsync(
        IReadOnlyList<ReadingEntry> entries, DateTimeOffset? takenAt = null, string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            throw new ArgumentException("Give at least one reading: a metric and its value.");
        }

        if (entries.Count > MaxTogether)
        {
            throw new ArgumentException($"Log up to {MaxTogether} readings at once; that's {entries.Count}.");
        }

        var all = await metrics.GetMetricsAsync(cancellationToken);
        var now = clock.UtcNow;
        var at = CheckTime(takenAt ?? now, now);
        var keptNote = MetricLimits.CheckNote(note);
        var writes = new List<MetricReadingWrite>(entries.Count);
        var ids = new List<Guid>(entries.Count);
        var seen = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            var target = MetricService.Resolve(all, entry.Metric);
            if (target.Archived)
            {
                throw new InvalidOperationException($"'{target.Name}' is archived: bring it back before logging to it.");
            }

            if (!seen.Add(target.Id))
            {
                throw new ArgumentException($"{target.Name} is given twice: log one value for each metric.");
            }

            var kept = MetricLimits.CheckValue(entry.Value, target.Decimals, target.Name);
            var id = Guid.NewGuid();
            ids.Add(id);
            writes.Add(new MetricReadingWrite(Declared(at, now, Payload(id, target.Id, kept, target.Unit, keptNote)), target.Id));
        }

        await metrics.AppendReadingsAsync(writes, cancellationToken);
        var readings = new List<MetricReading>(ids.Count);
        foreach (var id in ids)
        {
            readings.Add(await ReadAsync(id, cancellationToken));
        }

        return readings;
    }

    /// <summary>
    /// Changes a reading's value, time or note. Changing nothing writes nothing. The value is
    /// checked against the metric's decimals; the reading keeps the unit it was written in.
    /// </summary>
    public async Task<MetricReading> EditAsync(
        string reference, ReadingEdit edit, CancellationToken cancellationToken = default)
    {
        var reading = await ResolveAsync(reference, cancellationToken);
        var metric = await metrics.GetMetricAsync(reading.MetricId, cancellationToken)
                     ?? throw new InvalidOperationException("That reading's metric no longer exists.");
        var now = clock.UtcNow;
        var value = edit.Value is { } v ? MetricLimits.CheckValue(v, metric.Decimals, metric.Name) : reading.Value;
        var at = edit.TakenAt is { } t ? CheckTime(t, now) : reading.TakenAt;
        var note = edit.Note is null ? reading.Note : MetricLimits.CheckNote(edit.Note);
        if (value == reading.Value && at == reading.TakenAt && note == reading.Note)
        {
            return reading;
        }

        await metrics.AppendReadingsAsync(
            [new MetricReadingWrite(Declared(at, now, Payload(reading.Id, metric.Id, value, reading.Unit, note)), metric.Id)],
            cancellationToken);
        return await ReadAsync(reading.Id, cancellationToken);
    }

    /// <summary>Deletes a reading: a voided version, so the record keeps what it was. Answers the reading as it was.</summary>
    public async Task<MetricReading> DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var reading = await ResolveAsync(reference, cancellationToken);
        var now = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [MetricLimits.ReadingKey] = new Dictionary<string, object> { ["id"] = reading.Id.ToString(), ["voided"] = true }
        });
        await metrics.AppendReadingsAsync([new MetricReadingWrite(Declared(now, now, payload), reading.MetricId)], cancellationToken);
        return reading;
    }

    /// <summary>
    /// The readings taken in the last <paramref name="days"/> days (today included; all of them
    /// when null), of <paramref name="metric"/> only when given, newest first. Throws
    /// <see cref="ArgumentException"/> for a count outside 1 to <see cref="MaxDays"/>.
    /// </summary>
    public async Task<ReadingList> ListAsync(
        string? metric = null, int? days = DefaultDays, CancellationToken cancellationToken = default)
    {
        if (days is { } d)
        {
            Check(d);
        }

        var today = await TodayAsync(cancellationToken);
        Guid? metricId = string.IsNullOrWhiteSpace(metric) ? null : (await metricService.ResolveAsync(metric, cancellationToken)).Id;
        var readings = await metrics.GetReadingsAsync(metricId, days is { } n ? today.AddDays(1 - n) : null, cancellationToken);
        return new ReadingList(
            today, days, readings.OrderByDescending(r => r.TakenAt).ThenByDescending(r => r.Id).ToList());
    }

    /// <summary>A live reading named by its id or the start of it (at least 4 hex digits).</summary>
    public async Task<MetricReading> ResolveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var wanted = reference?.Trim() ?? "";
        if (Guid.TryParse(wanted, out var id))
        {
            return await metrics.GetReadingAsync(id, cancellationToken)
                   ?? throw new InvalidOperationException($"No reading has the id {id} (it may have been deleted).");
        }

        if (wanted.Length < 4 || !wanted.All(ch => Uri.IsHexDigit(ch) || ch == '-'))
        {
            throw new ArgumentException($"'{reference}' isn't a reading's id: give at least its first 4 hex digits.");
        }

        var matches = await metrics.FindReadingsAsync(wanted, cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"No reading's id starts with '{wanted}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException($"'{wanted}' starts {matches.Count} readings' ids: give more of the id.")
        };
    }

    /// <summary>Today in the home zone.</summary>
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
        => calendar.DateAtAsync(clock.UtcNow, cancellationToken);

    /// <summary>A home-zone wall-clock time as an instant (for hosts reading "2026-11-01 07:10").</summary>
    public Task<DateTimeOffset> InstantAtAsync(DateTime local, CancellationToken cancellationToken = default)
        => metrics.InstantAtAsync(local, cancellationToken);

    /// <summary>Throws <see cref="ArgumentException"/> for a day count outside 1 to <see cref="MaxDays"/>.</summary>
    public static void Check(int days)
    {
        if (days < 1 || days > MaxDays)
        {
            throw new ArgumentException($"Ask for 1 to {MaxDays} days; got {days}.");
        }
    }

    private static DateTimeOffset CheckTime(DateTimeOffset at, DateTimeOffset now)
    {
        if (at > now + MetricLimits.Skew)
        {
            throw new ArgumentException("A reading is taken now or earlier, not in the future.");
        }

        return at < Earliest
            ? throw new ArgumentException("A reading is taken in 1900 or later.")
            : at;
    }

    private async Task<MetricReading> ReadAsync(Guid id, CancellationToken cancellationToken)
        => await metrics.GetReadingAsync(id, cancellationToken)
           ?? throw new InvalidOperationException($"The reading {id} wasn't found after it was written.");

    private NewEvent Declared(DateTimeOffset occurredAt, DateTimeOffset now, string payload)
        => new(EventKinds.Measurement, Provenances.Declared, occurredAt, now, sourceId, PayloadJson: payload);

    /// <summary>A reading's payload; the value as a number (192.4), the note left out when there's none.</summary>
    private static string Payload(Guid id, Guid metric, decimal value, string unit, string? note)
    {
        var reading = new Dictionary<string, object>
        {
            ["id"] = id.ToString(),
            ["metric"] = metric.ToString(),
            ["value"] = value,
            ["unit"] = unit
        };
        if (note is not null)
        {
            reading["note"] = note;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { [MetricLimits.ReadingKey] = reading });
    }
}

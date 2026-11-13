using LifeOs.Application.Abstractions;
using LifeOs.Application.Measurements;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// The measurements' reads (MEASURE-1, MEASURE-2, MEASURE-4, measurements plan §4.6, §4.9) on the
/// <c>bsk_reader</c> connection, like every other API read: the metrics and readings through
/// <c>bsk.v_metric</c> and <c>bsk.v_metric_reading</c>, with the trend and goal from
/// <see cref="MetricReads"/>, and the lab visits through <c>bsk.v_lab_visit</c>, on the days of
/// the home zone. No writes live here; they go through <see cref="MetricService"/>,
/// <see cref="MetricReadingService"/> and <see cref="LabVisitService"/>.
/// </summary>
public sealed class MeasurementReader(IMetricRepository metrics, MetricReads reads, LabVisitReads visits)
{
    /// <summary>Every lab visit with its results, newest collected first (<c>GET /api/measurements/visits</c>).</summary>
    public async Task<LabVisitsRead> GetVisitsAsync(CancellationToken cancellationToken = default)
    {
        var list = await visits.ListAsync(cancellationToken);
        return new LabVisitsRead
        {
            Today = list.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Visits = list.Visits.Select(MeasurementRows.Visit).ToList()
        };
    }

    /// <summary>One lab visit (by its id or the start of it) with its results.</summary>
    public async Task<LabVisitRow> GetVisitAsync(string visit, CancellationToken cancellationToken = default)
        => MeasurementRows.Visit(await visits.GetAsync(visit, cancellationToken));

    /// <summary>Every metric, archived ones included, by name (<c>GET /api/measurements/metrics</c>).</summary>
    public async Task<IReadOnlyList<MetricRow>> GetMetricsAsync(CancellationToken cancellationToken = default)
        => (await reads.ListAsync(includeArchived: true, cancellationToken)).Select(MeasurementRows.From).ToList();

    /// <summary>One metric as a row, with its count and newest reading.</summary>
    public async Task<MetricRow> GetMetricAsync(Guid id, CancellationToken cancellationToken = default)
        => (await GetMetricsAsync(cancellationToken)).First(m => m.Id == id);

    /// <summary>
    /// A metric (by id or name) over the last <paramref name="days"/> days, or all its history
    /// (<c>GET /api/measurements/metrics/{id}</c>).
    /// </summary>
    public async Task<MetricDetailRead> GetDetailAsync(
        string metric, int? days, CancellationToken cancellationToken = default)
    {
        var read = await reads.ReadAsync(metric, days, cancellationToken);
        var all = await metrics.GetReadingsAsync(read.Metric.Id, cancellationToken: cancellationToken);
        return MeasurementRows.Detail(read, all.Count, all.Count == 0 ? null : all[^1]);
    }

    /// <summary>A reading as a row, flagged against its metric's normal range, or null when it's gone (deleted).</summary>
    public async Task<MetricReadingRow?> GetReadingAsync(Guid id, CancellationToken cancellationToken = default)
        => await metrics.GetReadingAsync(id, cancellationToken) is { } reading
            ? MeasurementRows.Reading(reading, await metrics.GetMetricAsync(reading.MetricId, cancellationToken))
            : null;
}

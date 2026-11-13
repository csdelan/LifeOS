using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Measurements;

/// <summary>A metric with how many readings it has and its newest one.</summary>
public sealed record MetricSummary(Metric Metric, int Readings, MetricReading? Last);

/// <summary>
/// One metric over a range (MEASURE-2, measurements plan §4.6): its readings taken from
/// <paramref name="From"/> (all of them when null) to today, newest first; the day values with the
/// trend, oldest first; and the goal, which always reads the newest 28 days.
/// </summary>
public sealed record MetricRead(
    DateOnly Today,
    DateOnly? From,
    Metric Metric,
    IReadOnlyList<MetricReading> Readings,
    IReadOnlyList<MetricTrendPoint> Days,
    MetricGoal Goal);

/// <summary>
/// The reads <c>bsk metric</c> and <c>/api/measurements</c> share: each metric with its newest
/// reading, and one metric with its day values, trend and goal date (§2.2–§2.3). The trend and the
/// goal are worked out from all of a metric's history, so a short range starts from the trend as
/// it was. The API builds one on the reader connection.
/// </summary>
public sealed class MetricReads(IMetricRepository metrics, IHomeCalendar calendar)
{
    /// <summary>Every metric (archived ones only when asked), by name, with its count and newest reading.</summary>
    public async Task<IReadOnlyList<MetricSummary>> ListAsync(
        bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        var all = await metrics.GetMetricsAsync(cancellationToken);
        var readings = await metrics.GetReadingsAsync(cancellationToken: cancellationToken);
        var byMetric = readings.ToLookup(r => r.MetricId);
        return all.Where(m => includeArchived || !m.Archived)
            .Select(m =>
            {
                var own = byMetric[m.Id].ToList();
                return new MetricSummary(m, own.Count, own.OrderBy(r => r.TakenAt).ThenBy(r => r.Id).LastOrDefault());
            })
            .ToList();
    }

    /// <summary>
    /// <paramref name="reference"/>'s readings and trend over the last <paramref name="days"/>
    /// days (all history when null), with its goal. Throws for an unknown metric or a day count
    /// outside 1 to <see cref="MetricReadingService.MaxDays"/>.
    /// </summary>
    public async Task<MetricRead> ReadAsync(string reference, int? days = null, CancellationToken cancellationToken = default)
    {
        if (days is { } d)
        {
            MetricReadingService.Check(d);
        }

        var metric = MetricService.Resolve(await metrics.GetMetricsAsync(cancellationToken), reference);
        var today = await calendar.TodayAsync(cancellationToken);
        DateOnly? from = days is { } n ? today.AddDays(1 - n) : null;

        var readings = await metrics.GetReadingsAsync(metric.Id, cancellationToken: cancellationToken);
        var dayValues = MetricTrend.DayValues(readings);
        var trend = MetricTrend.Trend(dayValues, metric.Decimals);
        var goal = MetricTrend.Goal(dayValues, metric.Target, metric.Better, metric.Decimals);

        return new MetricRead(
            today,
            from,
            metric,
            readings.Where(r => from is null || r.TakenOn >= from)
                .OrderByDescending(r => r.TakenAt).ThenByDescending(r => r.Id).ToList(),
            trend.Where(p => from is null || p.Day >= from).ToList(),
            goal);
    }
}

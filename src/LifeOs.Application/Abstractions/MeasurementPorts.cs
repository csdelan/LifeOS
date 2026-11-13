using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// Metrics, readings and lab visits (D20).

/// <summary>
/// A reading version to append and the metric its <c>concerns</c> edge points at (MEASURE-1); a lab
/// visit's own version (MEASURE-4) has no metric and so no edge.
/// </summary>
public sealed record MetricReadingWrite(NewEvent Event, Guid? MetricId);

/// <summary>
/// Metrics, their readings and lab visits (MEASURE-1, MEASURE-4, D20, measurements plan §4.1–§4.3,
/// §4.9), through <c>bsk.v_metric</c>, <c>bsk.v_metric_reading</c> (migrations 0050, 0051, 0053)
/// and <c>bsk.v_lab_visit</c> (0053).
/// </summary>
public interface IMetricRepository
{
    /// <summary>Every metric, archived ones included, by name.</summary>
    Task<IReadOnlyList<Metric>> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>The metric with that id, or null.</summary>
    Task<Metric?> GetMetricAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live readings, oldest first by when they were taken: of <paramref name="metricId"/>
    /// only when given, and taken on or after the home-zone day <paramref name="from"/> when given.
    /// </summary>
    Task<IReadOnlyList<MetricReading>> GetReadingsAsync(
        Guid? metricId = null, DateOnly? from = null, CancellationToken cancellationToken = default);

    /// <summary>How many live readings each metric has, by metric id (a metric with none is left out).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountReadingsAsync(CancellationToken cancellationToken = default);

    /// <summary>The live reading with that id, or null (never written, or deleted).</summary>
    Task<MetricReading?> GetReadingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Live readings whose id starts with <paramref name="prefix"/> (hex), newest first.</summary>
    Task<IReadOnlyList<MetricReading>> FindReadingsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>A home-zone wall-clock time as an instant.</summary>
    Task<DateTimeOffset> InstantAtAsync(DateTime local, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends reading versions (and a lab visit's own versions) and their <c>concerns</c> edges as
    /// one transaction. Returns the new event ids, in order.
    /// </summary>
    Task<IReadOnlyList<Guid>> AppendReadingsAsync(
        IReadOnlyList<MetricReadingWrite> writes, CancellationToken cancellationToken = default);

    /// <summary>Every live lab visit, newest collected first.</summary>
    Task<IReadOnlyList<LabVisit>> GetVisitsAsync(CancellationToken cancellationToken = default);

    /// <summary>The live lab visit with that id, or null (never written, or deleted).</summary>
    Task<LabVisit?> GetVisitAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Live lab visits whose id starts with <paramref name="prefix"/> (hex), newest first.</summary>
    Task<IReadOnlyList<LabVisit>> FindVisitsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>The live results of <paramref name="visitId"/>, or of every visit when null, oldest first.</summary>
    Task<IReadOnlyList<MetricReading>> GetResultsAsync(Guid? visitId = null, CancellationToken cancellationToken = default);
}

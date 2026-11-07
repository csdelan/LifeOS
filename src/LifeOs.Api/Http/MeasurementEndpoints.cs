using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Measurements;

namespace LifeOs.Api.Http;

/// <summary>
/// Metrics and their readings (MEASURE-1, MEASURE-2, measurements plan §4.6) — the HTTP twin of
/// <c>bsk metric</c> and <c>bsk measure</c>. Reads go through <see cref="MeasurementReader"/> on
/// the reader connection; writes call <see cref="MetricService"/> and
/// <see cref="MetricReadingService"/>, as the CLI does. Readings are information only, so no write
/// answers awards or progress. A refusal (a future time, too many decimal places, an archived or
/// unknown metric, a unit change once there are readings) is a 400 in words, with nothing written.
/// Archiving a metric uses <c>POST /api/archive</c>, as any subject does.
/// </summary>
public static class MeasurementEndpoints
{
    public static void MapMeasurementEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/measurements")
            .WithTags("Measurements")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/metrics", GetMetrics).WithName("GetMetrics")
            .Produces<IReadOnlyList<MetricRow>>();
        api.MapPost("/metrics", AddMetric).WithName("AddMetric")
            .Produces<MetricRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/metrics/{id}", GetMetric).WithName("GetMetric")
            .Produces<MetricDetailRead>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/metrics/{id}", SetMetric).WithName("SetMetric")
            .Produces<MetricRow>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/readings", LogReading).WithName("LogReading")
            .Produces<MetricReadingResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/readings/{id}", EditReading).WithName("EditReading")
            .Produces<MetricReadingResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/readings/{id}/delete", DeleteReading).WithName("DeleteReading")
            .Produces<MetricReadingResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    /// <summary>Every metric, archived ones included, by name, with its count and newest reading.</summary>
    private static async Task<IResult> GetMetrics(MeasurementReader measurements, CancellationToken cancellationToken)
        => Results.Ok(await measurements.GetMetricsAsync(cancellationToken));

    /// <summary>Adds a metric; a starter-list name (Weight) fills in the unit, decimals and which way is better.</summary>
    private static async Task<IResult> AddMetric(
        MetricBody body, MetricService metrics, MeasurementReader measurements, CancellationToken cancellationToken)
    {
        var metric = await metrics.AddAsync(
            body.Name ?? "", body.Unit, body.Decimals, body.Target, body.Better, cancellationToken);
        return Results.Ok(await measurements.GetMetricAsync(metric.Id, cancellationToken));
    }

    /// <summary>
    /// A metric (by id or name) over the last <paramref name="days"/> days (1–3,660; all its history
    /// when left out): readings newest first, day values with the trend, and the goal.
    /// </summary>
    private static async Task<IResult> GetMetric(
        string id, int? days, MeasurementReader measurements, CancellationToken cancellationToken)
        => Results.Ok(await measurements.GetDetailAsync(id, days, cancellationToken));

    /// <summary>Renames a metric (by id or name) or changes its unit, decimals, target or which way is better.</summary>
    private static async Task<IResult> SetMetric(
        string id, MetricBody body, MetricService metrics, MeasurementReader measurements, CancellationToken cancellationToken)
    {
        var metric = await metrics.SetAsync(
            id,
            new MetricChange(body.Name, body.Unit, body.Decimals, body.Target, body.ClearTarget ?? false, body.Better),
            cancellationToken);
        return Results.Ok(await measurements.GetMetricAsync(metric.Id, cancellationToken));
    }

    /// <summary>Logs a reading of a metric (by id or name), taken now unless <c>takenAt</c> says when.</summary>
    private static async Task<IResult> LogReading(
        MetricReadingBody body, MetricReadingService readings, MeasurementReader measurements,
        CancellationToken cancellationToken)
    {
        if (body.Value is not { } value)
        {
            return Results.BadRequest(new ApiError("Give the reading's value, e.g. 192.4."));
        }

        var reading = await readings.LogAsync(body.Metric ?? "", value, body.TakenAt, body.Note, cancellationToken);
        return Results.Ok(new MetricReadingResult { Reading = await measurements.GetReadingAsync(reading.Id, cancellationToken) });
    }

    /// <summary>Changes a reading's value, time or note.</summary>
    private static async Task<IResult> EditReading(
        string id, MetricReadingEditBody body, MetricReadingService readings, MeasurementReader measurements,
        CancellationToken cancellationToken)
    {
        var reading = await readings.EditAsync(id, new ReadingEdit(body.Value, body.TakenAt, body.Note), cancellationToken);
        return Results.Ok(new MetricReadingResult { Reading = await measurements.GetReadingAsync(reading.Id, cancellationToken) });
    }

    /// <summary>Deletes a reading. Its versions stay in the record.</summary>
    private static async Task<IResult> DeleteReading(
        string id, MetricReadingService readings, CancellationToken cancellationToken)
    {
        await readings.DeleteAsync(id, cancellationToken);
        return Results.Ok(new MetricReadingResult { Reading = null });
    }
}

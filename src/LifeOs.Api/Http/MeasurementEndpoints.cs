using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Measurements;
using LifeOs.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LifeOs.Api.Http;

/// <summary>
/// Metrics, their readings and lab visits (MEASURE-1 to MEASURE-4, measurements plan §4.6, §4.8,
/// §4.9) — the HTTP twin of <c>bsk metric</c>, <c>bsk measure</c> and <c>bsk lab</c>. Reads go
/// through <see cref="MeasurementReader"/> on the reader connection; writes call
/// <see cref="MetricService"/>, <see cref="MetricReadingService"/> and <see cref="LabVisitService"/>,
/// as the CLI does. Readings are information only, so no write answers awards or progress. A
/// refusal (a future time, too many decimal places, an archived or unknown metric, a unit change
/// once there are readings, a report that isn't a PDF or photo) is a 400 in words, with nothing
/// written. Archiving a metric uses <c>POST /api/archive</c>, as any subject does.
/// </summary>
public static class MeasurementEndpoints
{
    // Room for the multipart envelope around a report.
    private const int ReportRequestOverhead = 64 * 1024;

    public static void MapMeasurementEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/measurements")
            .WithTags("Measurements")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/catalog", GetCatalog).WithName("GetMetricCatalog")
            .Produces<IReadOnlyList<MetricTemplateRow>>();
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
        api.MapPost("/readings/batch", LogReadings).WithName("LogReadings")
            .Produces<MetricReadingsResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/readings/{id}", EditReading).WithName("EditReading")
            .Produces<MetricReadingResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/readings/{id}/delete", DeleteReading).WithName("DeleteReading")
            .Produces<MetricReadingResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);

        // Lab visits (MEASURE-4, measurements plan §4.9).
        api.MapGet("/visits", GetVisits).WithName("GetLabVisits")
            .Produces<LabVisitsRead>();
        api.MapGet("/visits/{id}", GetVisit).WithName("GetLabVisit")
            .Produces<LabVisitResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/visits", LogVisit).WithName("LogLabVisit")
            .Produces<LabVisitResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/visits/{id}", SaveVisit).WithName("SaveLabVisit")
            .Produces<LabVisitResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/visits/{id}/delete", DeleteVisit).WithName("DeleteLabVisit")
            .Produces<LabVisitResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/visits/{id}/report", AttachReport).WithName("AttachLabReport")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(LabLimits.MaxReportBytes + ReportRequestOverhead))
            .Produces<LabVisitResult>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
        api.MapPost("/visits/{id}/report/delete", RemoveReport).WithName("RemoveLabReport")
            .Produces<LabVisitResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    /// <summary>Every lab visit with its results, newest collected first.</summary>
    private static async Task<IResult> GetVisits(MeasurementReader measurements, CancellationToken cancellationToken)
        => Results.Ok(await measurements.GetVisitsAsync(cancellationToken));

    /// <summary>One lab visit (by its id or the start of it) with its results.</summary>
    private static async Task<IResult> GetVisit(string id, MeasurementReader measurements, CancellationToken cancellationToken)
        => Results.Ok(new LabVisitResult { Visit = await measurements.GetVisitAsync(id, cancellationToken) });

    /// <summary>
    /// Logs a lab visit with all its results at once: each a test (by id or name), its value and
    /// the range printed beside it. A test not tracked yet is made from the starter list, or with
    /// the unit given. All or nothing.
    /// </summary>
    private static async Task<IResult> LogVisit(
        LabVisitBody body, LabVisitService visits, MeasurementReader measurements, CancellationToken cancellationToken)
    {
        if (Entry(body, out var refusal) is not { } entry)
        {
            return refusal!;
        }

        var read = await visits.LogAsync(entry, cancellationToken);
        return Results.Ok(new LabVisitResult { Visit = await measurements.GetVisitAsync(read.Visit.Id.ToString(), cancellationToken) });
    }

    /// <summary>
    /// Saves a lab visit as given: its time, lab and note, and its results, which become the ones
    /// in the body (a result left out is deleted). All or nothing.
    /// </summary>
    private static async Task<IResult> SaveVisit(
        string id, LabVisitBody body, LabVisitService visits, MeasurementReader measurements,
        CancellationToken cancellationToken)
    {
        if (Entry(body, out var refusal) is not { } entry)
        {
            return refusal!;
        }

        var read = await visits.SaveAsync(id, entry, cancellationToken);
        return Results.Ok(new LabVisitResult { Visit = await measurements.GetVisitAsync(read.Visit.Id.ToString(), cancellationToken) });
    }

    /// <summary>Deletes a lab visit and its results. Their versions stay in the record.</summary>
    private static async Task<IResult> DeleteVisit(string id, LabVisitService visits, CancellationToken cancellationToken)
    {
        await visits.DeleteAsync(id, cancellationToken);
        return Results.Ok(new LabVisitResult { Visit = null });
    }

    /// <summary>
    /// Adds a lab visit's report, in place of any before it: a PDF, or a JPEG, PNG or WebP photo of
    /// the paper, of at most 10 MB, in the <c>report</c> field.
    /// </summary>
    private static async Task<IResult> AttachReport(
        string id, IFormFile? report, LabVisitService visits, MeasurementReader measurements,
        CancellationToken cancellationToken)
    {
        if (report is null)
        {
            return Results.BadRequest(new ApiError("Send the report as multipart/form-data, in a field named report."));
        }

        if (report.Length > LabLimits.MaxReportBytes)
        {
            return Results.BadRequest(new ApiError(LabReportChecks.TooBig(report.Length)));
        }

        using var buffer = new MemoryStream((int)report.Length);
        await report.CopyToAsync(buffer, cancellationToken);
        var read = await visits.AttachReportAsync(id, buffer.ToArray(), report.FileName, cancellationToken);
        return Results.Ok(new LabVisitResult { Visit = await measurements.GetVisitAsync(read.Visit.Id.ToString(), cancellationToken) });
    }

    /// <summary>Takes the report off a lab visit; its bytes stay in the record.</summary>
    private static async Task<IResult> RemoveReport(
        string id, LabVisitService visits, MeasurementReader measurements, CancellationToken cancellationToken)
    {
        var read = await visits.RemoveReportAsync(id, cancellationToken);
        return Results.Ok(new LabVisitResult { Visit = await measurements.GetVisitAsync(read.Visit.Id.ToString(), cancellationToken) });
    }

    /// <summary>A visit's body as the service takes it, or a 400 naming the result with no value.</summary>
    private static LabVisitEntry? Entry(LabVisitBody body, out IResult? refusal)
    {
        var results = new List<LabResultEntry>();
        foreach (var result in body.Results ?? [])
        {
            if (result.Value is not { } value)
            {
                refusal = Results.BadRequest(new ApiError($"Give {result.Metric ?? "each test"}'s result, e.g. 104."));
                return null;
            }

            results.Add(new LabResultEntry(
                result.Metric ?? "", value, result.Low, result.High, result.Note, result.Unit, result.Reading));
        }

        refusal = null;
        return new LabVisitEntry(body.CollectedAt, body.Lab, body.Note, results);
    }

    /// <summary>Every metric, archived ones included, by name, with its count and newest reading.</summary>
    /// <summary>
    /// The starter list (measurements plan §2.1): each metric and lab test with the unit, decimals,
    /// direction and normal range making it fills in. Add a metric and the lab visit sheet read it
    /// here, so a retuned range reaches them with no web change (code review CR73).
    /// </summary>
    private static IResult GetCatalog() => Results.Ok(MeasurementRows.Catalog());

    private static async Task<IResult> GetMetrics(MeasurementReader measurements, CancellationToken cancellationToken)
        => Results.Ok(await measurements.GetMetricsAsync(cancellationToken));

    /// <summary>
    /// Adds a metric; a starter-list name (Weight, Systolic BP) fills in the unit, decimals, which way
    /// is better and the normal range.
    /// </summary>
    private static async Task<IResult> AddMetric(
        MetricBody body, MetricService metrics, MeasurementReader measurements, CancellationToken cancellationToken)
    {
        var metric = await metrics.AddAsync(
            body.Name ?? "", body.Unit, body.Decimals, body.Target, body.Better, body.Low, body.High,
            cancellationToken: cancellationToken);
        return Results.Ok(await measurements.GetMetricAsync(metric.Id, cancellationToken));
    }

    /// <summary>
    /// A metric (by id or name) over the last <paramref name="days"/> days (1–3,660; all its history
    /// when left out): readings newest first, day values with the trend, and the goal.
    /// </summary>
    private static async Task<IResult> GetMetric(
        string id, int? days, MeasurementReader measurements, CancellationToken cancellationToken)
        => Results.Ok(await measurements.GetDetailAsync(id, days, cancellationToken));

    /// <summary>Renames a metric (by id or name) or changes its unit, decimals, target, which way is better or normal range.</summary>
    private static async Task<IResult> SetMetric(
        string id, MetricBody body, MetricService metrics, MeasurementReader measurements, CancellationToken cancellationToken)
    {
        var metric = await metrics.SetAsync(
            id,
            new MetricChange(
                body.Name, body.Unit, body.Decimals, body.Target, body.ClearTarget ?? false, body.Better,
                body.Low, body.ClearLow ?? false, body.High, body.ClearHigh ?? false),
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

    /// <summary>
    /// Logs readings of several metrics taken together (blood pressure's two), at one time and with
    /// one note. All or nothing: a refusal of any writes none.
    /// </summary>
    private static async Task<IResult> LogReadings(
        MetricReadingBatchBody body, MetricReadingService readings, MeasurementReader measurements,
        CancellationToken cancellationToken)
    {
        var entries = new List<ReadingEntry>();
        foreach (var entry in body.Readings ?? [])
        {
            if (entry.Value is not { } value)
            {
                return Results.BadRequest(new ApiError($"Give {entry.Metric ?? "each metric"}'s value, e.g. 122."));
            }

            entries.Add(new ReadingEntry(entry.Metric ?? "", value));
        }

        var logged = await readings.LogManyAsync(entries, body.TakenAt, body.Note, cancellationToken);
        var rows = new List<MetricReadingRow>(logged.Count);
        foreach (var reading in logged)
        {
            rows.Add((await measurements.GetReadingAsync(reading.Id, cancellationToken))!);
        }

        return Results.Ok(new MetricReadingsResult { Readings = rows });
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

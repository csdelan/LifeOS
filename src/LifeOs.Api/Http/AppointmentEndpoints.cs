using System.Globalization;
using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;

namespace LifeOs.Api.Http;

/// <summary>
/// Appointments (CAL-1, D10): the day's appointments for Focus, and Materialize, the twin of
/// <c>bsk materialize</c>, which makes a repeating appointment's occurrences through a date so
/// each has its own status. Days are home-zone <c>yyyy-MM-dd</c>.
/// </summary>
public static class AppointmentEndpoints
{
    public static void MapAppointmentEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/appointments")
            .WithTags("Appointments")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("", GetDay).WithName("GetAppointments")
            .Produces<IReadOnlyList<AppointmentRow>>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/{id:guid}/materialize", Materialize).WithName("MaterializeAppointments")
            .Produces<MaterializeRow>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    /// <summary>The appointments on <paramref name="on"/>, today in the home zone by default.</summary>
    private static async Task<IResult> GetDay(
        string? on, SubjectReader reader, IHomeCalendar calendar, IClock clock, CancellationToken cancellationToken)
    {
        DateOnly day;
        if (string.IsNullOrWhiteSpace(on))
        {
            day = await calendar.TodayAsync(cancellationToken);
        }
        else if (!DateOnly.TryParseExact(on.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
        {
            return Results.BadRequest(new ApiError($"'{on}' isn't a day. Send it as yyyy-MM-dd."));
        }

        return Results.Ok((await reader.GetAppointmentsOnAsync(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken: cancellationToken)));
    }

    /// <summary>
    /// Makes a repeating appointment's occurrences through <c>through</c>. Dates already made are
    /// skipped, so running it again only extends the horizon.
    /// </summary>
    private static async Task<IResult> Materialize(
        Guid id, MaterializeBody body, AppointmentService appointments,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(body.Through?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var through))
        {
            return Results.BadRequest(new ApiError("through is required, as yyyy-MM-dd."));
        }

        var result = await appointments.MaterializeAsync(
            id.ToString(), through, cancellationToken);
        return Results.Ok(SubjectRows.From(result));
    }
}

public sealed class MaterializeBody
{
    /// <summary>The last day to make occurrences for, <c>yyyy-MM-dd</c>.</summary>
    public string? Through { get; set; }
}

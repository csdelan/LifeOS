using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Materializes the occurrences of a recurring appointment series (CAL-1 / D10). Each
/// occurrence is created as its own Appointment subject referencing the series
/// (<c>attributes.series</c>) and carrying its own date; it inherits the series'
/// template fields and gets its own editable status via the normal state_change path.
/// Materialization is idempotent — only dates not already materialized are created —
/// so it can be re-run to extend the horizon.
/// </summary>
public sealed class AppointmentService(SubjectService subjects, IAppointmentRepository appointments)
{
    public async Task<MaterializeResult> MaterializeAsync(
        string seriesReference, DateOnly through, CancellationToken cancellationToken = default)
    {
        var series = await subjects.ResolveAsync(seriesReference, cancellationToken);
        if (series.Type != SubjectTypes.Appointment)
        {
            throw new InvalidOperationException(
                $"'{series.Urn}' is a {series.Type}, not an Appointment series.");
        }

        var pending = await appointments.PendingOccurrenceDatesAsync(
            series.Id, series.Urn, through, cancellationToken);

        var created = new List<SubjectRef>(pending.Count);
        foreach (var date in pending)
        {
            var attributes = new JsonObject
            {
                ["series"] = series.Urn,
                ["date"] = date.ToString("yyyy-MM-dd")
            };
            var occurrence = await subjects.CreateAsync(
                SubjectTypes.Appointment, series.Title, attributes.ToJsonString(),
                cancellationToken: cancellationToken);
            created.Add(occurrence);
        }

        return new MaterializeResult(series, created);
    }
}

/// <summary>The outcome of materialization: the series and the occurrences newly created.</summary>
public sealed record MaterializeResult(SubjectRef Series, IReadOnlyList<SubjectRef> Created);

using System.Globalization;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Materializes the occurrences of a recurring appointment series (CAL-1 / D10). Each
/// occurrence is created as its own Appointment subject referencing the series
/// (<c>attributes.series</c>), standing for one date of its pattern (<c>attributes.occurrence</c>)
/// and carrying its own day (<c>attributes.date</c>); it inherits the series' template fields and
/// gets its own editable status via the normal state_change path. Materialization is idempotent
/// (only dates no occurrence stands for are made) so it can be re-run to extend the horizon.
/// It reaches at most <see cref="AppointmentSeries.HorizonYears"/> past today and makes at most
/// <see cref="AppointmentSeries.MaxPerRun"/> at a time, and two runs at once make each date once
/// (code review CR65).
/// </summary>
public sealed class AppointmentService(
    SubjectService subjects, IAppointmentRepository appointments, IHomeCalendar calendar)
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

        var furthest = (await calendar.TodayAsync(cancellationToken)).AddYears(AppointmentSeries.HorizonYears);
        if (through > furthest)
        {
            throw new ArgumentException(
                $"Occurrences are made at most {AppointmentSeries.HorizonYears} years ahead, through {Iso(furthest)}. "
                + $"Pick a day up to then, not {Iso(through)}.");
        }

        var made = await appointments.MaterializeAsync(series, through, AppointmentSeries.MaxPerRun, cancellationToken);
        if (made.Due > AppointmentSeries.MaxPerRun)
        {
            throw new ArgumentException(
                $"{series.Title} has {made.Due} occurrences to make through {Iso(through)}, and one run makes at most "
                + $"{AppointmentSeries.MaxPerRun}. Pick an earlier day, then run it again.");
        }

        return new MaterializeResult(series, made.Created);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>The outcome of materialization: the series and the occurrences newly created.</summary>
public sealed record MaterializeResult(SubjectRef Series, IReadOnlyList<SubjectRef> Created);

using System.Globalization;
using System.Text.Json.Nodes;

namespace LifeOs.Domain;

/// <summary>
/// The shape of a repeating Appointment (CAL-1, D10). A <b>series</b> is an Appointment carrying
/// <c>attributes.recurrence</c>; each <b>occurrence</b> is its own Appointment carrying
/// <c>attributes.series</c> (the series' urn), <c>attributes.occurrence</c> (the date it stands
/// for, the series' slot) and <c>attributes.date</c> (the day it's on, which Chris can move).
/// One occurrence per (series, occurrence): migration 0064's unique index (code review CR65).
/// </summary>
public static class AppointmentSeries
{
    /// <summary>On an occurrence: the urn of the series it belongs to.</summary>
    public const string SeriesKey = "series";

    /// <summary>On an occurrence: the date it stands for (<c>yyyy-MM-dd</c>). Occurrences made before CR65 have none, and stand for their <see cref="DateKey"/>.</summary>
    public const string OccurrenceKey = "occurrence";

    /// <summary>On any Appointment: the day it's on (<c>yyyy-MM-dd</c>).</summary>
    public const string DateKey = "date";

    /// <summary>The furthest ahead one Materialize reaches, in years from today at home.</summary>
    public const int HorizonYears = 2;

    /// <summary>The most occurrences one Materialize makes: two years of a daily series.</summary>
    public const int MaxPerRun = 750;

    /// <summary>A new occurrence's attributes: its series, the date it stands for, and that date as its day.</summary>
    public static string OccurrenceAttributes(string seriesUrn, DateOnly date)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new JsonObject
        {
            [SeriesKey] = seriesUrn,
            [OccurrenceKey] = day,
            [DateKey] = day,
        }.ToJsonString();
    }
}

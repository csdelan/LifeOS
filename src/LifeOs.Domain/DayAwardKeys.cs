using System.Globalization;

namespace LifeOs.Domain;

/// <summary>
/// Awards paid once a day each, keyed <c>&lt;trigger&gt;:yyyy-MM-dd</c>: the Food Log's fast and diet
/// (<see cref="FoodAwards"/>) and the Activity page's steps, exercise and sleep
/// (<see cref="ActivityAwards"/>). One spelling of the key for both (code review CR78).
/// </summary>
public static class DayAwardKeys
{
    /// <summary>The key for <paramref name="trigger"/> on <paramref name="day"/>.</summary>
    public static string Key(string trigger, DateOnly day)
        => $"{trigger}:{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>The trigger and day of <paramref name="awardKey"/> when its trigger is one of <paramref name="triggers"/>; else null.</summary>
    public static (string Trigger, DateOnly Day)? Parse(string awardKey, IReadOnlyCollection<string> triggers)
    {
        var separator = awardKey.IndexOf(':');
        return separator > 0
               && triggers.Contains(awardKey[..separator])
               && DateOnly.TryParseExact(
                   awardKey[(separator + 1)..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? (awardKey[..separator], day)
            : null;
    }

    /// <summary>Every key of <paramref name="triggers"/> on each day from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static IReadOnlyList<string> Keys(IReadOnlyCollection<string> triggers, DateOnly from, DateOnly to)
    {
        var keys = new List<string>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            keys.AddRange(triggers.Select(trigger => Key(trigger, day)));
        }

        return keys;
    }
}

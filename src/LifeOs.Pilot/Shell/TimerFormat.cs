using System.Globalization;
using System.Text.RegularExpressions;

namespace LifeOs.Pilot.Shell;

/// <summary>
/// TASKS-2 timer read-outs, matching the web's <c>timer.ts</c>: the stopwatch clock,
/// recorded-time summaries, and reading a Task's free-text estimate as seconds.
/// Kept free of WinForms so tests can compile it.
/// </summary>
internal static partial class TimerFormat
{
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Stopped = "stopped";

    /// <summary>Stopwatch format: <c>m:ss</c> under an hour, <c>h:mm:ss</c> from there.</summary>
    public static string Clock(long seconds)
    {
        var s = Math.Max(0, seconds);
        var h = s / 3600;
        var m = s % 3600 / 60;
        var sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    /// <summary>Recorded-time format: <c>45s</c>, <c>25m</c>, <c>1h 20m</c>, <c>3h</c>.</summary>
    public static string Tracked(long seconds)
    {
        var s = Math.Max(0, seconds);
        if (s < 60)
        {
            return $"{s}s";
        }

        var h = s / 3600;
        var m = s % 3600 / 60;
        if (h == 0)
        {
            return $"{m}m";
        }

        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }

    /// <summary>
    /// The line under the timer: time recorded, the session count once stopped, and
    /// how it compares with the estimate when one parses.
    /// </summary>
    public static string Summary(long totalSeconds, long sessions, bool showSessions, long? estimateSeconds)
    {
        var parts = new List<string>
        {
            totalSeconds > 0 ? $"{Tracked(totalSeconds)} recorded" : "No time recorded yet"
        };
        if (showSessions && sessions > 1)
        {
            parts.Add($"{sessions} sessions");
        }

        if (estimateSeconds is long estimate && estimate > 0)
        {
            var left = estimate - totalSeconds;
            parts.Add(left >= 0
                ? $"{Tracked(estimate)} estimated · {Tracked(left)} left"
                : $"{Tracked(estimate)} estimated · {Tracked(-left)} over");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Read a free-text estimate (the Task's <c>estimated_duration</c>) as seconds:
    /// <c>90m</c>, <c>1h</c>, <c>1h 30m</c>, <c>1.5h</c>, <c>45 min</c>, <c>2 hours</c>,
    /// or a bare number of minutes. Null when the text is not a duration.
    /// </summary>
    public static long? ParseEstimateSeconds(string? text)
    {
        var raw = (text ?? "").Trim().ToLowerInvariant();
        if (raw.Length == 0)
        {
            return null;
        }

        if (BareMinutes().IsMatch(raw))
        {
            return (long)Math.Round(double.Parse(raw, CultureInfo.InvariantCulture) * 60);
        }

        double seconds = 0;
        var matched = 0;
        foreach (Match part in Unit().Matches(raw))
        {
            var value = double.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture);
            seconds += part.Groups[2].Value.StartsWith('h') ? value * 3600 : value * 60;
            matched += part.Value.Count(c => !char.IsWhiteSpace(c));
        }

        // Every non-space character must belong to a recognized part.
        if (matched == 0 || matched != raw.Count(c => !char.IsWhiteSpace(c)))
        {
            return null;
        }

        return (long)Math.Round(seconds);
    }

    /// <summary>Recorded and session seconds plus the running segment, as of <paramref name="utcNow"/>.</summary>
    public static (long Total, long Session) Live(
        string state, DateTime? runningSinceUtc, long totalSeconds, long sessionSeconds, DateTime utcNow)
    {
        var running = state == Running && runningSinceUtc is { } since
            ? Math.Max(0, (long)(utcNow - since).TotalSeconds)
            : 0;
        return (totalSeconds + running, sessionSeconds + running);
    }

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex BareMinutes();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(h|hr|hrs|hour|hours|m|min|mins|minute|minutes)\b")]
    private static partial Regex Unit();
}

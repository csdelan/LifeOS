using LifeOs.Pilot.Shell;

namespace LifeOs.Tests;

/// <summary>The Pilot's timer read-outs match the web's <c>timer.ts</c> (TASKS-2).</summary>
public sealed class TimerFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void Clock_is_a_stopwatch(long seconds, string expected)
        => Assert.Equal(expected, TimerFormat.Clock(seconds));

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(1500, "25m")]
    [InlineData(4800, "1h 20m")]
    [InlineData(10800, "3h")]
    public void Tracked_is_compact(long seconds, string expected)
        => Assert.Equal(expected, TimerFormat.Tracked(seconds));

    [Theory]
    [InlineData("90m", 5400)]
    [InlineData("1h 30m", 5400)]
    [InlineData("1.5h", 5400)]
    [InlineData("45 min", 2700)]
    [InlineData("2 hours", 7200)]
    [InlineData("30", 1800)]
    public void Estimates_parse_as_seconds(string text, long expected)
        => Assert.Equal(expected, TimerFormat.ParseEstimateSeconds(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("1h then some")]
    public void Non_durations_are_not_estimates(string? text)
        => Assert.Null(TimerFormat.ParseEstimateSeconds(text));

    [Fact]
    public void Summary_compares_recorded_time_with_the_estimate()
    {
        Assert.Equal("No time recorded yet", TimerFormat.Summary(0, 0, showSessions: true, null));
        Assert.Equal(
            "25m recorded · 2 sessions · 1h 30m estimated · 1h 5m left",
            TimerFormat.Summary(1500, 2, showSessions: true, 5400));
        Assert.Equal(
            "2h recorded · 1h 30m estimated · 30m over",
            TimerFormat.Summary(7200, 2, showSessions: false, 5400));
    }

    [Fact]
    public void Live_adds_only_a_running_segment()
    {
        var since = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        var now = since.AddMinutes(5);
        Assert.Equal((900L, 360L), TimerFormat.Live(TimerFormat.Running, since, 600, 60, now));
        Assert.Equal((600L, 60L), TimerFormat.Live(TimerFormat.Paused, null, 600, 60, now));
        // A clock behind the server never shows negative time.
        Assert.Equal((600L, 60L), TimerFormat.Live(TimerFormat.Running, now, 600, 60, since));
    }
}

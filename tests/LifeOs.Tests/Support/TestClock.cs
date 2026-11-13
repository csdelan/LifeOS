using LifeOs.Application.Abstractions;

namespace LifeOs.Tests;

/// <summary>
/// The one clock tests set and move. Fixed unless made to tick: <see cref="Ticking"/> moves on a
/// second at every read, so events written one after another get distinct, ordered times.
/// </summary>
internal sealed class TestClock(DateTimeOffset start, TimeSpan tick = default) : IClock
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = start;

    /// <summary>A clock a second further on at every read, from <paramref name="start"/> (else now).</summary>
    public static TestClock Ticking(DateTimeOffset? start = null)
        => new(start ?? DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                var current = _now;
                _now += tick;
                return current;
            }
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }
    }

    public void Set(DateTimeOffset at)
    {
        lock (_gate)
        {
            _now = at;
        }
    }

    /// <summary>Noon UTC on <paramref name="day"/>.</summary>
    public void On(DateOnly day) => Set(new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));
}

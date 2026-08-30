using LifeOs.Application.Abstractions;

namespace LifeOs.Infrastructure;

/// <summary>The real wall clock, in UTC.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

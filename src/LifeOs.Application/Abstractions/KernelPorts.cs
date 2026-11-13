using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// The clock, the home calendar and the event log every service writes through.

/// <summary>A clock, so services can be tested with a fixed time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// The calendar in the home zone (<see cref="HomeZone"/>, <c>Rewards:HomeTimeZone</c>): what
/// day it is where Chris lives, whatever zone the server or the database runs in. The
/// solution runs with invariant globalization, so the database does the zone math.
/// </summary>
public interface IHomeCalendar
{
    /// <summary>
    /// The date in the home zone at <paramref name="instant"/>. Throws
    /// <see cref="InvalidOperationException"/> for a zone the database doesn't know.
    /// </summary>
    Task<DateOnly> DateAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Today in the home zone, by the kernel's <see cref="IClock"/>: the one way the kernel asks
    /// what day it is (code review CR42), so a test's clock moves "today" for every service. A
    /// view that says "today" (<c>bsk.home_today()</c>) reads the database's clock instead, which
    /// a test can't move.
    /// </summary>
    Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default);
}

/// <summary>Appends events to the source stream. The only write path for events.</summary>
public interface IEventStore
{
    Task<Guid> AppendAsync(NewEvent newEvent, CancellationToken cancellationToken = default);
}

/// <summary>Reads source events. Read-only; the source stream is never mutated.</summary>
public interface IEventReader
{
    Task<SourceEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}

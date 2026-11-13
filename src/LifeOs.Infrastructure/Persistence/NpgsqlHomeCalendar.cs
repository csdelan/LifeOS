using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The home-zone calendar (<see cref="IHomeCalendar"/>): Postgres reads an instant in
/// <paramref name="homeTimeZone"/> (an IANA name, <see cref="HomeZone"/>), since .NET runs
/// with invariant globalization and can't. A zone the database doesn't know fails loudly, and so
/// does one that isn't the database's own <c>bsk.home_zone</c> (<see cref="HomeZoneCheck"/>),
/// read in the same round trip. Today is the date at <paramref name="clock"/>'s now.
/// </summary>
public sealed class NpgsqlHomeCalendar(NpgsqlDataSource dataSource, IClock clock, string homeTimeZone = HomeZone.Default)
    : IHomeCalendar
{
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
        => DateAtAsync(clock.UtcNow, cancellationToken);

    public async Task<DateOnly> DateAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            // Text, not a date: Dapper has no DateOnly mapping in every host.
            var row = await connection.QuerySingleAsync<(string Day, string Zone)>(new CommandDefinition(
                "SELECT to_char((@instant AT TIME ZONE @tz)::date, 'YYYY-MM-DD'), bsk.home_zone();",
                new { instant = instant.ToUniversalTime(), tz = homeTimeZone },
                cancellationToken: cancellationToken));
            HomeZoneCheck.Ensure(homeTimeZone, row.Zone);
            return DateOnly.ParseExact(row.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InvalidParameterValue)
        {
            throw UnknownZone(homeTimeZone, e);
        }
    }

    /// <summary>The error for a home zone Postgres doesn't know, naming the setting to fix.</summary>
    internal static InvalidOperationException UnknownZone(string homeTimeZone, PostgresException e)
        => new(
            $"{HomeZone.ConfigurationKey} '{homeTimeZone}' is not a time zone the database knows; use an IANA name such as {HomeZone.Default}.",
            e);
}

using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The home-zone calendar (<see cref="IHomeCalendar"/>): Postgres reads an instant in
/// <paramref name="homeTimeZone"/> (an IANA name, <see cref="HomeZone"/>), since .NET runs
/// with invariant globalization and can't. A zone the database doesn't know fails loudly.
/// </summary>
public sealed class NpgsqlHomeCalendar(string connectionString, string homeTimeZone = HomeZone.Default) : IHomeCalendar
{
    public async Task<DateOnly> DateAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            // Text, not a date: Dapper has no DateOnly mapping in every host.
            var day = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT to_char((@instant AT TIME ZONE @tz)::date, 'YYYY-MM-DD');",
                new { instant = instant.ToUniversalTime(), tz = homeTimeZone },
                cancellationToken: cancellationToken));
            return DateOnly.ParseExact(day!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
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

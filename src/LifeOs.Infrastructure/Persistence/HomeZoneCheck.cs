using Dapper;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The home zone is set in two places that must agree (code review CR22): the app's
/// <see cref="HomeZone.ConfigurationKey"/>, which dates rewards and check-offs, and the
/// database's <c>bsk.home_zone</c> (migration 0033), which the habit, food, measurement and home
/// views read "today" in. If they disagree, a habit day and the XP credited for it land on
/// different dates, so the API refuses to start and the calendar refuses to answer.
/// </summary>
public static class HomeZoneCheck
{
    /// <summary>Throws when the database's home zone isn't <paramref name="configured"/>.</summary>
    public static async Task EnsureAgreesAsync(
        NpgsqlDataSource dataSource, string configured, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var database = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT bsk.home_zone();", cancellationToken: cancellationToken));
        Ensure(configured, database);
    }

    /// <summary>Throws when <paramref name="database"/>, the database's <c>bsk.home_zone()</c>, isn't <paramref name="configured"/>.</summary>
    public static void Ensure(string configured, string? database)
    {
        if (!string.Equals(configured, database, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The home zone is set two ways that disagree: {HomeZone.ConfigurationKey} is '{configured}' and the "
                + $"database's bsk.home_zone is '{database}'. Make them the same: set {HomeZone.ConfigurationKey}, or run "
                + $"ALTER DATABASE <db> SET bsk.home_zone = '<zone>' (docs/pilot/deploy.md).");
        }
    }
}

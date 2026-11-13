using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Api.Hosting;

/// <summary>
/// Refuses to start the API when <c>Rewards:HomeTimeZone</c> and the database's
/// <c>bsk.home_zone</c> disagree (<see cref="HomeZoneCheck"/>, code review CR22), so a deploy with
/// the two set apart fails rather than filing days on two calendars. A database it can't reach,
/// or one not yet migrated to 0033, is only logged: the API still starts, and the calendar makes
/// the same check on every use.
/// </summary>
internal sealed class HomeZoneStartupCheck(
    NpgsqlDataSource dataSource, string homeTimeZone, ILogger<HomeZoneStartupCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await HomeZoneCheck.EnsureAgreesAsync(dataSource, homeTimeZone, cancellationToken);
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException)
        {
            logger.LogWarning(e, "Couldn't check the home zone against the database's bsk.home_zone at startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

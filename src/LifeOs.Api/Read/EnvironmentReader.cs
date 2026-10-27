using LifeOs.Application.Abstractions;
using LifeOs.Application.Home;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;

namespace LifeOs.Api.Read;

/// <summary>
/// The home's reads (HOME-1 to HOME-3, environment plan §4.7) on the <c>bsk_reader</c>
/// connection, like every other API read: the spaces and ratings through <c>bsk.v_space</c> and
/// <c>bsk.v_space_rating</c>, and the Home Score through <see cref="HomeService"/> on the
/// reader's own ledger repository, on the days of <paramref name="homeTimeZone"/>. No writes live
/// here; they go through <see cref="SpaceService"/> and <see cref="SpaceRatingService"/>.
/// </summary>
public sealed class EnvironmentReader(string readerConnectionString, IClock clock, string homeTimeZone = HomeZone.Default)
{
    private readonly NpgsqlSpaceRepository spaces = new(readerConnectionString);
    private readonly NpgsqlHomeCalendar calendar = new(readerConnectionString, homeTimeZone);
    private readonly HomeService home = new(new NpgsqlLedgerRepository(readerConnectionString, homeTimeZone));

    /// <summary>The home today: the Home Score, each space and part, and Fix next (<c>GET /api/environment</c>).</summary>
    public async Task<EnvironmentRead> GetHomeAsync(CancellationToken cancellationToken = default)
        => EnvironmentRows.From(await home.GetAsync(cancellationToken));

    /// <summary>
    /// The Home Score on each of the last <paramref name="days"/> days (<c>GET /api/environment/days</c>).
    /// Throws <see cref="ArgumentException"/> for a count outside 1 to <see cref="SpaceRatingService.MaxDays"/>.
    /// </summary>
    public async Task<HomeDaysRead> GetHomeDaysAsync(int days, CancellationToken cancellationToken = default)
        => EnvironmentRows.From(await home.GetDaysAsync(days, cancellationToken));

    /// <summary>Every space, archived ones included, by name.</summary>
    public async Task<IReadOnlyList<SpaceRow>> GetSpacesAsync(CancellationToken cancellationToken = default)
    {
        var all = await spaces.GetSpacesAsync(cancellationToken);
        var total = EnvironmentRows.TotalWeight(all);
        return all.Select(s => EnvironmentRows.From(s, total)).ToList();
    }

    /// <summary>
    /// The ratings among the last <paramref name="days"/> (today included), of the space with id
    /// <paramref name="space"/> only when given, newest first. Throws
    /// <see cref="ArgumentException"/> for a count outside 1 to <see cref="SpaceRatingService.MaxDays"/>.
    /// </summary>
    public async Task<SpaceRatingsRead> GetRatingsAsync(
        Guid? space, int days, CancellationToken cancellationToken = default)
    {
        SpaceRatingService.Check(days);
        var today = await calendar.DateAtAsync(clock.UtcNow, cancellationToken);
        var from = today.AddDays(1 - days);
        var all = await spaces.GetSpacesAsync(cancellationToken);
        var ratings = await spaces.GetRatingsAsync(space, from, cancellationToken);
        return new SpaceRatingsRead
        {
            Today = EnvironmentRows.Day(today),
            From = EnvironmentRows.Day(from),
            Ratings = ratings
                .OrderByDescending(r => r.Day).ThenByDescending(r => r.RatedAt)
                .Select(r => EnvironmentRows.From(r, all))
                .ToList()
        };
    }

    /// <summary>Every space, for naming the spaces in a write's answer.</summary>
    public Task<IReadOnlyList<Space>> GetAllSpacesAsync(CancellationToken cancellationToken = default)
        => spaces.GetSpacesAsync(cancellationToken);
}

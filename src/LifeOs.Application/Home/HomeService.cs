using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Home;

/// <summary>
/// The home now (HOME-3): today in the home zone, the last settled day, today's
/// <see cref="HomeDay"/> (the Home Score, each space and part), the spaces not archived, and the
/// three parts that cost the score most (<see cref="FixNext"/>).
/// </summary>
public sealed record HomeReading(
    DateOnly Today,
    DateOnly SettledThrough,
    HomeDay Day,
    IReadOnlyList<Space> Spaces,
    IReadOnlyList<(Guid SpaceId, PartValue Part)> FixNext)
{
    /// <summary>Today's Home Score in ten-thousandths of a point; null before any space counts.</summary>
    public long? Score => Day.Score;

    /// <summary>A space's day, or null when it isn't in today's (it's archived).</summary>
    public SpaceDay? Of(Guid spaceId) => Day.Spaces.FirstOrDefault(s => s.SpaceId == spaceId);
}

/// <summary>
/// The home over the last <see cref="Days"/> days (HOME-3), oldest first, today included, with
/// every space (archived ones too, to name the old days) and the exception days' comments.
/// </summary>
public sealed record HomeSeries(
    DateOnly Today,
    DateOnly SettledThrough,
    int Count,
    IReadOnlyList<HomeDay> Days,
    IReadOnlyList<Space> Spaces,
    IReadOnlyDictionary<DateOnly, string> Exceptions);

/// <summary>
/// Reads the Home Score (HOME-3, D18, environment plan §4.4, §4.7) behind <c>bsk home</c> and
/// <c>GET /api/environment</c>: the spaces, ratings, exception days and today, read together
/// with the life-domain credit (<see cref="ILedgerRepository.GetDomainDaysAsync"/>), so the
/// Home Score shown is the one Environment's level is held by. Nothing is stored.
/// </summary>
public sealed class HomeService(ILedgerRepository ledger)
{
    /// <summary>How many parts Fix next names.</summary>
    public const int FixNextCount = 3;

    /// <summary>The home today.</summary>
    public async Task<HomeReading> GetAsync(CancellationToken cancellationToken = default)
    {
        var credit = await ledger.GetDomainDaysAsync([], cancellationToken);
        var day = HomeScore.On(credit.Home, credit.ExceptionDays.Select(d => d.Day), credit.Today);
        return new HomeReading(
            credit.Today,
            credit.SettledThrough,
            day,
            credit.Home.Spaces.Where(s => !s.Archived).ToList(),
            HomeScore.FixNext(day, FixNextCount));
    }

    /// <summary>
    /// The home on each of the last <paramref name="days"/> days, today included. Throws
    /// <see cref="ArgumentException"/> for a count outside 1 to <see cref="SpaceRatingService.MaxDays"/>.
    /// </summary>
    public async Task<HomeSeries> GetDaysAsync(int days, CancellationToken cancellationToken = default)
    {
        SpaceRatingService.Check(days);
        var credit = await ledger.GetDomainDaysAsync([], cancellationToken);
        var from = credit.Today.AddDays(1 - days);
        var series = HomeScore.Days(
            credit.Home.Spaces, credit.Home.Ratings, credit.ExceptionDays.Select(d => d.Day), from, credit.Today);
        return new HomeSeries(
            credit.Today,
            credit.SettledThrough,
            days,
            series,
            credit.Home.Spaces,
            credit.ExceptionDays.Where(d => d.Day >= from).ToDictionary(d => d.Day, d => d.Comment));
    }
}

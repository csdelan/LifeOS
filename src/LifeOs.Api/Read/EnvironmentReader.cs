using LifeOs.Application.Abstractions;
using LifeOs.Application.Home;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;

namespace LifeOs.Api.Read;

/// <summary>
/// The home's reads (HOME-1 to HOME-4, environment plan §4.7, §8.3) on the <c>bsk_reader</c>
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

    /// <summary>
    /// The home today: the Home Score, each space and part, Fix next, and each space's shots with
    /// Then and Now (<c>GET /api/environment</c>).
    /// </summary>
    public async Task<EnvironmentRead> GetHomeAsync(CancellationToken cancellationToken = default)
        => EnvironmentRows.From(
            await home.GetAsync(cancellationToken),
            await spaces.GetSpacesAsync(cancellationToken),
            await spaces.GetPhotosAsync(cancellationToken: cancellationToken));

    /// <summary>
    /// A space's photos (<c>GET /api/environment/spaces/{id}/photos</c>), of one shot when
    /// <paramref name="shot"/> names it, newest first, each with the space's score on its day.
    /// Throws <see cref="SubjectNotFoundException"/> for an unknown space and
    /// <see cref="ArgumentException"/> for an unknown shot.
    /// </summary>
    public async Task<SpacePhotosRead> GetSpacePhotosAsync(
        string space, string? shot, CancellationToken cancellationToken = default)
    {
        var target = await FindAsync(space, cancellationToken);
        var shots = string.IsNullOrWhiteSpace(shot) ? target.Shots : [SpaceService.ResolveShot(target, shot)];
        var photos = await spaces.GetPhotosAsync(target.Id, cancellationToken);
        var on = EnvironmentRows.Days(await home.GetAsync(cancellationToken));
        var baselines = target.Shots.Select(s => ShotPhotos.Baseline(s, photos)?.Id).OfType<Guid>().ToHashSet();
        return new SpacePhotosRead
        {
            SpaceId = target.Id,
            Space = target.Name,
            Shots = EnvironmentRows.Shots(target, photos, on),
            Photos = photos
                .Where(p => shots.Any(s => string.Equals(s.Id, p.ShotId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => p.Day).ThenByDescending(p => p.AddedAt).ThenByDescending(p => p.Id)
                .Select(p => EnvironmentRows.Photo(p, target, baselines.Contains(p.Id) ? p.Id : null, on))
                .ToList()
        };
    }

    /// <summary>
    /// A photo or shot write's answer: <paramref name="photo"/> as it now reads (none when null or
    /// removed) and the space's shots with Then and Now.
    /// </summary>
    public async Task<SpacePhotoResult> PhotoResultAsync(
        Guid spaceId, Guid? photo, CancellationToken cancellationToken = default)
    {
        var space = await spaces.GetSpaceAsync(spaceId, cancellationToken)
                    ?? throw new SubjectNotFoundException(spaceId.ToString());
        var photos = await spaces.GetPhotosAsync(space.Id, cancellationToken);
        var on = EnvironmentRows.Days(await home.GetAsync(cancellationToken));
        var shots = EnvironmentRows.Shots(space, photos, on);
        var read = photo is { } id ? photos.FirstOrDefault(p => p.Id == id) : null;
        return new SpacePhotoResult
        {
            Photo = read is null
                ? null
                : EnvironmentRows.Photo(read, space, shots.FirstOrDefault(s => s.Baseline?.Id == read.Id)?.Baseline?.Id, on),
            SpaceId = space.Id,
            Shots = shots
        };
    }

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

    // A space by its id or its name (a space not archived first), as SpaceService resolves it.
    private async Task<Space> FindAsync(string reference, CancellationToken cancellationToken)
    {
        var wanted = reference.Trim();
        var all = await spaces.GetSpacesAsync(cancellationToken);
        var found = Guid.TryParse(wanted, out var id)
            ? all.FirstOrDefault(s => s.Id == id)
            : all.Where(s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Archived)
                .FirstOrDefault();
        return found ?? throw new SubjectNotFoundException(reference);
    }
}

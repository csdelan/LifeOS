using System.Globalization;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Application.Home;

/// <summary>
/// One space's rating in a walk-through or a quick check (HOME-2): the space by name or id, any
/// of the three parts (1–5 in halves), and an optional note. <paramref name="Same"/> confirms the
/// space's last rating instead, so it takes no parts.
/// </summary>
public sealed record RatingInput(
    string Space,
    decimal? Tidiness = null,
    decimal? Cleanliness = null,
    decimal? Aesthetics = null,
    string? Note = null,
    bool Same = false);

/// <summary>
/// A change to one rating: a new day and any parts to replace. <paramref name="Note"/> null keeps
/// the note and blank removes it. Parts not given keep their value.
/// </summary>
public sealed record RatingEdit(
    DateOnly? Day = null,
    decimal? Tidiness = null,
    decimal? Cleanliness = null,
    decimal? Aesthetics = null,
    string? Note = null);

/// <summary>What a rating write left: the ratings as they now read (none after a delete) and what it paid or moved.</summary>
public sealed record RatingWriteResult(IReadOnlyList<SpaceRating> Ratings, Earned Earned);

/// <summary>Today in the home zone and the ratings among the last <see cref="Days"/>, newest first.</summary>
public sealed record RatingList(DateOnly Today, int Days, IReadOnlyList<SpaceRating> Ratings);

/// <summary>
/// Ratings of the home's spaces (HOME-2, D18, environment plan §4.2), behind <c>bsk rate</c> and
/// <c>/api/environment/ratings</c>. A rating is a <c>declared</c> <c>measurement</c> event,
/// <c>{space_rating: {id, space, day, tidiness?, cleanliness?, aesthetics?, note?}}</c>, with a
/// <c>concerns</c> edge to the space; an edit writes a newer version with the same id, and a
/// delete writes <c>{id, voided: true}</c>. A walk-through saves many spaces in one write.
/// <para>
/// A rating can move Environment's level, either way (a backdated one re-reads past days), so
/// every write runs between two folds of the life domains and answers <c>progress</c>, as an
/// exception day does. Days are today or earlier in the home zone. Refusals are in words, with
/// nothing written.
/// </para>
/// </summary>
public sealed class SpaceRatingService(
    ISpaceRepository spaces, SpaceService spaceService, RewardService rewards, IHomeCalendar calendar, IClock clock,
    string sourceId)
{
    /// <summary>How far back a list reads by default.</summary>
    public const int DefaultDays = 90;

    /// <summary>The longest list: two years.</summary>
    public const int MaxDays = 731;

    /// <summary>
    /// Rates each space in <paramref name="ratings"/> on <paramref name="day"/> (today in the home
    /// zone when null), in one write. Refused, with nothing written, for a day after today, a
    /// space that's archived, unknown or named twice, a rating with no parts, a part outside 1–5
    /// or not in halves, an overlong note, or "same as last time" for a space never rated.
    /// </summary>
    public async Task<RatingWriteResult> RateAsync(
        DateOnly? day, IReadOnlyList<RatingInput> ratings, CancellationToken cancellationToken = default)
    {
        if (ratings is null || ratings.Count == 0)
        {
            throw new ArgumentException("Rate at least one space.");
        }

        var now = clock.UtcNow;
        var today = await calendar.DateAtAsync(now, cancellationToken);
        var on = CheckDay(day ?? today, today);
        var history = await spaces.GetRatingsAsync(cancellationToken: cancellationToken);

        var planned = new List<(Space Space, Guid Id, decimal? T, decimal? C, decimal? A, string? Note)>();
        foreach (var input in ratings)
        {
            var space = await spaceService.ResolveAsync(input.Space, cancellationToken);
            if (space.Archived)
            {
                throw new InvalidOperationException($"'{space.Name}' is archived: bring it back before rating it.");
            }

            if (planned.Any(p => p.Space.Id == space.Id))
            {
                throw new ArgumentException($"'{space.Name}' is rated twice: rate each space once.");
            }

            decimal? t, c, a;
            if (input.Same)
            {
                if (input.Tidiness is not null || input.Cleanliness is not null || input.Aesthetics is not null)
                {
                    throw new ArgumentException(
                        $"'Same as last time' repeats {space.Name}'s last rating, so it takes no new ones: leave them out.");
                }

                var last = history.Where(r => r.SpaceId == space.Id && r.Day <= on).ToList();
                t = LastOf(last, HomeParts.Tidiness);
                c = LastOf(last, HomeParts.Cleanliness);
                a = LastOf(last, HomeParts.Aesthetics);
                if (t is null && c is null && a is null)
                {
                    throw new InvalidOperationException(
                        $"'{space.Name}' hasn't been rated by {Text(on)}, so there's nothing to repeat: rate it instead.");
                }
            }
            else
            {
                t = Check(input.Tidiness, HomeParts.Tidiness);
                c = Check(input.Cleanliness, HomeParts.Cleanliness);
                a = Check(input.Aesthetics, HomeParts.Aesthetics);
                if (t is null && c is null && a is null)
                {
                    throw new ArgumentException(
                        $"Rate at least one part of '{space.Name}': tidiness, cleanliness or aesthetics.");
                }
            }

            planned.Add((space, Guid.NewGuid(), t, c, a, SpaceLimits.CheckNote(input.Note)));
        }

        var occurredAt = await OccurredAtAsync(on, today, now, cancellationToken);
        var writes = planned
            .Select(p => new SpaceRatingWrite(
                Declared(occurredAt, now, Payload(p.Id, p.Space.Id, on, p.T, p.C, p.A, p.Note)), p.Space.Id))
            .ToList();
        var earned = await rewards.ChangeDomainInputsAsync(
            async ct => await spaces.AppendRatingsAsync(writes, ct), cancellationToken);
        return new RatingWriteResult(await ReadAsync(planned.Select(p => p.Id), cancellationToken), earned);
    }

    /// <summary>
    /// Changes a rating: its day and any of its parts or its note. Changing nothing writes nothing.
    /// Refused like <see cref="RateAsync"/>, and for a rating that doesn't exist.
    /// </summary>
    public async Task<RatingWriteResult> EditAsync(
        string reference, RatingEdit edit, CancellationToken cancellationToken = default)
    {
        var rating = await ResolveAsync(reference, cancellationToken);
        var now = clock.UtcNow;
        var today = await calendar.DateAtAsync(now, cancellationToken);
        var on = CheckDay(edit.Day ?? rating.Day, today);
        var t = Check(edit.Tidiness, HomeParts.Tidiness) ?? rating.Tidiness;
        var c = Check(edit.Cleanliness, HomeParts.Cleanliness) ?? rating.Cleanliness;
        var a = Check(edit.Aesthetics, HomeParts.Aesthetics) ?? rating.Aesthetics;
        var note = edit.Note is null ? rating.Note : SpaceLimits.CheckNote(edit.Note);
        if (on == rating.Day && t == rating.Tidiness && c == rating.Cleanliness && a == rating.Aesthetics && note == rating.Note)
        {
            return new RatingWriteResult([rating], Earned.Nothing);
        }

        var space = await spaces.GetSpaceAsync(rating.SpaceId, cancellationToken)
                    ?? throw new InvalidOperationException("That rating's space no longer exists.");
        var occurredAt = await OccurredAtAsync(on, today, now, cancellationToken);
        var write = new SpaceRatingWrite(
            Declared(occurredAt, now, Payload(rating.Id, space.Id, on, t, c, a, note)), space.Id);
        var earned = await rewards.ChangeDomainInputsAsync(
            async ct => await spaces.AppendRatingsAsync([write], ct), cancellationToken);
        return new RatingWriteResult(await ReadAsync([rating.Id], cancellationToken), earned);
    }

    /// <summary>Deletes a rating: a voided version, so the record keeps what it was.</summary>
    public async Task<RatingWriteResult> DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var rating = await ResolveAsync(reference, cancellationToken);
        var now = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [SpaceLimits.RatingKey] = new Dictionary<string, object> { ["id"] = rating.Id.ToString(), ["voided"] = true }
        });
        var write = new SpaceRatingWrite(Declared(now, now, payload), rating.SpaceId);
        var earned = await rewards.ChangeDomainInputsAsync(
            async ct => await spaces.AppendRatingsAsync([write], ct), cancellationToken);
        return new RatingWriteResult([], earned);
    }

    /// <summary>
    /// The ratings among the last <paramref name="days"/> (today included), of
    /// <paramref name="space"/> only when given, newest first. Throws
    /// <see cref="ArgumentException"/> for a count outside 1 to <see cref="MaxDays"/>.
    /// </summary>
    public async Task<RatingList> ListAsync(
        string? space = null, int days = DefaultDays, CancellationToken cancellationToken = default)
    {
        Check(days);
        var today = await calendar.DateAtAsync(clock.UtcNow, cancellationToken);
        Guid? spaceId = string.IsNullOrWhiteSpace(space) ? null : (await spaceService.ResolveAsync(space, cancellationToken)).Id;
        var ratings = await spaces.GetRatingsAsync(spaceId, today.AddDays(1 - days), cancellationToken);
        return new RatingList(
            today, days, ratings.OrderByDescending(r => r.Day).ThenByDescending(r => r.RatedAt).ToList());
    }

    /// <summary>A live rating named by its id or the start of it (at least 4 hex digits).</summary>
    public async Task<SpaceRating> ResolveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var wanted = reference?.Trim() ?? "";
        if (Guid.TryParse(wanted, out var id))
        {
            return await spaces.GetRatingAsync(id, cancellationToken)
                   ?? throw new InvalidOperationException($"No rating has the id {id} (it may have been deleted).");
        }

        if (wanted.Length < 4 || !wanted.All(ch => Uri.IsHexDigit(ch) || ch == '-'))
        {
            throw new ArgumentException($"'{reference}' isn't a rating's id: give at least its first 4 hex digits.");
        }

        var matches = await spaces.FindRatingsAsync(wanted, cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"No rating's id starts with '{wanted}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"'{wanted}' starts {matches.Count} ratings' ids: give more of the id.")
        };
    }

    /// <summary>Throws <see cref="ArgumentException"/> for a day count outside 1 to <see cref="MaxDays"/>.</summary>
    public static void Check(int days)
    {
        if (days < 1 || days > MaxDays)
        {
            throw new ArgumentException($"Ask for 1 to {MaxDays} days; got {days}.");
        }
    }

    /// <summary>
    /// A part's last rated value as of the ratings given (oldest first): what "same as last time"
    /// writes. Null when the part was never rated.
    /// </summary>
    private static decimal? LastOf(IReadOnlyList<SpaceRating> ratings, string part)
        => ratings.Where(r => r.Of(part) is not null)
            .OrderBy(r => r.Day).ThenBy(r => r.RatedAt)
            .Select(r => r.Of(part))
            .LastOrDefault();

    private static decimal? Check(decimal? rating, string part)
        => rating is { } r ? SpaceLimits.CheckRating(r, part) : null;

    private static DateOnly CheckDay(DateOnly day, DateOnly today)
        => day <= today
            ? day
            : throw new ArgumentException(
                $"A rating is for today or earlier; {Text(day)} hasn't happened yet (today is {Text(today)}).");

    private async Task<DateTimeOffset> OccurredAtAsync(
        DateOnly on, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
        => on == today ? now : await spaces.NoonAsync(on, cancellationToken);

    private async Task<IReadOnlyList<SpaceRating>> ReadAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var read = new List<SpaceRating>();
        foreach (var id in ids)
        {
            if (await spaces.GetRatingAsync(id, cancellationToken) is { } rating)
            {
                read.Add(rating);
            }
        }

        return read;
    }

    private NewEvent Declared(DateTimeOffset occurredAt, DateTimeOffset now, string payload)
        => new(EventKinds.Measurement, Provenances.Declared, occurredAt, now, sourceId, PayloadJson: payload);

    /// <summary>A rating's payload; the parts as numbers (4.5, 4), the ones left out absent.</summary>
    private static string Payload(
        Guid id, Guid space, DateOnly day, decimal? tidiness, decimal? cleanliness, decimal? aesthetics, string? note)
    {
        var rating = new Dictionary<string, object>
        {
            ["id"] = id.ToString(),
            ["space"] = space.ToString(),
            ["day"] = Text(day)
        };
        if (tidiness is { } t)
        {
            rating[HomeParts.Tidiness] = (double)t;
        }

        if (cleanliness is { } c)
        {
            rating[HomeParts.Cleanliness] = (double)c;
        }

        if (aesthetics is { } a)
        {
            rating[HomeParts.Aesthetics] = (double)a;
        }

        if (note is not null)
        {
            rating["note"] = note;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { [SpaceLimits.RatingKey] = rating });
    }

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

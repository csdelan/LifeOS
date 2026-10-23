using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Covers;

/// <summary>
/// Sets, moves and clears a subject's cover (COVER-1, D15; cover-images plan §3.3) — the one
/// writer of <c>attributes.cover</c>, behind <c>bsk cover</c> and the cover endpoints.
/// <list type="bullet">
/// <item>The caller names a photo by its id only. The service looks it up on Unsplash and
/// stores what Unsplash says, so a stored image is always on <c>images.unsplash.com</c>.</item>
/// <item>Setting a cover counts a download for the photographer, after the write and best
/// effort: a counter that fails doesn't undo the cover. Moving and clearing tell Unsplash
/// nothing.</item>
/// <item>Like a rename, a change rewrites the attribute in place and keeps no history.</item>
/// </list>
/// Without an <see cref="IUnsplashClient"/> (no <c>Unsplash:AccessKey</c> on this host),
/// searching and setting are refused with <see cref="UnsplashException.NotSetUpMessage"/>;
/// reading, moving and clearing still work.
/// </summary>
public sealed class CoverService(
    SubjectService subjects, ISubjectRepository repository, IUnsplashClient? unsplash, IClock clock)
{
    public const int MaxQuery = 100;
    public const int MaxPage = 50;
    public const int DefaultPerPage = 24;
    public const int MaxPerPage = 30;

    /// <summary>Whether this host can search Unsplash and set covers.</summary>
    public bool IsSetUp => unsplash is not null;

    /// <summary>The <c>utm_source</c> on links back to Unsplash.</summary>
    public string AppName => unsplash?.AppName ?? Cover.DefaultAppName;

    /// <summary>
    /// A page (1–<see cref="MaxPage"/>) of landscape photos for <paramref name="query"/>
    /// (1–<see cref="MaxQuery"/> characters, spaces squeezed).
    /// </summary>
    public Task<UnsplashSearchPage> SearchAsync(
        string query, int page = 1, int perPage = DefaultPerPage, CancellationToken cancellationToken = default)
    {
        var words = CheckQuery(query);
        if (page is < 1 or > MaxPage)
        {
            throw new ArgumentException($"Pages run from 1 to {MaxPage}; got {page}.");
        }

        if (perPage is < 1 or > MaxPerPage)
        {
            throw new ArgumentException($"A page holds 1 to {MaxPerPage} photos; got {perPage}.");
        }

        return RequireClient().SearchAsync(words, page, perPage, cancellationToken);
    }

    /// <summary>The subject's cover, or none.</summary>
    public async Task<CoverView> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        return new CoverView(subject, await ReadAsync(subject, cancellationToken));
    }

    /// <summary>
    /// Makes the Unsplash photo <paramref name="photoId"/> the subject's cover, centred at
    /// <paramref name="positionY"/> (the middle when not given). Refused, with nothing
    /// written, for a type that can't have a cover, a malformed id or position, or a photo
    /// Unsplash doesn't have.
    /// </summary>
    public async Task<CoverChange> SetFromUnsplashAsync(
        string reference, string photoId, int? positionY = null, CancellationToken cancellationToken = default)
    {
        var id = photoId?.Trim() ?? "";
        if (!Cover.IsPhotoId(id))
        {
            throw new ArgumentException($"'{photoId}' isn't an Unsplash photo id.");
        }

        var position = Cover.CheckPosition(positionY ?? Cover.DefaultPositionY);
        var client = RequireClient();
        var subject = await ResolveCoverableAsync(reference, cancellationToken);

        var photo = await client.GetPhotoAsync(id, cancellationToken);
        var cover = FromPhoto(photo, position, clock.UtcNow);
        await WriteAsync(subject, cover, cancellationToken);

        var problem = await CountDownloadAsync(client, photo, cancellationToken);
        return new CoverChange(subject, cover, DownloadCounted: problem is null, DownloadProblem: problem);
    }

    /// <summary>Moves the banner's centre to <paramref name="positionY"/> (0 top, 100 bottom). Refused when there's no cover.</summary>
    public async Task<CoverChange> MoveAsync(
        string reference, int positionY, CancellationToken cancellationToken = default)
    {
        var position = Cover.CheckPosition(positionY);
        var subject = await ResolveCoverableAsync(reference, cancellationToken);
        var cover = await ReadAsync(subject, cancellationToken)
                    ?? throw new InvalidOperationException($"'{subject.Title}' has no cover to move.");

        var moved = cover.WithPosition(position);
        await WriteAsync(subject, moved, cancellationToken);
        return new CoverChange(subject, moved, DownloadCounted: false, DownloadProblem: null);
    }

    /// <summary>
    /// Removes the subject's cover. Works on any subject, so a cover left in some other shape
    /// can always be cleared; says whether there was one.
    /// </summary>
    public async Task<CoverCleared> ClearAsync(string reference, CancellationToken cancellationToken = default)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        var had = await repository.GetAttributeValueAsync(subject.Id, Cover.AttributeKey, cancellationToken) is not null;
        if (had && !await repository.UpdateAttributesAsync(subject.Id, "{}", [Cover.AttributeKey], cancellationToken))
        {
            throw new SubjectNotFoundException(subject.Urn);
        }

        return new CoverCleared(subject, had);
    }

    /// <summary>
    /// The cover a photo makes: its raw image, colour, size and credit, with the description
    /// and photographer trimmed to what a cover keeps. Refused when Unsplash's answer wouldn't
    /// pass <see cref="Cover.Validate"/> (an image off <c>images.unsplash.com</c>, say).
    /// </summary>
    public static Cover FromPhoto(UnsplashPhoto photo, int positionY, DateTimeOffset now)
    {
        var alt = Squeeze(photo.AltDescription) ?? Squeeze(photo.Description);
        var photographer = Squeeze(photo.Photographer) ?? "";
        return new Cover(
                Cover.Unsplash,
                photo.Id,
                photo.RawUrl,
                Cover.IsColor(photo.Color) ? photo.Color!.ToLowerInvariant() : Cover.DefaultColor,
                photo.Width,
                photo.Height,
                Clip(alt, Cover.MaxAlt),
                Clip(photographer, Cover.MaxPhotographer)!,
                photo.PhotographerUrl,
                photo.PhotoUrl,
                positionY,
                now)
            .Validate();
    }

    /// <summary>The search words, spaces squeezed, or refused when blank or too long.</summary>
    public static string CheckQuery(string? query)
    {
        var words = Squeeze(query);
        if (words is null)
        {
            throw new ArgumentException("Say what to search for, e.g. mountain sunrise.");
        }

        if (words.Length > MaxQuery)
        {
            throw new ArgumentException($"Search for at most {MaxQuery} characters; got {words.Length}.");
        }

        return words;
    }

    private IUnsplashClient RequireClient() => unsplash ?? throw UnsplashException.NotSetUp();

    private async Task<SubjectRef> ResolveCoverableAsync(string reference, CancellationToken cancellationToken)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        if (!Cover.Allowed(subject.Type))
        {
            throw new InvalidOperationException(
                $"'{subject.Title}' is a {subject.Type}; only identity statements (Value), goals and projects have covers.");
        }

        return subject;
    }

    private async Task<Cover?> ReadAsync(SubjectRef subject, CancellationToken cancellationToken)
        => Cover.Parse(await repository.GetAttributeValueAsync(subject.Id, Cover.AttributeKey, cancellationToken));

    private async Task WriteAsync(SubjectRef subject, Cover cover, CancellationToken cancellationToken)
    {
        var patch = new JsonObject { [Cover.AttributeKey] = cover.ToJson() }.ToJsonString();
        if (!await repository.UpdateAttributesAsync(subject.Id, patch, [], cancellationToken))
        {
            // The subject resolved a moment ago; a missing row now is a real fault.
            throw new SubjectNotFoundException(subject.Urn);
        }
    }

    // Best effort: the cover is already set, so a counter that fails is reported, not thrown.
    private static async Task<string?> CountDownloadAsync(
        IUnsplashClient client, UnsplashPhoto photo, CancellationToken cancellationToken)
    {
        if (photo.DownloadLocation is not { } location)
        {
            return "Unsplash gave no download link for this photo, so the download wasn't counted.";
        }

        try
        {
            await client.TrackDownloadAsync(location, cancellationToken);
            return null;
        }
        catch (UnsplashException ex)
        {
            return $"The cover is set, but the download wasn't counted: {ex.Message}";
        }
    }

    private static string? Squeeze(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? Clip(string? text, int max)
        => text is null || text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
}

/// <summary>A subject and its cover, if it has one.</summary>
public sealed record CoverView(SubjectRef Subject, Cover? Cover);

/// <summary>
/// A cover set or moved. <see cref="DownloadCounted"/> says whether Unsplash's download
/// counter was told (only on set); <see cref="DownloadProblem"/> says why not, when it wasn't.
/// </summary>
public sealed record CoverChange(SubjectRef Subject, Cover Cover, bool DownloadCounted, string? DownloadProblem);

/// <summary>A cover removed; <see cref="HadCover"/> is false when there was nothing to remove.</summary>
public sealed record CoverCleared(SubjectRef Subject, bool HadCover);

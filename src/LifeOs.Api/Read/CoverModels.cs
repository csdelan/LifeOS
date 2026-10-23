using LifeOs.Application.Abstractions;
using LifeOs.Application.Covers;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// A subject's cover as pages read it (COVER-1..3, D15; cover-images plan §3.4): the stored
/// cover, with the credit links already carrying Unsplash's referral parameters, so every
/// page and the CLI credit the same way. <see cref="Url"/> is the photo's raw image; pages add
/// the size they need.
/// </summary>
public sealed class CoverRead
{
    public string Source { get; set; } = "";
    public string PhotoId { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>The photo's average colour, <c>#rrggbb</c>: shown while it loads, and if it can't.</summary>
    public string Color { get; set; } = "";

    public int Width { get; set; }
    public int Height { get; set; }
    public string? Alt { get; set; }
    public string Photographer { get; set; } = "";

    /// <summary>The photographer's Unsplash profile, with the referral parameters.</summary>
    public string PhotographerLink { get; set; } = "";

    /// <summary>The photo's page on Unsplash, with the referral parameters.</summary>
    public string PhotoLink { get; set; } = "";

    /// <summary>Unsplash's home page, with the referral parameters.</summary>
    public string UnsplashLink { get; set; } = "";

    /// <summary>Where a banner is centred vertically: 0 (top) to 100 (bottom).</summary>
    public int PositionY { get; set; }

    public DateTimeOffset SetAt { get; set; }

    public static CoverRead From(Cover cover, string appName) => new()
    {
        Source = cover.Source,
        PhotoId = cover.PhotoId,
        Url = cover.Url,
        Color = cover.Color,
        Width = cover.Width,
        Height = cover.Height,
        Alt = cover.Alt,
        Photographer = cover.Photographer,
        PhotographerLink = cover.PhotographerLink(appName),
        PhotoLink = cover.PhotoLink(appName),
        UnsplashLink = Cover.UnsplashLink(appName),
        PositionY = cover.PositionY,
        SetAt = cover.SetAt,
    };

    /// <summary>The cover in the raw <c>attributes-&gt;'cover'</c> text, or null when there's none or it's malformed.</summary>
    public static CoverRead? FromJson(string? json, string appName)
        => Cover.Parse(json) is { } cover ? From(cover, appName) : null;
}

/// <summary>
/// <c>GET /api/covers/search</c>: a page of Unsplash photos for the picker. Without an
/// Access Key on the server, <see cref="Configured"/> is false and there are no photos.
/// </summary>
public sealed class CoverSearchRead
{
    public bool Configured { get; set; }
    public string Query { get; set; } = "";
    public int Page { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }

    /// <summary>Unsplash's home page with the referral parameters, for "Photos from Unsplash".</summary>
    public string UnsplashLink { get; set; } = "";

    public IReadOnlyList<CoverPhotoRead> Photos { get; set; } = [];
}

/// <summary>One photo in the picker: a small image to show, and its credit.</summary>
public sealed class CoverPhotoRead
{
    /// <summary>What <c>POST /api/cover</c> takes.</summary>
    public string Id { get; set; } = "";

    /// <summary>The photo at 400 px wide, for a picker tile.</summary>
    public string ThumbUrl { get; set; } = "";

    public string Color { get; set; } = "";
    public string? Alt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Photographer { get; set; } = "";

    /// <summary>The photographer's Unsplash profile, with the referral parameters.</summary>
    public string PhotographerLink { get; set; } = "";

    /// <summary>The width a picker tile asks for.</summary>
    public const int ThumbWidth = 400;

    public static CoverPhotoRead From(UnsplashPhoto photo, string appName) => new()
    {
        Id = photo.Id,
        ThumbUrl = Sized(photo.RawUrl, ThumbWidth),
        Color = Cover.IsColor(photo.Color) ? photo.Color!.ToLowerInvariant() : Cover.DefaultColor,
        Alt = photo.AltDescription ?? photo.Description,
        Width = photo.Width,
        Height = photo.Height,
        Photographer = photo.Photographer,
        PhotographerLink = Cover.WithReferral(photo.PhotographerUrl, appName),
    };

    /// <summary>A raw Unsplash image at <paramref name="width"/> px, its <c>ixid</c> kept.</summary>
    public static string Sized(string rawUrl, int width)
    {
        var separator = rawUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{rawUrl}{separator}w={width}&q=70&auto=format&fit=max";
    }
}

/// <summary><c>POST /api/cover</c> and <c>/api/cover/position</c>: the subject and its cover now.</summary>
public sealed class CoverResult
{
    public CreatedSubject Subject { get; set; } = new();
    public CoverRead Cover { get; set; } = new();

    /// <summary>Whether Unsplash's download counter was told (only when a photo is picked).</summary>
    public bool DownloadCounted { get; set; }

    /// <summary>Why the download wasn't counted, when it should have been; the cover is set regardless.</summary>
    public string? DownloadProblem { get; set; }

    public static CoverResult From(CoverChange change, string appName) => new()
    {
        Subject = CoverSubjects.From(change.Subject),
        Cover = CoverRead.From(change.Cover, appName),
        DownloadCounted = change.DownloadCounted,
        DownloadProblem = change.DownloadProblem,
    };
}

/// <summary><c>POST /api/cover/clear</c>: the subject, and whether it had a cover to remove.</summary>
public sealed class CoverClearedRead
{
    public CreatedSubject Subject { get; set; } = new();
    public bool Removed { get; set; }

    public static CoverClearedRead From(CoverCleared cleared) => new()
    {
        Subject = CoverSubjects.From(cleared.Subject),
        Removed = cleared.HadCover,
    };
}

internal static class CoverSubjects
{
    public static CreatedSubject From(SubjectRef subject) => new()
    {
        Id = subject.Id,
        Urn = subject.Urn,
        Type = subject.Type,
        Title = subject.Title,
    };
}

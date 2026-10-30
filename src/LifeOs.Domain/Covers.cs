using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LifeOs.Domain;

/// <summary>
/// A subject's cover image (COVER-1..3, D15; cover-images plan §3.1): a reference to an
/// Unsplash photo kept in <c>attributes.cover</c>. Unsplash requires its images be shown
/// from its own servers, so this is a link and its credit, never bytes.
/// <list type="bullet">
/// <item><see cref="Url"/> is the photo's <c>urls.raw</c>, kept whole so its <c>ixid</c>
/// stays; pages add size parameters to it.</item>
/// <item><see cref="PositionY"/> (0–100) is where a banner is centred vertically.</item>
/// <item>Only <see cref="Types"/> may have one.</item>
/// </list>
/// <see cref="Validate"/> is the write-side gate: the image must be on
/// <c>images.unsplash.com</c> and the credit links on <c>unsplash.com</c>, so a cover can't
/// put another site's picture on a page. <see cref="Parse"/> is lenient: a cover hand-edited
/// into some other shape reads as none.
/// </summary>
public sealed partial record Cover(
    string Source,
    string PhotoId,
    string Url,
    string Color,
    int Width,
    int Height,
    string? Alt,
    string Photographer,
    string PhotographerUrl,
    string PhotoUrl,
    int PositionY,
    DateTimeOffset SetAt)
{
    /// <summary>The attribute that holds a subject's cover.</summary>
    public const string AttributeKey = "cover";

    /// <summary>The one source today: a photo on Unsplash.</summary>
    public const string Unsplash = "unsplash";

    /// <summary>Where a banner is centred when nobody has moved it: the middle.</summary>
    public const int DefaultPositionY = 50;

    /// <summary>The colour shown while a photo loads, when Unsplash gives none.</summary>
    public const string DefaultColor = "#808080";

    /// <summary>The <c>utm_source</c> on credit links when no app name is configured.</summary>
    public const string DefaultAppName = "lifeos";

    /// <summary>The only host a cover's image may come from.</summary>
    public const string ImageHost = "images.unsplash.com";

    /// <summary>The only host a cover's credit links may go to.</summary>
    public const string SiteHost = "unsplash.com";

    public const int MaxPhotographer = 200;
    public const int MaxAlt = 500;
    public const int MaxUrl = 2000;

    /// <summary>The subject types that may have a cover: identity statements, goals, projects and habits.</summary>
    public static readonly IReadOnlyList<string> Types =
        [SubjectTypes.Value, SubjectTypes.Goal, SubjectTypes.Project, SubjectTypes.Habit];

    /// <summary>Whether a subject of <paramref name="type"/> may have a cover.</summary>
    public static bool Allowed(string type) => Types.Contains(type, StringComparer.Ordinal);

    /// <summary>The same cover centred at <paramref name="positionY"/>, checked.</summary>
    public Cover WithPosition(int positionY) => this with { PositionY = CheckPosition(positionY) };

    /// <summary><paramref name="positionY"/> when it's 0–100; otherwise refused in words.</summary>
    public static int CheckPosition(int positionY)
        => positionY is >= 0 and <= 100
            ? positionY
            : throw new ArgumentException($"A cover's position runs from 0 (top) to 100 (bottom); got {positionY}.");

    /// <summary>Refuses, in words, a cover that mustn't be stored. Returns it when it may.</summary>
    public Cover Validate()
    {
        if (!string.Equals(Source, Unsplash, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A cover comes from {Unsplash}; got '{Source}'.");
        }

        if (!IsPhotoId(PhotoId))
        {
            throw new ArgumentException($"'{PhotoId}' isn't an Unsplash photo id.");
        }

        if (!IsImageUrl(Url))
        {
            throw new ArgumentException($"A cover's image must be an https address on {ImageHost}.");
        }

        if (!IsColor(Color))
        {
            throw new ArgumentException($"A cover's colour is #rrggbb; got '{Color}'.");
        }

        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentException($"A cover's size must be above zero; got {Width} × {Height}.");
        }

        if (Alt is { Length: > MaxAlt })
        {
            throw new ArgumentException($"A cover's description is at most {MaxAlt} characters.");
        }

        if (string.IsNullOrWhiteSpace(Photographer) || Photographer.Length > MaxPhotographer)
        {
            throw new ArgumentException($"A cover names its photographer, in at most {MaxPhotographer} characters.");
        }

        if (!IsSiteUrl(PhotographerUrl) || !IsSiteUrl(PhotoUrl))
        {
            throw new ArgumentException($"A cover's credit links must be https addresses on {SiteHost}.");
        }

        CheckPosition(PositionY);
        return this;
    }

    /// <summary>Whether <paramref name="id"/> looks like an Unsplash photo id: letters, digits, dashes, underscores.</summary>
    public static bool IsPhotoId(string? id) => id is not null && PhotoIdPattern().IsMatch(id);

    /// <summary>Whether <paramref name="color"/> is <c>#rrggbb</c>.</summary>
    public static bool IsColor(string? color) => color is not null && ColorPattern().IsMatch(color);

    /// <summary>Whether <paramref name="url"/> is an https address on <see cref="ImageHost"/>.</summary>
    public static bool IsImageUrl(string? url) => IsHttpsOn(url, ImageHost);

    /// <summary>Whether <paramref name="url"/> is an https address on <see cref="SiteHost"/>.</summary>
    public static bool IsSiteUrl(string? url) => IsHttpsOn(url, SiteHost);

    /// <summary>Whether <paramref name="name"/> can be a <c>utm_source</c>: lowercase letters, digits, dashes, underscores.</summary>
    public static bool IsAppName(string? name) => name is not null && AppNamePattern().IsMatch(name);

    /// <summary>The photographer's Unsplash profile, with the referral parameters Unsplash asks for.</summary>
    public string PhotographerLink(string appName) => WithReferral(PhotographerUrl, appName);

    /// <summary>The photo's own page on Unsplash, with the referral parameters.</summary>
    public string PhotoLink(string appName) => WithReferral(PhotoUrl, appName);

    /// <summary>Unsplash's home page, with the referral parameters.</summary>
    public static string UnsplashLink(string appName) => WithReferral($"https://{SiteHost}/", appName);

    /// <summary>
    /// <paramref name="url"/> with <c>utm_source=&lt;app&gt;&amp;utm_medium=referral</c> added, as
    /// Unsplash's guidelines ask of every link back to it.
    /// </summary>
    public static string WithReferral(string url, string appName)
    {
        if (!IsAppName(appName))
        {
            throw new ArgumentException($"'{appName}' can't be a utm_source: use lowercase letters, digits, dashes or underscores.");
        }

        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}utm_source={appName}&utm_medium=referral";
    }

    /// <summary>The cover as stored in <c>attributes.cover</c>.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["source"] = Source,
            ["photo_id"] = PhotoId,
            ["url"] = Url,
            ["color"] = Color,
            ["width"] = Width,
            ["height"] = Height,
        };
        if (Alt is not null)
        {
            json["alt"] = Alt;
        }

        json["photographer"] = Photographer;
        json["photographer_url"] = PhotographerUrl;
        json["photo_url"] = PhotoUrl;
        json["position_y"] = PositionY;
        json["set_at"] = SetAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return json;
    }

    /// <summary>
    /// The cover in the raw <c>attributes-&gt;'cover'</c> text, or null when there is none, it
    /// isn't JSON, a field is missing or the wrong type, or it wouldn't pass
    /// <see cref="Validate"/>.
    /// </summary>
    public static Cover? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject o
            || Text(o, "source") is not { } source
            || Text(o, "photo_id") is not { } photoId
            || Text(o, "url") is not { } url
            || Text(o, "color") is not { } color
            || Int(o, "width") is not { } width
            || Int(o, "height") is not { } height
            || Text(o, "photographer") is not { } photographer
            || Text(o, "photographer_url") is not { } photographerUrl
            || Text(o, "photo_url") is not { } photoUrl
            || Int(o, "position_y") is not { } positionY
            || Text(o, "set_at") is not { } setAtText
            || !DateTimeOffset.TryParse(setAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var setAt))
        {
            return null;
        }

        var cover = new Cover(
            source, photoId, url, color, width, height, Text(o, "alt"), photographer, photographerUrl, photoUrl,
            positionY, setAt);
        try
        {
            return cover.Validate();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsHttpsOn(string? url, string host)
        => url is { Length: > 0 and <= MaxUrl }
           && Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && uri.IsDefaultPort
           && string.IsNullOrEmpty(uri.UserInfo)
           && string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonObject o, string name)
        => o[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Int(JsonObject o, string name)
        => o[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex PhotoIdPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,39}$")]
    private static partial Regex AppNamePattern();
}

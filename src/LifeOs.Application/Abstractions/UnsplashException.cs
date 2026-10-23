namespace LifeOs.Application.Abstractions;

/// <summary>What went wrong with Unsplash (cover-images plan §3.2).</summary>
public enum UnsplashFailure
{
    /// <summary>This server has no <c>Unsplash:AccessKey</c>. The API answers 409.</summary>
    NotSetUp,

    /// <summary>Unsplash refused the Access Key (401). The API answers 502: it's the server's setting.</summary>
    BadKey,

    /// <summary>The hourly limit is used up (403 with no requests left, or 429). The API answers 502.</summary>
    RateLimited,

    /// <summary>Unsplash no longer has the photo (404). The API answers 404.</summary>
    NotFound,

    /// <summary>Unsplash couldn't be reached, timed out, failed or answered something unreadable. The API answers 502.</summary>
    Unavailable,
}

/// <summary>
/// Unsplash couldn't do what was asked. The message says why in words and never carries the
/// Access Key. Nothing stored has changed.
/// </summary>
public sealed class UnsplashException(UnsplashFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Unsplash isn't set up on this server: the words the picker and the CLI show.</summary>
    public const string NotSetUpMessage =
        "Unsplash isn't set up on this server: add Unsplash__AccessKey (from unsplash.com/developers).";

    public UnsplashFailure Failure { get; } = failure;

    public static UnsplashException NotSetUp() => new(UnsplashFailure.NotSetUp, NotSetUpMessage);
}

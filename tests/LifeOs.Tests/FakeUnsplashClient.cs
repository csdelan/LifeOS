using LifeOs.Application.Abstractions;

namespace LifeOs.Tests;

/// <summary>
/// Unsplash for the cover tests (COVER-1): answers every lookup and search with
/// <see cref="Next"/>, or fails with <see cref="Failure"/>, and records what was asked, so no
/// test ever calls Unsplash.
/// </summary>
internal sealed class FakeUnsplashClient : IUnsplashClient
{
    public static readonly UnsplashPhoto Photo = new(
        "Dwu85P9SOIk",
        "https://images.unsplash.com/photo-1417325384643-aac51acc9e5d?ixid=M3w2NTQ3",
        "#6E633A",
        2448,
        3264,
        "A man drinking a coffee.",
        "man drinking coffee",
        "Joe Example",
        "https://unsplash.com/@exampleuser",
        "https://unsplash.com/photos/Dwu85P9SOIk",
        new Uri("https://api.unsplash.com/photos/Dwu85P9SOIk/download?ixid=M3w2NTQ3"));

    public UnsplashPhoto Next { get; set; } = Photo;

    public UnsplashException? Failure { get; set; }

    public UnsplashException? DownloadFailure { get; init; }

    public List<string> Looked { get; } = [];

    public List<Uri> Downloads { get; } = [];

    public List<(string Query, int Page, int PerPage)> Searched { get; } = [];

    public string AppName => "lifeos";

    public Task<UnsplashSearchPage> SearchAsync(string query, int page, int perPage, CancellationToken cancellationToken = default)
    {
        Searched.Add((query, page, perPage));
        return Task.FromResult(new UnsplashSearchPage(1, 1, [Next]));
    }

    public Task<UnsplashPhoto> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default)
    {
        Looked.Add(photoId);
        return Failure is null ? Task.FromResult(Next) : Task.FromException<UnsplashPhoto>(Failure);
    }

    public Task TrackDownloadAsync(Uri downloadLocation, CancellationToken cancellationToken = default)
    {
        if (DownloadFailure is not null)
        {
            return Task.FromException(DownloadFailure);
        }

        Downloads.Add(downloadLocation);
        return Task.CompletedTask;
    }
}

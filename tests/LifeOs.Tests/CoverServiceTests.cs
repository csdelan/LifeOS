using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Covers;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk cover</c>'s service (COVER-1, D15; cover-images plan §3.3, C1), against a fake
/// Unsplash: a cover is set by photo id from what Unsplash says, only on identity statements,
/// goals and projects; a download is counted on set and never on move; clearing removes it;
/// and nothing else — <c>bsk set</c>, other attribute edits, renames — writes or loses it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CoverServiceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 18, 4, 0, TimeSpan.Zero);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Provider(FakeUnsplashClient? unsplash)
    {
        var services = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString);
        services.AddSingleton<IClock>(new TimerTestClock(Now));
        if (unsplash is not null)
        {
            services.AddSingleton<IUnsplashClient>(unsplash);
        }

        return services.BuildServiceProvider();
    }

    private Task<SubjectRef> Create(ServiceProvider provider, string type)
    {
        var attributes = type == SubjectTypes.Value
            ? """{"statement":"I am someone who looks up at the hills."}"""
            : "{}";
        return provider.GetRequiredService<SubjectService>()
            .CreateAsync(type, $"{type} cover {Guid.NewGuid():N}", attributes, cancellationToken: Ct);
    }

    private async Task<string?> StoredCover(Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT attributes->>'cover' FROM bsk.subject WHERE id = @id;", new { id }, cancellationToken: Ct));
    }

    private async Task<string?> Stored(Guid id, string key)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT attributes->>@key FROM bsk.subject WHERE id = @id;", new { id, key }, cancellationToken: Ct));
    }

    [Theory]
    [InlineData(SubjectTypes.Value)]
    [InlineData(SubjectTypes.Goal)]
    [InlineData(SubjectTypes.Project)]
    [InlineData(SubjectTypes.Habit)]
    public async Task A_photo_becomes_the_cover_from_what_unsplash_says(string type)
    {
        var unsplash = new FakeUnsplashClient();
        await using var provider = Provider(unsplash);
        var subject = await Create(provider, type);

        var change = await provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(subject.Urn, " Dwu85P9SOIk ", cancellationToken: Ct);

        Assert.True(change.DownloadCounted);
        Assert.Null(change.DownloadProblem);
        var cover = Assert.IsType<Cover>(Cover.Parse(await StoredCover(subject.Id)));
        Assert.Equal(change.Cover, cover);
        Assert.Equal(Cover.Unsplash, cover.Source);
        Assert.Equal("Dwu85P9SOIk", cover.PhotoId);
        Assert.Equal(FakeUnsplashClient.Photo.RawUrl, cover.Url);
        Assert.Equal("#6e633a", cover.Color);
        Assert.Equal((2448, 3264), (cover.Width, cover.Height));
        Assert.Equal("man drinking coffee", cover.Alt);
        Assert.Equal("Joe Example", cover.Photographer);
        Assert.Equal("https://unsplash.com/@exampleuser", cover.PhotographerUrl);
        Assert.Equal("https://unsplash.com/photos/Dwu85P9SOIk", cover.PhotoUrl);
        Assert.Equal(Cover.DefaultPositionY, cover.PositionY);
        Assert.Equal(Now, cover.SetAt);

        Assert.Equal(["Dwu85P9SOIk"], unsplash.Looked);
        Assert.Equal([FakeUnsplashClient.Photo.DownloadLocation!], unsplash.Downloads);
    }

    [Fact]
    public async Task A_position_can_be_given_with_the_photo()
    {
        await using var provider = Provider(new FakeUnsplashClient());
        var goal = await Create(provider, SubjectTypes.Goal);

        var change = await provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", 25, Ct);

        Assert.Equal(25, change.Cover.PositionY);
        Assert.Equal(25, Cover.Parse(await StoredCover(goal.Id))!.PositionY);
    }

    [Fact]
    public async Task Picking_another_photo_replaces_the_cover_and_counts_again()
    {
        var unsplash = new FakeUnsplashClient();
        await using var provider = Provider(unsplash);
        var covers = provider.GetRequiredService<CoverService>();
        var goal = await Create(provider, SubjectTypes.Goal);

        await covers.SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct);
        unsplash.Next = FakeUnsplashClient.Photo with { Id = "xBRQfR2bqNI", Photographer = "Annie Spratt" };
        await covers.SetFromUnsplashAsync(goal.Urn, "xBRQfR2bqNI", cancellationToken: Ct);

        Assert.Equal("xBRQfR2bqNI", Cover.Parse(await StoredCover(goal.Id))!.PhotoId);
        Assert.Equal(2, unsplash.Downloads.Count);
    }

    [Theory]
    [InlineData(SubjectTypes.Task)]
    [InlineData(SubjectTypes.Area)]
    public async Task Other_types_are_refused_before_asking_unsplash(string type)
    {
        var unsplash = new FakeUnsplashClient();
        await using var provider = Provider(unsplash);
        var subject = await Create(provider, type);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(subject.Urn, "Dwu85P9SOIk", cancellationToken: Ct));

        Assert.Contains("only identity statements (Value), goals, projects and habits have covers", ex.Message);
        Assert.Null(await StoredCover(subject.Id));
        Assert.Empty(unsplash.Looked);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("../photos", null)]
    [InlineData("Dwu85P9SOIk", 101)]
    [InlineData("Dwu85P9SOIk", -5)]
    public async Task A_malformed_photo_id_or_position_is_refused_before_anything(string photoId, int? position)
    {
        var unsplash = new FakeUnsplashClient();
        await using var provider = Provider(unsplash);
        var goal = await Create(provider, SubjectTypes.Goal);

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(goal.Urn, photoId, position, Ct));

        Assert.Null(await StoredCover(goal.Id));
        Assert.Empty(unsplash.Looked);
    }

    [Fact]
    public async Task A_photo_unsplash_doesnt_have_writes_nothing()
    {
        var unsplash = new FakeUnsplashClient
        {
            Failure = new UnsplashException(UnsplashFailure.NotFound, "That photo is no longer on Unsplash.")
        };
        await using var provider = Provider(unsplash);
        var goal = await Create(provider, SubjectTypes.Goal);

        var ex = await Assert.ThrowsAsync<UnsplashException>(() => provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(goal.Urn, "gone123", cancellationToken: Ct));

        Assert.Equal(UnsplashFailure.NotFound, ex.Failure);
        Assert.Null(await StoredCover(goal.Id));
        Assert.Empty(unsplash.Downloads);
    }

    [Fact]
    public async Task An_answer_pointing_off_unsplash_is_refused_and_writes_nothing()
    {
        var unsplash = new FakeUnsplashClient { Next = FakeUnsplashClient.Photo with { RawUrl = "https://example.com/photo.jpg" } };
        await using var provider = Provider(unsplash);
        var goal = await Create(provider, SubjectTypes.Goal);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct));

        Assert.Contains("images.unsplash.com", ex.Message);
        Assert.Null(await StoredCover(goal.Id));
        Assert.Empty(unsplash.Downloads);
    }

    [Fact]
    public async Task A_download_counter_that_fails_leaves_the_cover_set_and_says_so()
    {
        var unsplash = new FakeUnsplashClient
        {
            DownloadFailure = new UnsplashException(UnsplashFailure.RateLimited, "Unsplash's hourly limit is used up. Try again in a few minutes.")
        };
        await using var provider = Provider(unsplash);
        var goal = await Create(provider, SubjectTypes.Goal);

        var change = await provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct);

        Assert.False(change.DownloadCounted);
        Assert.Contains("hourly limit", change.DownloadProblem);
        Assert.NotNull(Cover.Parse(await StoredCover(goal.Id)));
    }

    [Fact]
    public async Task Tidies_what_unsplash_says_into_what_a_cover_keeps()
    {
        var unsplash = new FakeUnsplashClient
        {
            Next = FakeUnsplashClient.Photo with
            {
                Color = null,
                AltDescription = "  ",
                Description = "Morning   light\nover the hills",
                Photographer = "  Annie   Spratt ",
            }
        };
        await using var provider = Provider(unsplash);
        var goal = await Create(provider, SubjectTypes.Goal);

        var cover = (await provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct)).Cover;

        Assert.Equal(Cover.DefaultColor, cover.Color);
        Assert.Equal("Morning light over the hills", cover.Alt);
        Assert.Equal("Annie Spratt", cover.Photographer);
    }

    [Fact]
    public async Task Moving_changes_only_the_position_and_tells_unsplash_nothing()
    {
        var unsplash = new FakeUnsplashClient();
        await using var provider = Provider(unsplash);
        var covers = provider.GetRequiredService<CoverService>();
        var value = await Create(provider, SubjectTypes.Value);
        var set = (await covers.SetFromUnsplashAsync(value.Urn, "Dwu85P9SOIk", cancellationToken: Ct)).Cover;

        var moved = await covers.MoveAsync(value.Urn, 80, Ct);

        Assert.Equal(set with { PositionY = 80 }, moved.Cover);
        Assert.False(moved.DownloadCounted);
        Assert.Equal(set with { PositionY = 80 }, Cover.Parse(await StoredCover(value.Id)));
        Assert.Single(unsplash.Looked);
        Assert.Single(unsplash.Downloads);
    }

    [Fact]
    public async Task Moving_without_a_cover_or_out_of_range_is_refused()
    {
        await using var provider = Provider(new FakeUnsplashClient());
        var covers = provider.GetRequiredService<CoverService>();
        var goal = await Create(provider, SubjectTypes.Goal);

        var none = await Assert.ThrowsAsync<InvalidOperationException>(() => covers.MoveAsync(goal.Urn, 30, Ct));
        Assert.Contains("has no cover to move", none.Message);

        await covers.SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => covers.MoveAsync(goal.Urn, 120, Ct));
        Assert.Equal(Cover.DefaultPositionY, Cover.Parse(await StoredCover(goal.Id))!.PositionY);
    }

    [Fact]
    public async Task Clearing_removes_the_cover_and_says_whether_there_was_one()
    {
        await using var provider = Provider(new FakeUnsplashClient());
        var covers = provider.GetRequiredService<CoverService>();
        var project = await Create(provider, SubjectTypes.Project);
        await covers.SetFromUnsplashAsync(project.Urn, "Dwu85P9SOIk", cancellationToken: Ct);

        Assert.True((await covers.ClearAsync(project.Urn, Ct)).HadCover);
        Assert.Null(await StoredCover(project.Id));
        Assert.False((await covers.ClearAsync(project.Urn, Ct)).HadCover);
    }

    [Fact]
    public async Task Without_a_key_searching_and_setting_say_unsplash_isnt_set_up_but_the_rest_works()
    {
        await using var withKey = Provider(new FakeUnsplashClient());
        var goal = await Create(withKey, SubjectTypes.Goal);
        await withKey.GetRequiredService<CoverService>().SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct);

        await using var provider = Provider(unsplash: null);
        var covers = provider.GetRequiredService<CoverService>();

        Assert.False(covers.IsSetUp);
        Assert.Equal(Cover.DefaultAppName, covers.AppName);
        var search = await Assert.ThrowsAsync<UnsplashException>(() => covers.SearchAsync("hills", cancellationToken: Ct));
        Assert.Equal(UnsplashFailure.NotSetUp, search.Failure);
        Assert.Equal(UnsplashException.NotSetUpMessage, search.Message);
        await Assert.ThrowsAsync<UnsplashException>(() => covers.SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct));

        Assert.NotNull((await covers.GetAsync(goal.Urn, Ct)).Cover);
        Assert.Equal(10, (await covers.MoveAsync(goal.Urn, 10, Ct)).Cover.PositionY);
        Assert.True((await covers.ClearAsync(goal.Urn, Ct)).HadCover);
    }

    [Fact]
    public async Task Search_squeezes_the_words_and_checks_the_page()
    {
        var unsplash = new FakeUnsplashClient();
        await using var provider = Provider(unsplash);
        var covers = provider.GetRequiredService<CoverService>();

        var page = await covers.SearchAsync("  mountain \t sunrise ", 2, cancellationToken: Ct);

        Assert.Equal(("mountain sunrise", 2, CoverService.DefaultPerPage), unsplash.Searched.Single());
        Assert.Single(page.Photos);

        await Assert.ThrowsAsync<ArgumentException>(() => covers.SearchAsync("   ", cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => covers.SearchAsync(new string('a', CoverService.MaxQuery + 1), cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => covers.SearchAsync("hills", 0, cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => covers.SearchAsync("hills", CoverService.MaxPage + 1, cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => covers.SearchAsync("hills", 1, CoverService.MaxPerPage + 1, Ct));
        Assert.Single(unsplash.Searched);
    }

    [Fact]
    public async Task Bsk_set_refuses_the_cover_key_either_way()
    {
        await using var provider = Provider(new FakeUnsplashClient());
        var attributes = provider.GetRequiredService<AttributeService>();
        var goal = await Create(provider, SubjectTypes.Goal);
        await provider.GetRequiredService<CoverService>().SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct);

        var set = await Assert.ThrowsAsync<ArgumentException>(() =>
            attributes.SetAsync(goal.Urn, [new AttributeAssignment("cover", "https://example.com/x.jpg")], Ct));
        Assert.Contains("bsk cover", set.Message);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            attributes.SetAsync(goal.Urn, [new AttributeAssignment("Cover", null)], Ct));

        Assert.NotNull(Cover.Parse(await StoredCover(goal.Id)));
    }

    [Fact]
    public async Task Other_attribute_edits_and_renames_leave_the_cover_alone()
    {
        await using var provider = Provider(new FakeUnsplashClient());
        var goal = await Create(provider, SubjectTypes.Goal);
        var cover = (await provider.GetRequiredService<CoverService>()
            .SetFromUnsplashAsync(goal.Urn, "Dwu85P9SOIk", cancellationToken: Ct)).Cover;

        await provider.GetRequiredService<AttributeService>().SetAsync(
            goal.Urn, [new AttributeAssignment("target_date", "2030-01-01"), new AttributeAssignment("description", "Hills.")], Ct);
        await provider.GetRequiredService<RenameService>().RenameAsync(goal.Urn, $"renamed {Guid.NewGuid():N}", Ct);

        Assert.Equal(cover, Cover.Parse(await StoredCover(goal.Id)));
        Assert.Equal("2030-01-01", await Stored(goal.Id, "target_date"));
    }

    [Fact]
    public async Task A_hand_edited_cover_reads_as_none_and_can_still_be_cleared()
    {
        await using var provider = Provider(new FakeUnsplashClient());
        var covers = provider.GetRequiredService<CoverService>();
        var goal = await Create(provider, SubjectTypes.Goal);
        await provider.GetRequiredService<ISubjectRepository>().UpdateAttributesAsync(
            goal.Id, """{"cover":{"source":"unsplash","url":"https://example.com/x.jpg"}}""", [], Ct);

        Assert.Null((await covers.GetAsync(goal.Urn, Ct)).Cover);
        await Assert.ThrowsAsync<InvalidOperationException>(() => covers.MoveAsync(goal.Urn, 10, Ct));
        Assert.True((await covers.ClearAsync(goal.Urn, Ct)).HadCover);
        Assert.Null(await StoredCover(goal.Id));
    }
}

using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// FOOD-5 (food-log plan F6, §3.8): a food entry's photo is an artifact with bytes, written by
/// an <c>observation</c> event, and a new version of the entry pointing at it. It round-trips
/// byte for byte, survives an edit, comes off with Remove photo, and only JPEG, PNG and WebP
/// of at most 5 MB are taken. Each test has a database of its own (UTC).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FoodPhotoTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Bytes that start like a JPEG (the service reads only the first bytes).</summary>
    internal static byte[] Jpeg(int length = 2_000)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        return bytes;
    }

    private async Task<NpgsqlConnection> OpenAsync(DomainTestDatabase db)
    {
        var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task A_photo_round_trips_byte_for_byte_and_an_edit_keeps_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var photos = db.Get<FoodPhotoService>();
        var meal = (await food.LogAsync(new FoodEntryInput("Salmon salad", 5, 650), cancellationToken: Ct)).Meal!;
        var bytes = Jpeg();

        var set = await photos.AttachAsync(FoodLogService.Short(meal.Id), bytes, @"C:\phone\IMG_0042.jpg", Ct);

        Assert.Equal(FoodActions.PhotoSet, set.Action);
        var photo = set.Meal!.PhotoArtifactId!.Value;
        Assert.Equal(("Salmon salad", 5, 650, meal.EatenAt, 2), (set.Meal.Name, set.Meal.Score, set.Meal.Calories, set.Meal.EatenAt, set.Meal.Versions));
        var blob = (await db.Get<IArtifactBlobStore>().GetAsync(photo, Ct))!;
        Assert.Equal(bytes, blob.Bytes);
        Assert.Equal(("image/jpeg", "IMG_0042.jpg"), (blob.ContentType, blob.Filename));

        // The photo's own event: an observation naming the entry, in no inbox.
        await using (var connection = await OpenAsync(db))
        {
            var evt = await connection.QuerySingleAsync<(string Kind, string Payload, bool Inbox)>(new CommandDefinition(
                """
                SELECT e.kind, e.payload::text,
                       EXISTS (SELECT 1 FROM bsk.v_inbox i WHERE i.item_id = e.id)
                FROM bsk.event e WHERE e.artifact_id = @photo;
                """,
                new { photo }, cancellationToken: Ct));
            Assert.Equal("observation", evt.Kind);
            Assert.Equal(meal.Id, JsonDocument.Parse(evt.Payload).RootElement.GetProperty("food_photo").GetProperty("meal").GetGuid());
            Assert.False(evt.Inbox);
        }

        // An edit keeps it; the day reads it.
        var edited = await food.EditAsync(meal.Id.ToString(), new FoodEntryInput("Salmon salad", 4, 700), Ct);
        Assert.Equal(photo, edited.Meal!.PhotoArtifactId);
        Assert.Equal(photo, Assert.Single((await food.GetDayAsync(meal.EatenOn, Ct)).Meals).PhotoArtifactId);

        // The same photo again writes nothing; another replaces it.
        Assert.Equal(3, (await photos.AttachAsync(meal.Id.ToString(), bytes, cancellationToken: Ct)).Meal!.Versions);
        var other = await photos.AttachAsync(meal.Id.ToString(), Jpeg(), cancellationToken: Ct);
        Assert.NotEqual(photo, other.Meal!.PhotoArtifactId);
        Assert.Equal(4, other.Meal.Versions);
    }

    [Fact]
    public async Task Log_again_carries_the_photo_without_storing_it_again()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var photos = db.Get<FoodPhotoService>();
        var first = (await food.LogAsync(new FoodEntryInput("Sausage burrito", 2, 650), cancellationToken: Ct)).Meal!;
        var photo = (await photos.AttachAsync(first.Id.ToString(), Jpeg(), cancellationToken: Ct)).Meal!.PhotoArtifactId!.Value;
        Assert.Equal(photo, await food.PhotoOfAsync(FoodLogService.Short(first.Id), Ct));

        var again = await food.LogAsync(new FoodEntryInput("Sausage burrito", 2, 650), photo: photo, cancellationToken: Ct);

        Assert.Equal(FoodActions.Logged, again.Action);
        Assert.NotEqual(first.Id, again.Meal!.Id);
        Assert.Equal((photo, 1), (again.Meal.PhotoArtifactId, again.Meal.Versions));
        // One stored photo and one photo event, shown by both entries.
        await using (var connection = await OpenAsync(db))
        {
            Assert.Equal((1L, 1L), await connection.QuerySingleAsync<(long, long)>(new CommandDefinition(
                """
                SELECT (SELECT count(*) FROM bsk.artifact_blob),
                       (SELECT count(*) FROM bsk.event WHERE artifact_id = @photo);
                """,
                new { photo }, cancellationToken: Ct)));
        }

        // Each entry's photo is its own: removing the copy's leaves the first's.
        Assert.Null((await photos.RemoveAsync(again.Meal.Id.ToString(), Ct)).Meal!.PhotoArtifactId);
        Assert.Equal(photo, (await food.ResolveMealAsync(first.Id.ToString(), Ct)).PhotoArtifactId);

        // Anything but a stored photo is refused in words, with nothing written.
        var plain = (await food.LogAsync(new FoodEntryInput("Toast", 2, 300), cancellationToken: Ct)).Meal!;
        Assert.Equal("Toast has no photo to carry over.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => food.PhotoOfAsync(plain.Id.ToString(), Ct))).Message);
        Assert.Equal(
            "That photo isn't stored here: pick the photo again, or log the entry without one.",
            (await Assert.ThrowsAsync<ArgumentException>(() => food.LogAsync(
                new FoodEntryInput("Toast", 2, 300), photo: Guid.NewGuid(), cancellationToken: Ct))).Message);
        Assert.Equal(3, (await food.GetDayAsync(first.EatenOn, Ct)).Meals.Count);
    }

    [Fact]
    public async Task Log_again_offers_the_newest_photo_a_name_has_had()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var photos = db.Get<FoodPhotoService>();
        var now = DateTimeOffset.UtcNow;
        var monday = (await food.LogAsync(new FoodEntryInput("Sausage burrito", 2, 650, now.AddDays(-2)), cancellationToken: Ct)).Meal!;
        var photo = (await photos.AttachAsync(monday.Id.ToString(), Jpeg(), cancellationToken: Ct)).Meal!.PhotoArtifactId;
        // Logged again without it: the newest entry has no photo, but its name still offers it.
        var tuesday = (await food.LogAsync(new FoodEntryInput("sausage burrito", 2, 700, now.AddDays(-1)), cancellationToken: Ct)).Meal!;
        await food.LogAsync(new FoodEntryInput("Toast", 2, 300, now.AddHours(-1)), cancellationToken: Ct);

        var recent = await food.GetRecentAsync(cancellationToken: Ct);

        Assert.Equal(["Toast", "sausage burrito"], recent.Select(m => m.Name));
        Assert.Null(recent[0].PhotoArtifactId);
        Assert.Equal((tuesday.Id, 700, photo), (recent[1].Id, recent[1].Calories, recent[1].PhotoArtifactId));
    }

    [Fact]
    public async Task Remove_photo_writes_a_version_without_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var photos = db.Get<FoodPhotoService>();
        var meal = (await food.LogAsync(new FoodEntryInput("Toast", 2, 300), cancellationToken: Ct)).Meal!;
        var photo = (await photos.AttachAsync(meal.Id.ToString(), Jpeg(), cancellationToken: Ct)).Meal!.PhotoArtifactId!.Value;

        var removed = await photos.RemoveAsync(meal.Id.ToString(), Ct);

        Assert.Equal(FoodActions.PhotoRemoved, removed.Action);
        Assert.Null(removed.Meal!.PhotoArtifactId);
        Assert.Equal(("Toast", 2, 300, 3), (removed.Meal.Name, removed.Meal.Score, removed.Meal.Calories, removed.Meal.Versions));
        // The bytes stay in the record.
        Assert.NotNull(await db.Get<IArtifactBlobStore>().GetAsync(photo, Ct));
        Assert.Equal("Toast has no photo to remove.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => photos.RemoveAsync(meal.Id.ToString(), Ct))).Message);
    }

    [Fact]
    public async Task Oversized_non_image_and_empty_files_are_refused_and_nothing_is_stored()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var photos = db.Get<FoodPhotoService>();
        var meal = (await food.LogAsync(new FoodEntryInput("Apple", 5, 90), cancellationToken: Ct)).Meal!;
        var id = meal.Id.ToString();

        Assert.Contains("at most 5 MB", (await Assert.ThrowsAsync<ArgumentException>(
            () => photos.AttachAsync(id, Jpeg(FoodPhotoService.MaxBytes + 1), cancellationToken: Ct))).Message);
        Assert.Contains("JPEG, PNG or WebP", (await Assert.ThrowsAsync<ArgumentException>(
            () => photos.AttachAsync(id, "%PDF-1.7 not a photo"u8.ToArray(), "menu.jpg", Ct))).Message);
        Assert.Contains("empty", (await Assert.ThrowsAsync<ArgumentException>(
            () => photos.AttachAsync(id, [], cancellationToken: Ct))).Message);
        // An entry that isn't there is refused before anything is stored.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => photos.AttachAsync(Guid.NewGuid().ToString(), Jpeg(), cancellationToken: Ct));

        await using var connection = await OpenAsync(db);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.artifact_blob;", cancellationToken: Ct)));
        Assert.Null((await food.ResolveMealAsync(id, Ct)).PhotoArtifactId);
    }

    [Fact]
    public void Only_jpeg_png_and_webp_are_photos()
    {
        Assert.Equal("image/jpeg", FoodPhotoService.Sniff([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/png", FoodPhotoService.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal("image/webp", FoodPhotoService.Sniff("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(FoodPhotoService.Sniff("RIFF\0\0\0\0WAVEfmt "u8));
        Assert.Null(FoodPhotoService.Sniff("GIF89a"u8));
        Assert.Null(FoodPhotoService.Sniff("<svg xmlns="u8));
        Assert.Null(FoodPhotoService.Sniff([0xFF, 0xD8]));
    }

    [Fact]
    public async Task With_object_storage_the_photo_goes_there_and_without_it_a_host_can_refuse_photos()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var storage = new MemoryObjectStorage();
        await using var kernel = new ServiceCollection()
            .AddLifeOsKernel(db.ConnectionString, homeTimeZone: DomainTestDatabase.Zone, allowPhotosInDatabase: false)
            .AddSingleton<IObjectStorage>(storage)
            .BuildServiceProvider();
        var food = kernel.GetRequiredService<FoodLogService>();
        var meal = (await food.LogAsync(new FoodEntryInput("Soup", 4, 350), cancellationToken: Ct)).Meal!;
        var bytes = Jpeg();

        var set = await kernel.GetRequiredService<FoodPhotoService>().AttachAsync(meal.Id.ToString(), bytes, cancellationToken: Ct);

        Assert.Equal(1, storage.Uploads);
        Assert.Equal(bytes, (await kernel.GetRequiredService<IArtifactBlobStore>().GetAsync(set.Meal!.PhotoArtifactId!.Value, Ct))!.Bytes);

        // The same settings without a store refuse photos in words, storing nothing.
        await using var refusing = new ServiceCollection()
            .AddLifeOsKernel(db.ConnectionString, homeTimeZone: DomainTestDatabase.Zone, allowPhotosInDatabase: false)
            .BuildServiceProvider();
        var photos = refusing.GetRequiredService<FoodPhotoService>();
        Assert.False(photos.Enabled);
        Assert.Equal(FoodPhotoService.NotSetUp, (await Assert.ThrowsAsync<InvalidOperationException>(
            () => photos.AttachAsync(meal.Id.ToString(), Jpeg(), cancellationToken: Ct))).Message);
        // Removing one still works.
        Assert.Null((await photos.RemoveAsync(meal.Id.ToString(), Ct)).Meal!.PhotoArtifactId);
    }
}

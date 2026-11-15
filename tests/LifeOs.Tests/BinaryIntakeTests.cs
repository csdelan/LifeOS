using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Application.Food;
using LifeOs.Application.Photos;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR112 (fifth review R5): a stored file's dedup key names its feature. Before, the
/// key was the bare hash for every feature, so a file shared to the Inbox that matched a photo
/// stored for a room or a meal came back as that photo's event, and the Inbox flagged the photo.
/// </summary>
public sealed class BinaryIntakeTests(PostgresFixture postgres, ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // What each feature's door stores: its kind, and a payload that carries its key.
    private static readonly Dictionary<BinaryIntake, string> Kinds = new()
    {
        [BinaryIntake.Inbox] = EventKinds.Note,
        [BinaryIntake.FoodPhoto] = EventKinds.Observation,
        [BinaryIntake.SpacePhoto] = EventKinds.Observation,
        [BinaryIntake.LabReport] = EventKinds.Measurement,
        [BinaryIntake.PersonPhoto] = EventKinds.Observation,
    };

    private static NewBinaryCapture Capture(BinaryIntake intake, byte[] bytes, string source)
        => NewBinaryCapture.Of(
            intake, Kinds[intake], bytes, "image/jpeg", source, DateTimeOffset.UtcNow,
            payloadJson: intake.PayloadKey is null
                ? "{}"
                : JsonSerializer.Serialize(new Dictionary<string, object> { [intake.PayloadKey] = new { id = Guid.NewGuid() } }));

    [Fact]
    public void Every_feature_that_stores_files_is_listed_with_its_kind()
        => Assert.Equal(BinaryIntake.All.OrderBy(i => i.Name), Kinds.Keys.OrderBy(i => i.Name));

    [Fact]
    public async Task The_same_bytes_through_each_pair_of_features_are_an_event_of_each_and_again_through_one_are_its_own()
    {
        var store = new NpgsqlArtifactBlobStore(KernelDataSource.For(classDb.ConnectionString), new MemoryObjectStorage());
        foreach (var first in BinaryIntake.All)
        {
            foreach (var second in BinaryIntake.All.Where(i => i != first))
            {
                var bytes = FoodPhotoTests.Jpeg(300);
                var source = $"test-{Guid.NewGuid():N}";

                var a = await store.PutAsync(Capture(first, bytes, source), Ct);
                var b = await store.PutAsync(Capture(second, bytes, source), Ct);
                var again = await store.PutAsync(Capture(second, bytes, source), Ct);

                Assert.False(b.Deduplicated, $"{second} after {first}");
                Assert.NotEqual(a.EventId, b.EventId);
                Assert.True(again.Deduplicated, $"{second} again after {first}");
                Assert.Equal(b.EventId, again.EventId);
            }
        }
    }

    [Fact]
    public async Task A_file_stored_before_the_key_named_its_feature_is_still_that_features_and_no_others()
    {
        var store = new NpgsqlArtifactBlobStore(KernelDataSource.For(classDb.ConnectionString));
        var bytes = FoodPhotoTests.Jpeg(300);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var source = $"test-{Guid.NewGuid():N}";

        // A meal's photo as it was stored before CR112: its external_id the bare hash.
        Guid legacy;
        await using (var connection = new NpgsqlConnection(classDb.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            var artifact = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO bsk.artifact (content) VALUES ('') RETURNING id;", cancellationToken: Ct));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.artifact_blob (artifact_id, bytes, content_type, byte_size, sha256)
                VALUES (@artifact, @bytes, 'image/jpeg', @size, @sha);
                """,
                new { artifact, bytes, size = (long)bytes.Length, sha }, cancellationToken: Ct));
            legacy = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, external_id, payload, artifact_id)
                VALUES ('observation', 'declared', now(), now(), @source, @sha, '{"food_photo":{"meal":"x"}}', @artifact)
                RETURNING id;
                """,
                new { source, sha, artifact }, cancellationToken: Ct));
        }

        var meal = await store.PutAsync(Capture(BinaryIntake.FoodPhoto, bytes, source), Ct);
        Assert.True(meal.Deduplicated);
        Assert.Equal(legacy, meal.EventId);

        var inbox = await store.PutAsync(Capture(BinaryIntake.Inbox, bytes, source), Ct);
        Assert.False(inbox.Deduplicated);
        Assert.NotEqual(legacy, inbox.EventId);
        var room = await store.PutAsync(Capture(BinaryIntake.SpacePhoto, bytes, source), Ct);
        Assert.False(room.Deduplicated);
    }

    [Fact]
    public async Task A_document_shared_to_the_Inbox_that_matches_a_meals_photo_is_a_capture_of_its_own()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var meal = (await db.Get<FoodLogService>().LogAsync(new FoodEntryInput("Salmon salad", 5, 650), cancellationToken: Ct)).Meal!;
        var bytes = FoodPhotoTests.Jpeg();
        var photo = (await db.Get<FoodPhotoService>().AttachAsync(meal.Id.ToString(), bytes, "salad.jpg", Ct)).Meal!.PhotoArtifactId!.Value;

        var filed = await db.Get<InboxFilingService>().CaptureDocumentAsync(new CaptureFile(bytes, "salad.jpg", "image/jpeg"), cancellationToken: Ct);

        Assert.False(filed.Stored!.Deduplicated);
        Assert.NotEqual(photo, filed.Stored.ArtifactId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        var inbox = (await connection.QueryAsync<(string Kind, string Payload)>(new CommandDefinition(
            """
            SELECT e.kind, e.payload::text FROM bsk.v_inbox i JOIN bsk.event e ON e.id = i.item_id;
            """, cancellationToken: Ct))).ToList();
        Assert.Equal([(EventKinds.Note, "{}")], inbox);
    }
}

using System.Security.Cryptography;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Artifact bytes in object storage (migration 0035, food-log plan F6): with an
/// <see cref="IObjectStorage"/> the blob store uploads the bytes under a content-addressed key
/// and keeps only where they are in <c>bsk.artifact_object</c>; reads find bytes in either
/// place, check what the store gives back, and say plainly when they can't reach it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ArtifactObjectStoreTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (byte[] Bytes, string Sha) RandomBytes(int length = 300)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return (bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    // A source of its own per test: dedup is per (source, hash), and the fixture is shared.
    private static NewBinaryCapture Capture(byte[] bytes, string sha, string source, string? filename = "meal.jpg")
    {
        var now = DateTimeOffset.UtcNow;
        return new NewBinaryCapture(
            EventKinds.Observation, Provenances.Declared, now, now, source, sha, bytes, "image/jpeg", bytes.Length,
            filename, PayloadJson: """{"test":true}""");
    }

    private static string Source() => $"test-{Guid.NewGuid():N}";

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task Bytes_go_to_the_object_store_and_read_back_byte_for_byte()
    {
        var storage = new MemoryObjectStorage();
        var store = new NpgsqlArtifactBlobStore(postgres.ConnectionString, storage);
        var (bytes, sha) = RandomBytes();

        var put = await store.PutAsync(Capture(bytes, sha, Source()), Ct);

        Assert.False(put.Deduplicated);
        Assert.Equal(bytes, storage.Peek($"artifacts/{sha}"));

        await using var connection = await OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.artifact_blob WHERE artifact_id = @ArtifactId;", put, cancellationToken: Ct)));
        var row = await connection.QuerySingleAsync<(string Store, string Bucket, string Key, long Size, string Sha)>(
            new CommandDefinition(
                "SELECT store, bucket, object_key, byte_size, sha256 FROM bsk.artifact_object WHERE artifact_id = @ArtifactId;",
                put, cancellationToken: Ct));
        Assert.Equal(("memory", "test-artifacts", $"artifacts/{sha}", (long)bytes.Length, sha), row);

        // The event points at the artifact and carries the hash, as a database capture does.
        var evt = await connection.QuerySingleAsync<(string Kind, Guid ArtifactId, string ExternalId)>(new CommandDefinition(
            "SELECT kind, artifact_id, external_id FROM bsk.event WHERE id = @EventId;", put, cancellationToken: Ct));
        Assert.Equal(("observation", put.ArtifactId, sha), evt);

        var view = await connection.QuerySingleAsync<(bool HasBytes, string ContentType, long Size, string Filename, string StoredIn)>(
            new CommandDefinition(
                "SELECT has_bytes, content_type, byte_size, filename, stored_in FROM bsk.v_artifact WHERE id = @ArtifactId;",
                put, cancellationToken: Ct));
        Assert.Equal((true, "image/jpeg", (long)bytes.Length, "meal.jpg", "memory"), view);

        var blob = await store.GetAsync(put.ArtifactId, Ct);
        Assert.Equal(bytes, blob!.Bytes);
        Assert.Equal(("image/jpeg", sha, "meal.jpg"), (blob.ContentType, blob.Sha256, blob.Filename));
        Assert.True(await store.ExistsAsync(put.ArtifactId, Ct));
        Assert.Equal(sha, (await store.GetMetadataAsync(put.ArtifactId, Ct))!.Sha256);
    }

    [Fact]
    public async Task The_same_bytes_again_reuse_the_artifact_without_uploading()
    {
        var storage = new MemoryObjectStorage();
        var store = new NpgsqlArtifactBlobStore(postgres.ConnectionString, storage);
        var (bytes, sha) = RandomBytes();
        var source = Source();

        var first = await store.PutAsync(Capture(bytes, sha, source), Ct);
        var again = await store.PutAsync(Capture(bytes, sha, source), Ct);

        Assert.True(again.Deduplicated);
        Assert.Equal((first.EventId, first.ArtifactId), (again.EventId, again.ArtifactId));
        Assert.Equal(1, storage.Uploads);
    }

    [Fact]
    public async Task A_failed_upload_writes_nothing()
    {
        var storage = new MemoryObjectStorage { Failing = true };
        var store = new NpgsqlArtifactBlobStore(postgres.ConnectionString, storage);
        var (bytes, sha) = RandomBytes();

        await Assert.ThrowsAsync<ObjectStorageException>(() => store.PutAsync(Capture(bytes, sha, Source()), Ct));

        await using var connection = await OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE external_id = @sha;", new { sha }, cancellationToken: Ct)));
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.artifact_object WHERE sha256 = @sha;", new { sha }, cancellationToken: Ct)));
    }

    [Fact]
    public async Task Bytes_stored_in_the_database_before_object_storage_still_read()
    {
        var (bytes, sha) = RandomBytes();
        var before = await new NpgsqlArtifactBlobStore(postgres.ConnectionString).PutAsync(Capture(bytes, sha, Source()), Ct);

        var storage = new MemoryObjectStorage();
        var store = new NpgsqlArtifactBlobStore(postgres.ConnectionString, storage);

        Assert.Equal(bytes, (await store.GetAsync(before.ArtifactId, Ct))!.Bytes);
        Assert.Equal(0, storage.Uploads);

        await using var connection = await OpenAsync();
        Assert.Equal("database", await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT stored_in FROM bsk.v_artifact WHERE id = @ArtifactId;", before, cancellationToken: Ct)));
    }

    [Fact]
    public async Task Reading_an_object_needs_its_store_and_checks_what_comes_back()
    {
        var storage = new MemoryObjectStorage();
        var store = new NpgsqlArtifactBlobStore(postgres.ConnectionString, storage);
        var (bytes, sha) = RandomBytes();
        var put = await store.PutAsync(Capture(bytes, sha, Source()), Ct);
        var key = $"artifacts/{sha}";

        // A host without object storage knows where the bytes are but can't fetch them.
        var unconfigured = await Assert.ThrowsAsync<ObjectStorageException>(
            () => new NpgsqlArtifactBlobStore(postgres.ConnectionString).GetAsync(put.ArtifactId, Ct));
        Assert.Contains("Storage__SupabaseSecretKey", unconfigured.Message);
        // Its metadata still reads.
        Assert.NotNull(await new NpgsqlArtifactBlobStore(postgres.ConnectionString).GetMetadataAsync(put.ArtifactId, Ct));

        // Changed bytes are caught, size and hash alike.
        var changed = bytes.ToArray();
        changed[0] ^= 0xFF;
        storage.Overwrite(key, changed);
        Assert.Contains("don't match", (await Assert.ThrowsAsync<ObjectStorageException>(
            () => store.GetAsync(put.ArtifactId, Ct))).Message);
        storage.Overwrite(key, bytes[..^1]);
        await Assert.ThrowsAsync<ObjectStorageException>(() => store.GetAsync(put.ArtifactId, Ct));

        // So is a lost object.
        storage.Lose(key);
        Assert.Contains("missing", (await Assert.ThrowsAsync<ObjectStorageException>(
            () => store.GetAsync(put.ArtifactId, Ct))).Message);
    }

    [Fact]
    public async Task Object_rows_are_append_only_and_bytes_live_in_one_place()
    {
        var store = new NpgsqlArtifactBlobStore(postgres.ConnectionString, new MemoryObjectStorage());
        var (bytes, sha) = RandomBytes();
        var put = await store.PutAsync(Capture(bytes, sha, Source()), Ct);

        await using var connection = await OpenAsync();
        var update = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk.artifact_object SET object_key = 'elsewhere' WHERE artifact_id = @ArtifactId;", put, cancellationToken: Ct)));
        Assert.Contains("append-only", update.MessageText);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM bsk.artifact_object WHERE artifact_id = @ArtifactId;", put, cancellationToken: Ct)));
        Assert.Contains("append-only", delete.MessageText);

        // The artifact's bytes are in the object store, so the database can't hold them too.
        var twice = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.artifact_blob (artifact_id, bytes, content_type, byte_size, sha256)
            VALUES (@ArtifactId, @bytes, 'image/jpeg', @size, @sha);
            """,
            new { put.ArtifactId, bytes, size = (long)bytes.Length, sha }, cancellationToken: Ct)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, twice.SqlState);
    }

    [Fact]
    public async Task The_reader_reads_where_an_object_is_but_cannot_write_one()
    {
        var storage = new MemoryObjectStorage();
        var (bytes, sha) = RandomBytes();
        var put = await new NpgsqlArtifactBlobStore(postgres.ConnectionString, storage).PutAsync(Capture(bytes, sha, Source()), Ct);
        var readerConnection = BskReader.ConnectionStringFrom(postgres.ConnectionString);

        // A store on the reader connection serves the bytes, as the API does.
        Assert.Equal(bytes, (await new NpgsqlArtifactBlobStore(readerConnection, storage).GetAsync(put.ArtifactId, Ct))!.Bytes);

        await using var reader = new NpgsqlConnection(readerConnection);
        await reader.OpenAsync(Ct);
        var refused = await Assert.ThrowsAsync<PostgresException>(() => reader.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.artifact_object (artifact_id, store, bucket, object_key, content_type, byte_size, sha256)
            VALUES (@ArtifactId, 'memory', 'b', 'k', 'image/jpeg', 1, @sha);
            """,
            new { put.ArtifactId, sha }, cancellationToken: Ct)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
    }
}

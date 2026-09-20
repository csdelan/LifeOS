using System.Security.Cryptography;
using Dapper;
using LifeOs.Infrastructure;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies the binary-artifact schema from migration 0022: the append-only
/// guarantee extends to blob bytes, metadata cannot drift from the payload, the
/// hash column is well-formed, the metadata view hides the raw bytes, and the
/// SELECT-only reader can read metadata and bytes but never write them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ArtifactBlobTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    /// <summary>Seeds an artifact + a blob for it, and returns the artifact id.</summary>
    private async Task<Guid> SeedBlobAsync(NpgsqlConnection connection, byte[]? bytes = null)
    {
        bytes ??= [1, 2, 3, 4];
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var artifactId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "INSERT INTO bsk.artifact (content) VALUES ('') RETURNING id;",
            cancellationToken: Ct));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.artifact_blob (artifact_id, bytes, content_type, byte_size, sha256, filename)
            VALUES (@artifactId, @bytes, 'application/octet-stream', @size, @sha, 'seed.bin');
            """,
            new { artifactId, bytes, size = (long)bytes.Length, sha }, cancellationToken: Ct));

        return artifactId;
    }

    [Fact]
    public async Task Update_of_a_blob_is_denied_at_the_database()
    {
        await using var connection = await OpenAsync();
        var artifactId = await SeedBlobAsync(connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE bsk.artifact_blob SET bytes = '\\x00'::bytea WHERE artifact_id = @artifactId;",
                new { artifactId }, cancellationToken: Ct)));

        Assert.Contains("append-only", ex.MessageText);
    }

    [Fact]
    public async Task Delete_of_a_blob_is_denied_at_the_database()
    {
        await using var connection = await OpenAsync();
        var artifactId = await SeedBlobAsync(connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM bsk.artifact_blob WHERE artifact_id = @artifactId;",
                new { artifactId }, cancellationToken: Ct)));

        Assert.Contains("append-only", ex.MessageText);
    }

    [Fact]
    public async Task Byte_size_that_disagrees_with_the_payload_is_rejected()
    {
        await using var connection = await OpenAsync();
        var artifactId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "INSERT INTO bsk.artifact (content) VALUES ('') RETURNING id;", cancellationToken: Ct));

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.artifact_blob (artifact_id, bytes, content_type, byte_size, sha256)
                VALUES (@artifactId, '\x0102'::bytea, 'application/octet-stream', 99,
                        '0000000000000000000000000000000000000000000000000000000000000000');
                """,
                new { artifactId }, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task A_malformed_hash_is_rejected()
    {
        await using var connection = await OpenAsync();
        var artifactId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "INSERT INTO bsk.artifact (content) VALUES ('') RETURNING id;", cancellationToken: Ct));

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.artifact_blob (artifact_id, bytes, content_type, byte_size, sha256)
                VALUES (@artifactId, '\x01'::bytea, 'application/octet-stream', 1, 'not-a-hash');
                """,
                new { artifactId }, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Metadata_view_exposes_metadata_but_not_the_bytes()
    {
        await using var connection = await OpenAsync();
        var artifactId = await SeedBlobAsync(connection, [9, 8, 7, 6, 5]);

        var row = await connection.QuerySingleAsync<(bool HasBytes, string ContentType, long ByteSize, string Sha, string? Filename)>(
            new CommandDefinition(
                "SELECT has_bytes, content_type, byte_size, sha256, filename FROM bsk.v_artifact WHERE id = @artifactId;",
                new { artifactId }, cancellationToken: Ct));

        Assert.True(row.HasBytes);
        Assert.Equal("application/octet-stream", row.ContentType);
        Assert.Equal(5, row.ByteSize);
        Assert.Equal("seed.bin", row.Filename);

        // The view carries no bytes column at all — a list query can never drag the payload.
        var columns = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'bsk' AND table_name = 'v_artifact';
            """,
            cancellationToken: Ct))).ToList();
        Assert.DoesNotContain("bytes", columns);
    }

    [Fact]
    public async Task Reader_can_read_blob_metadata_and_bytes_but_cannot_write()
    {
        await using var owner = await OpenAsync();
        var artifactId = await SeedBlobAsync(owner, [4, 2]);

        await using var reader = new NpgsqlConnection(BskReader.ConnectionStringFrom(postgres.ConnectionString));
        await reader.OpenAsync(Ct);

        // Metadata via the view, and the bytes via a deliberate SELECT — both allowed.
        var hasBytes = await reader.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT has_bytes FROM bsk.v_artifact WHERE id = @artifactId;",
            new { artifactId }, cancellationToken: Ct));
        Assert.True(hasBytes);

        var bytes = await reader.ExecuteScalarAsync<byte[]>(new CommandDefinition(
            "SELECT bytes FROM bsk.artifact_blob WHERE artifact_id = @artifactId;",
            new { artifactId }, cancellationToken: Ct));
        Assert.Equal([4, 2], bytes);

        // But the reader cannot write bytes — append-only + single-writer holds below the door.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await reader.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.artifact_blob (artifact_id, bytes, content_type, byte_size, sha256)
                VALUES (@artifactId, '\x00'::bytea, 'application/octet-stream', 1,
                        '0000000000000000000000000000000000000000000000000000000000000000');
                """,
                new { artifactId }, cancellationToken: Ct)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }
}

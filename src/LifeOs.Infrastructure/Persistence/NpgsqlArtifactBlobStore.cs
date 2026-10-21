using System.Data;
using System.Security.Cryptography;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The <see cref="IArtifactBlobStore"/>. Where the bytes go depends on <paramref name="objects"/>:
/// <list type="bullet">
/// <item>Without object storage, as Postgres <c>bytea</c> in <c>bsk.artifact_blob</c>, in one
/// transaction with the artifact and the event — they land together or not at all, because
/// <c>bsk.event</c> is append-only and a committed event whose bytes failed to write could
/// never be corrected.</item>
/// <item>With object storage (Supabase Storage, migration 0035), the bytes are uploaded first
/// under a content-addressed key (<c>artifacts/&lt;sha256&gt;</c>), then the artifact, a
/// <c>bsk.artifact_object</c> row saying where the object is, and the event are written in one
/// transaction. A failed upload writes nothing; a failed transaction leaves an object nothing
/// points at, which the next upload of the same bytes reuses.</item>
/// </list>
/// Dedup is content-addressed either way: an event already present for (source_id, sha256)
/// short-circuits the write and is returned as-is. Reads find the bytes wherever they are, so
/// artifacts stored before object storage was configured still read from the database. A
/// store built on the reader connection serves reads only.
/// </summary>
public sealed class NpgsqlArtifactBlobStore(string connectionString, IObjectStorage? objects = null) : IArtifactBlobStore
{
    private const string FindExistingSql = """
        SELECT id AS EventId, artifact_id AS ArtifactId
        FROM bsk.event
        WHERE source_id = @SourceId AND external_id = @Sha256
        LIMIT 1;
        """;

    private const string InsertArtifactSql = """
        INSERT INTO bsk.artifact (content) VALUES (@Content) RETURNING id;
        """;

    private const string InsertBlobSql = """
        INSERT INTO bsk.artifact_blob
            (artifact_id, bytes, content_type, byte_size, sha256, filename)
        VALUES
            (@ArtifactId, @Bytes, @ContentType, @ByteSize, @Sha256, @Filename);
        """;

    private const string InsertObjectSql = """
        INSERT INTO bsk.artifact_object
            (artifact_id, store, bucket, object_key, content_type, byte_size, sha256, filename)
        VALUES
            (@ArtifactId, @Store, @Bucket, @ObjectKey, @ContentType, @ByteSize, @Sha256, @Filename);
        """;

    private const string InsertEventSql = """
        INSERT INTO bsk.event
            (kind, provenance, occurred_at, recorded_at, source_id, external_id,
             payload, artifact_id)
        VALUES
            (@Kind, @Provenance, @OccurredAt, @RecordedAt, @SourceId, @Sha256,
             @Payload::jsonb, @ArtifactId)
        RETURNING id;
        """;

    // The bytes wherever they are: the database's, or where the object is (bytes null).
    private const string FindStoredSql = """
        SELECT artifact_id AS ArtifactId, bytes AS Bytes, content_type AS ContentType,
               byte_size AS ByteSize, sha256 AS Sha256, filename AS Filename,
               NULL::text AS Store, NULL::text AS Bucket, NULL::text AS ObjectKey
        FROM bsk.artifact_blob
        WHERE artifact_id = @artifactId
        UNION ALL
        SELECT artifact_id, NULL::bytea, content_type, byte_size, sha256, filename,
               store, bucket, object_key
        FROM bsk.artifact_object
        WHERE artifact_id = @artifactId
        LIMIT 1;
        """;

    private const string FindMetadataSql = """
        SELECT artifact_id AS ArtifactId, content_type AS ContentType,
               byte_size AS ByteSize, sha256 AS Sha256, filename AS Filename
        FROM bsk.artifact_blob
        WHERE artifact_id = @artifactId
        UNION ALL
        SELECT artifact_id, content_type, byte_size, sha256, filename
        FROM bsk.artifact_object
        WHERE artifact_id = @artifactId
        LIMIT 1;
        """;

    /// <summary>The object key for bytes with this hash: the same bytes are the same object.</summary>
    public static string ObjectKey(string sha256) => $"artifacts/{sha256}";

    public async Task<BinaryCaptureResult> PutAsync(
        NewBinaryCapture capture, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (objects is null)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            // Content-addressed idempotency: an identical file already captured from this
            // source collapses to the existing event — no second event, no second blob.
            if (await FindExistingAsync(connection, transaction, capture, cancellationToken) is { } stored)
            {
                await transaction.CommitAsync(cancellationToken);
                return stored;
            }

            var result = await WriteAsync(connection, transaction, capture, InsertBlobSql, new
            {
                capture.Bytes,
                capture.ContentType,
                capture.ByteSize,
                capture.Sha256,
                capture.Filename
            }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        // Probe before uploading, so a re-captured file costs no upload. Nothing is held open
        // across the upload; a capture racing this one trips the (source_id, external_id) index.
        if (await FindExistingAsync(connection, null, capture, cancellationToken) is { } existing)
        {
            return existing;
        }

        var key = ObjectKey(capture.Sha256);
        await objects.PutAsync(key, capture.Bytes, capture.ContentType, cancellationToken);

        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var result = await WriteAsync(connection, transaction, capture, InsertObjectSql, new
            {
                objects.Store,
                objects.Bucket,
                ObjectKey = key,
                capture.ContentType,
                capture.ByteSize,
                capture.Sha256,
                capture.Filename
            }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }

    public async Task<bool> ExistsAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (SELECT 1 FROM bsk.artifact_blob WHERE artifact_id = @artifactId)
                OR EXISTS (SELECT 1 FROM bsk.artifact_object WHERE artifact_id = @artifactId);
            """,
            new { artifactId }, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The bytes and metadata, from the database or the object store. Throws
    /// <see cref="ObjectStorageException"/> when the bytes are in an object store this store
    /// wasn't given, the object is missing, or what comes back doesn't match its size and hash.
    /// </summary>
    public async Task<ArtifactBlob?> GetAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        StoredRow? row;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            row = await connection.QuerySingleOrDefaultAsync<StoredRow>(new CommandDefinition(
                FindStoredSql, new { artifactId }, cancellationToken: cancellationToken));
        }

        if (row is null)
        {
            return null;
        }

        var bytes = row.Bytes ?? await DownloadAsync(row, cancellationToken);
        return new ArtifactBlob(row.ArtifactId, bytes, row.ContentType, row.ByteSize, row.Sha256, row.Filename);
    }

    public async Task<ArtifactBlobMetadata?> GetMetadataAsync(
        Guid artifactId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<MetadataRow>(new CommandDefinition(
            FindMetadataSql, new { artifactId }, cancellationToken: cancellationToken));

        return row is not null
            ? new ArtifactBlobMetadata(row.ArtifactId, row.ContentType, row.ByteSize, row.Sha256, row.Filename)
            : null;
    }

    private async Task<byte[]> DownloadAsync(StoredRow row, CancellationToken cancellationToken)
    {
        var where = $"{row.Store} storage ({row.Bucket}/{row.ObjectKey})";
        if (objects is null || objects.Store != row.Store)
        {
            throw new ObjectStorageException(
                $"Artifact {row.ArtifactId}'s bytes are in {where}, and this host isn't configured to read it. Set Storage__SupabaseSecretKey.");
        }

        var bytes = await objects.GetAsync(row.Bucket!, row.ObjectKey!, cancellationToken)
            ?? throw new ObjectStorageException($"Artifact {row.ArtifactId}'s bytes are missing from {where}.");

        if (bytes.LongLength != row.ByteSize
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), row.Sha256, StringComparison.Ordinal))
        {
            throw new ObjectStorageException(
                $"Artifact {row.ArtifactId}'s bytes in {where} don't match what was stored ({row.ByteSize} bytes, sha256 {row.Sha256}).");
        }

        return bytes;
    }

    private static async Task<BinaryCaptureResult?> FindExistingAsync(
        NpgsqlConnection connection, IDbTransaction? transaction, NewBinaryCapture capture,
        CancellationToken cancellationToken)
    {
        var existing = await connection.QuerySingleOrDefaultAsync<ExistingCapture>(new CommandDefinition(
            FindExistingSql,
            new { capture.SourceId, capture.Sha256 },
            transaction: transaction, cancellationToken: cancellationToken));
        return existing is null ? null : new BinaryCaptureResult(existing.EventId, existing.ArtifactId, Deduplicated: true);
    }

    // The artifact, where its bytes are (bytesSql, given the artifact id), then the event.
    private static async Task<BinaryCaptureResult> WriteAsync(
        NpgsqlConnection connection, IDbTransaction transaction, NewBinaryCapture capture,
        string bytesSql, object bytesParameters, CancellationToken cancellationToken)
    {
        var artifactId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertArtifactSql,
            new { Content = capture.TextContent },
            transaction: transaction, cancellationToken: cancellationToken));

        var parameters = new DynamicParameters(bytesParameters);
        parameters.Add("ArtifactId", artifactId);
        await connection.ExecuteAsync(new CommandDefinition(
            bytesSql, parameters, transaction: transaction, cancellationToken: cancellationToken));

        var eventId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertEventSql,
            new
            {
                capture.Kind,
                capture.Provenance,
                capture.OccurredAt,
                capture.RecordedAt,
                capture.SourceId,
                capture.Sha256,
                Payload = capture.PayloadJson,
                ArtifactId = artifactId
            },
            transaction: transaction, cancellationToken: cancellationToken));

        return new BinaryCaptureResult(eventId, artifactId, Deduplicated: false);
    }

    // Reference-type row DTOs: Dapper maps rows into a record class cleanly and
    // returns null for no row, whereas a Nullable<record struct> maps to null even
    // when a row is present.
    private sealed record ExistingCapture(Guid EventId, Guid ArtifactId);

    private sealed record StoredRow(
        Guid ArtifactId, byte[]? Bytes, string ContentType, long ByteSize, string Sha256, string? Filename,
        string? Store, string? Bucket, string? ObjectKey);

    private sealed record MetadataRow(
        Guid ArtifactId, string ContentType, long ByteSize, string Sha256, string? Filename);
}

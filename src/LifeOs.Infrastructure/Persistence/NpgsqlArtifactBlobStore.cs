using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The default <see cref="IArtifactBlobStore"/>: stores binary payloads as Postgres
/// <c>bytea</c> in <c>bsk.artifact_blob</c>, alongside the append-only source layer.
/// The write is one transaction — event + artifact + blob land together or not at all
/// — because <c>bsk.event</c> is append-only and a committed event whose bytes failed
/// to write could never be corrected. Dedup is content-addressed: an event already
/// present for (source_id, sha256) short-circuits the write and is returned as-is.
///
/// This is the seam a future object-storage adapter replaces; nothing above it changes.
/// </summary>
public sealed class NpgsqlArtifactBlobStore(string connectionString) : IArtifactBlobStore
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

    private const string InsertEventSql = """
        INSERT INTO bsk.event
            (kind, provenance, occurred_at, recorded_at, source_id, external_id,
             payload, artifact_id)
        VALUES
            (@Kind, @Provenance, @OccurredAt, @RecordedAt, @SourceId, @Sha256,
             @Payload::jsonb, @ArtifactId)
        RETURNING id;
        """;

    public async Task<BinaryCaptureResult> PutAsync(
        NewBinaryCapture capture, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Content-addressed idempotency: an identical file already captured from this
        // source collapses to the existing event — no second event, no second blob.
        var existing = await connection.QuerySingleOrDefaultAsync<ExistingCapture>(new CommandDefinition(
            FindExistingSql,
            new { capture.SourceId, capture.Sha256 },
            transaction: transaction, cancellationToken: cancellationToken));

        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new BinaryCaptureResult(existing.EventId, existing.ArtifactId, Deduplicated: true);
        }

        var artifactId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertArtifactSql,
            new { Content = capture.TextContent },
            transaction: transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            InsertBlobSql,
            new
            {
                ArtifactId = artifactId,
                capture.Bytes,
                capture.ContentType,
                capture.ByteSize,
                capture.Sha256,
                capture.Filename
            },
            transaction: transaction, cancellationToken: cancellationToken));

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

        await transaction.CommitAsync(cancellationToken);
        return new BinaryCaptureResult(eventId, artifactId, Deduplicated: false);
    }

    public async Task<bool> ExistsAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.artifact_blob WHERE artifact_id = @artifactId);",
            new { artifactId }, cancellationToken: cancellationToken));
    }

    public async Task<ArtifactBlob?> GetAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<BlobRow>(new CommandDefinition(
            """
            SELECT artifact_id AS ArtifactId, bytes AS Bytes, content_type AS ContentType,
                   byte_size AS ByteSize, sha256 AS Sha256, filename AS Filename
            FROM bsk.artifact_blob
            WHERE artifact_id = @artifactId;
            """,
            new { artifactId }, cancellationToken: cancellationToken));

        return row is not null
            ? new ArtifactBlob(row.ArtifactId, row.Bytes, row.ContentType, row.ByteSize, row.Sha256, row.Filename)
            : null;
    }

    public async Task<ArtifactBlobMetadata?> GetMetadataAsync(
        Guid artifactId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<MetadataRow>(new CommandDefinition(
            """
            SELECT artifact_id AS ArtifactId, content_type AS ContentType,
                   byte_size AS ByteSize, sha256 AS Sha256, filename AS Filename
            FROM bsk.artifact_blob
            WHERE artifact_id = @artifactId;
            """,
            new { artifactId }, cancellationToken: cancellationToken));

        return row is not null
            ? new ArtifactBlobMetadata(row.ArtifactId, row.ContentType, row.ByteSize, row.Sha256, row.Filename)
            : null;
    }

    // Reference-type row DTOs: Dapper maps rows into a record class cleanly and
    // returns null for no row, whereas a Nullable<record struct> maps to null even
    // when a row is present.
    private sealed record ExistingCapture(Guid EventId, Guid ArtifactId);

    private sealed record BlobRow(
        Guid ArtifactId, byte[] Bytes, string ContentType, long ByteSize, string Sha256, string? Filename);

    private sealed record MetadataRow(
        Guid ArtifactId, string ContentType, long ByteSize, string Sha256, string? Filename);
}

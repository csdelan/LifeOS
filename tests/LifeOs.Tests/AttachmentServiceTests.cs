using System.Security.Cryptography;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Exercises the binary-capture write path end to end (CAP-2 / CAP-4): bytes roundtrip
/// byte-for-byte through the export port, the content hash is honest, an identical
/// re-capture deduplicates, the voice path writes a voice event + audio artifact +
/// transcript, the size guard rejects oversized payloads, and a multi-MB payload
/// survives the roundtrip intact.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AttachmentServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider BuildProvider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task Attachment_bytes_roundtrip_byte_for_byte_and_the_hash_is_honest()
    {
        await using var provider = BuildProvider();
        var attachments = provider.GetRequiredService<AttachmentService>();
        var blobs = provider.GetRequiredService<IArtifactBlobStore>();

        var bytes = new byte[512];
        Random.Shared.NextBytes(bytes);
        var expectedSha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var result = await attachments.CaptureAttachmentAsync(
            bytes, "statement.pdf", contentType: null, description: "disputed Comcast charge", Ct);

        Assert.False(result.Deduplicated);
        Assert.Equal(expectedSha, result.Sha256);
        Assert.Equal("application/pdf", result.ContentType); // inferred from the extension
        Assert.Equal("note", result.Kind);

        var blob = await blobs.GetAsync(result.ArtifactId, Ct);
        Assert.NotNull(blob);
        Assert.Equal(bytes, blob!.Bytes);
        Assert.Equal(expectedSha, blob.Sha256);
        Assert.Equal("statement.pdf", blob.Filename);

        // The description rode along as the artifact's text sidecar.
        await using var connection = await OpenAsync();
        var content = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT content FROM bsk.artifact WHERE id = @id;",
            new { id = result.ArtifactId }, cancellationToken: Ct));
        Assert.Equal("disputed Comcast charge", content);
    }

    [Fact]
    public async Task Recapturing_an_identical_file_deduplicates_to_one_event_and_one_blob()
    {
        await using var provider = BuildProvider();
        var attachments = provider.GetRequiredService<AttachmentService>();

        var bytes = new byte[256];
        Random.Shared.NextBytes(bytes);

        var first = await attachments.CaptureAttachmentAsync(bytes, "dupe.bin", null, null, Ct);
        var second = await attachments.CaptureAttachmentAsync(bytes, "dupe.bin", null, null, Ct);

        Assert.False(first.Deduplicated);
        Assert.True(second.Deduplicated);
        Assert.Equal(first.EventId, second.EventId);
        Assert.Equal(first.ArtifactId, second.ArtifactId);

        await using var connection = await OpenAsync();
        var eventCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE external_id = @sha;",
            new { sha = first.Sha256 }, cancellationToken: Ct));
        Assert.Equal(1, eventCount);

        var blobCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.artifact_blob WHERE sha256 = @sha;",
            new { sha = first.Sha256 }, cancellationToken: Ct));
        Assert.Equal(1, blobCount);
    }

    [Fact]
    public async Task Voice_capture_writes_a_voice_event_audio_artifact_and_transcript()
    {
        await using var provider = BuildProvider();
        var attachments = provider.GetRequiredService<AttachmentService>();

        var audio = new byte[1024];
        Random.Shared.NextBytes(audio);
        const string transcript = "remember to call the plumber about the leak";

        var result = await attachments.CaptureVoiceAsync(audio, "memo.m4a", null, transcript, Ct);

        Assert.Equal("voice", result.Kind);
        Assert.Equal("audio/mp4", result.ContentType); // inferred from .m4a

        await using var connection = await OpenAsync();
        var row = await connection.QuerySingleAsync<(string Kind, Guid? ArtifactId, string Content)>(
            new CommandDefinition(
                """
                SELECT e.kind, e.artifact_id, a.content
                FROM bsk.event e JOIN bsk.artifact a ON a.id = e.artifact_id
                WHERE e.id = @id;
                """,
                new { id = result.EventId }, cancellationToken: Ct));

        Assert.Equal("voice", row.Kind);
        Assert.Equal(result.ArtifactId, row.ArtifactId);
        Assert.Equal(transcript, row.Content);

        // The metadata view sees the audio as a voice artifact carrying bytes.
        var view = await connection.QuerySingleAsync<(string Kind, bool HasBytes, string ContentType)>(
            new CommandDefinition(
                "SELECT kind, has_bytes, content_type FROM bsk.v_artifact WHERE id = @id;",
                new { id = result.ArtifactId }, cancellationToken: Ct));
        Assert.Equal("voice", view.Kind);
        Assert.True(view.HasBytes);
        Assert.Equal("audio/mp4", view.ContentType);
    }

    [Fact]
    public async Task Voice_capture_without_a_transcript_stores_empty_text()
    {
        await using var provider = BuildProvider();
        var attachments = provider.GetRequiredService<AttachmentService>();

        var audio = new byte[64];
        Random.Shared.NextBytes(audio);

        var result = await attachments.CaptureVoiceAsync(audio, "failed.wav", null, transcript: null, Ct);

        await using var connection = await OpenAsync();
        var content = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT content FROM bsk.artifact WHERE id = @id;",
            new { id = result.ArtifactId }, cancellationToken: Ct));
        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public async Task Payload_over_the_configured_limit_is_rejected_before_any_write()
    {
        // A service with a deliberately tiny limit; nothing should reach the store.
        var service = new AttachmentService(
            new NpgsqlArtifactBlobStore(postgres.ConnectionString), new SystemClock(), "cli", maxBytes: 8);

        var tooBig = new byte[9];

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CaptureAttachmentAsync(tooBig, "big.bin", null, null, Ct));

        await using var connection = await OpenAsync();
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.artifact_blob WHERE byte_size = 9;", cancellationToken: Ct));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Empty_payload_is_rejected()
    {
        await using var provider = BuildProvider();
        var attachments = provider.GetRequiredService<AttachmentService>();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await attachments.CaptureAttachmentAsync([], "empty.bin", null, null, Ct));
    }

    [Fact]
    public async Task A_multi_megabyte_payload_roundtrips_intact()
    {
        await using var provider = BuildProvider();
        var attachments = provider.GetRequiredService<AttachmentService>();
        var blobs = provider.GetRequiredService<IArtifactBlobStore>();

        var bytes = new byte[5 * 1024 * 1024]; // 5 MiB
        Random.Shared.NextBytes(bytes);

        var result = await attachments.CaptureAttachmentAsync(bytes, "big.pdf", null, null, Ct);
        Assert.Equal(bytes.Length, result.ByteSize);

        var blob = await blobs.GetAsync(result.ArtifactId, Ct);
        Assert.NotNull(blob);
        Assert.Equal(bytes.Length, blob!.ByteSize);
        Assert.Equal(bytes, blob.Bytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), blob.Sha256);
    }

    [Fact]
    public async Task Metadata_read_omits_the_bytes()
    {
        await using var provider = BuildProvider();
        var attachments = provider.GetRequiredService<AttachmentService>();
        var blobs = provider.GetRequiredService<IArtifactBlobStore>();

        var bytes = new byte[128];
        Random.Shared.NextBytes(bytes);
        var result = await attachments.CaptureAttachmentAsync(bytes, "doc.png", null, null, Ct);

        var metadata = await blobs.GetMetadataAsync(result.ArtifactId, Ct);
        Assert.NotNull(metadata);
        Assert.Equal("image/png", metadata!.ContentType);
        Assert.Equal(128, metadata.ByteSize);
        Assert.True(await blobs.ExistsAsync(result.ArtifactId, Ct));

        // An artifact with no blob reports absent.
        Assert.False(await blobs.ExistsAsync(Guid.NewGuid(), Ct));
        Assert.Null(await blobs.GetMetadataAsync(Guid.NewGuid(), Ct));
        Assert.Null(await blobs.GetAsync(Guid.NewGuid(), Ct));
    }
}

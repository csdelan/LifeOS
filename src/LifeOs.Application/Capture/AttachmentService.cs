using System.Security.Cryptography;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Capture;

/// <summary>
/// The write path for binary captures — a document/attachment (CAP-4) or a voice
/// note (CAP-2). Both are one immutable event whose artifact carries the binary
/// payload (bytes + metadata) and, optionally, a text sidecar (a description for a
/// document, a transcript for a voice note). Transport-neutral: it knows nothing of
/// the CLI, and transcription itself is out of scope — the kernel stores the audio
/// and whatever transcript it is given.
///
/// Idempotency is content-addressed: the payload's sha256 is the event's external_id,
/// so re-capturing an identical file from the same source collapses to one event and
/// one blob (epic invariant 7). A configurable size guard rejects oversized payloads
/// before any bytes are written.
/// </summary>
public sealed class AttachmentService(
    IArtifactBlobStore blobs, IClock clock, string sourceId, long maxBytes)
{
    /// <summary>
    /// Captures a document/attachment as one <c>note</c> event with a binary artifact
    /// (CAP-4 / D4 — the reference flavor). <paramref name="description"/> is stored as
    /// the artifact's text sidecar; <paramref name="contentType"/> overrides the type
    /// guessed from <paramref name="filename"/>.
    /// </summary>
    public Task<AttachmentResult> CaptureAttachmentAsync(
        byte[] bytes, string? filename = null, string? contentType = null,
        string? description = null, CancellationToken cancellationToken = default)
        => CaptureAsync(EventKinds.Note, bytes, filename, contentType, description, cancellationToken);

    /// <summary>
    /// Captures a voice note as one <c>voice</c> event with an audio artifact (CAP-2) —
    /// closing the previously unreachable <c>voice</c> event kind. <paramref name="transcript"/>
    /// is stored as the artifact's text sidecar (empty when transcript-only failed or was
    /// skipped); the audio bytes are always retained here (the app decides whether to keep
    /// audio before it ever calls the kernel).
    /// </summary>
    public Task<AttachmentResult> CaptureVoiceAsync(
        byte[] audioBytes, string? filename = null, string? contentType = null,
        string? transcript = null, CancellationToken cancellationToken = default)
        => CaptureAsync(EventKinds.Voice, audioBytes, filename, contentType, transcript, cancellationToken);

    private async Task<AttachmentResult> CaptureAsync(
        string kind, byte[] bytes, string? filename, string? contentType,
        string? textSidecar, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
        {
            throw new ArgumentException("A binary capture must have at least one byte.", nameof(bytes));
        }

        if (bytes.Length > maxBytes)
        {
            throw new ArgumentException(
                $"Payload is {bytes.Length} bytes, which exceeds the {maxBytes}-byte limit.", nameof(bytes));
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var resolvedContentType = string.IsNullOrWhiteSpace(contentType)
            ? ContentTypes.Guess(filename)
            : contentType.Trim();

        // A direct capture happened when we recorded it (occurred_at == recorded_at).
        var now = clock.UtcNow;
        var capture = new NewBinaryCapture(
            Kind: kind,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            Sha256: sha256,
            Bytes: bytes,
            ContentType: resolvedContentType,
            ByteSize: bytes.Length,
            Filename: string.IsNullOrWhiteSpace(filename) ? null : filename,
            TextContent: textSidecar ?? string.Empty);

        var result = await blobs.PutAsync(capture, cancellationToken);

        return new AttachmentResult(
            result.EventId, result.ArtifactId, result.Deduplicated,
            sha256, bytes.Length, resolvedContentType, kind);
    }
}

/// <summary>
/// The outcome of a binary capture: the event and artifact written (or reused on a
/// dedup hit), plus the metadata a caller needs to report or verify the write.
/// </summary>
public sealed record AttachmentResult(
    Guid EventId, Guid ArtifactId, bool Deduplicated,
    string Sha256, long ByteSize, string ContentType, string Kind);

using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Archives or restores a subject by appending an <c>archive_change</c> event
/// (<c>bsk archive</c> / <c>bsk restore</c>). The archive flag (D9) is orthogonal
/// to workflow status and is never mutated in place: like status, it moves only by
/// an append-only event, and <c>bsk.is_archived</c> folds the newest one per
/// subject. Nothing is ever deleted — archive merely hides a subject from default
/// views, and restore reverses it, with both recorded in history.
/// </summary>
public sealed class ArchiveService(SubjectService subjects, IEventStore events, IClock clock, string sourceId)
{
    /// <summary>Archives the subject — hides it from default views, reversibly.</summary>
    public Task<ArchiveResult> ArchiveAsync(
        string reference, CancellationToken cancellationToken = default)
        => WriteAsync(reference, archived: true, cancellationToken);

    /// <summary>Restores a previously archived subject back into default views.</summary>
    public Task<ArchiveResult> RestoreAsync(
        string reference, CancellationToken cancellationToken = default)
        => WriteAsync(reference, archived: false, cancellationToken);

    private async Task<ArchiveResult> WriteAsync(
        string reference, bool archived, CancellationToken cancellationToken)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);

        var payload = JsonSerializer.Serialize(new { subject_id = subject.Id.ToString(), archived });

        var now = clock.UtcNow;
        var newEvent = new NewEvent(
            Kind: EventKinds.ArchiveChange,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload);

        var eventId = await events.AppendAsync(newEvent, cancellationToken);
        return new ArchiveResult(eventId, subject, archived);
    }
}

/// <summary>The outcome of an archive / restore: the event and the subject.</summary>
public sealed record ArchiveResult(Guid EventId, SubjectRef Subject, bool Archived);

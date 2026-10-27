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
/// views, and restore reverses it, with both recorded in history. A space's name is unique among
/// the spaces not archived (HOME-1), so restoring one is refused while another uses its name,
/// whichever route restores it.
/// </summary>
public sealed class ArchiveService(
    SubjectService subjects, IEventStore events, IClock clock, string sourceId, TaskSeriesService? taskSeries = null,
    ISpaceRepository? spaces = null)
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

        // Areas are declared permanent (GEN-2 / D9 exemption): they become less
        // prominent, never archived. Enforced at the single writer, not just the UI.
        if (archived && subject.Type == SubjectTypes.Area)
        {
            throw new InvalidOperationException(
                "An Area is permanent and cannot be archived (GEN-2). Areas become less prominent, not archived.");
        }

        if (!archived && subject.Type == SubjectTypes.Space)
        {
            await CheckSpaceNameFreeAsync(subject.Id, cancellationToken);
        }

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

        // Archiving a repeating Task stops it; restoring it repeats again from today (D16).
        if (!archived && taskSeries is not null)
        {
            await taskSeries.AfterRestoreAsync(subject, cancellationToken);
        }

        return new ArchiveResult(eventId, subject, archived);
    }

    private async Task CheckSpaceNameFreeAsync(Guid id, CancellationToken cancellationToken)
    {
        if (spaces is null)
        {
            return;
        }

        var all = await spaces.GetSpacesAsync(cancellationToken);
        var space = all.FirstOrDefault(s => s.Id == id);
        if (space is not { Archived: true })
        {
            return;
        }

        if (all.Any(s => !s.Archived && s.Id != id
                         && string.Equals(s.Name.Trim(), space.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"You already have a space called '{space.Name}': rename one of them first.");
        }
    }
}

/// <summary>The outcome of an archive / restore: the event and the subject.</summary>
public sealed record ArchiveResult(Guid EventId, SubjectRef Subject, bool Archived);

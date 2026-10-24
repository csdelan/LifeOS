using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Adds and removes tags on an item — the write path behind <c>bsk tag &lt;item&gt;
/// +x -y</c> (GEN-1). An item is a subject (referenced by urn / short id / title) or
/// an event (referenced by its id): a bare uuid that names an existing event is
/// tagged as that event, otherwise the reference is resolved as a subject. Tags are
/// normalized before they hit the store, and this verb is deliberately separate from
/// <c>relate</c> / <c>link</c> — tags classify, relations connect.
/// </summary>
public sealed class TagService(
    SubjectService subjects, IEventReader events, ITagRepository tags, TaskSeriesService? taskSeries = null)
{
    public async Task<TagResult> ApplyAsync(
        string reference, IEnumerable<string> add, IEnumerable<string> remove,
        CancellationToken cancellationToken = default)
    {
        var toAdd = Tags.NormalizeAll(add);
        var toRemove = Tags.NormalizeAll(remove);
        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            throw new ArgumentException("Provide at least one tag to add (+tag) or remove (-tag).");
        }

        var item = await ResolveItemAsync(reference, cancellationToken);

        // Remove first so "+x -x" in one call is a deliberate net-add, not order-dependent.
        var removed = toRemove.Count == 0
            ? 0
            : await tags.RemoveAsync(item.IsEvent, item.Id, toRemove, cancellationToken);
        var added = toAdd.Count == 0
            ? 0
            : await tags.AddAsync(item.IsEvent, item.Id, toAdd, cancellationToken);

        // A repeating Task's tags follow to its open repetitions (D16).
        if (!item.IsEvent && taskSeries is not null)
        {
            await taskSeries.FollowTagsAsync(item.Id, toAdd, toRemove, cancellationToken);
        }

        return new TagResult(item, toAdd, toRemove, added, removed);
    }

    /// <summary>
    /// Renames a tag on every item that carries it (<c>bsk rename-tag</c>). A tag has
    /// no identity beyond its text, so a rename relabels each use; renaming onto a tag
    /// that already exists merges the two, and an item carrying both keeps one. Both
    /// names are normalized first, so a case-only change is a no-op.
    /// </summary>
    public async Task<TagRenameResult> RenameAsync(
        string oldTag, string newTag, CancellationToken cancellationToken = default)
    {
        var from = Tags.Normalize(oldTag) ?? throw new ArgumentException("The tag to rename is required.", nameof(oldTag));
        var to = Tags.Normalize(newTag) ?? throw new ArgumentException("A new tag name is required.", nameof(newTag));

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return new TagRenameResult(from, to, Renamed: 0, Merged: 0);
        }

        var (renamed, merged) = await tags.RenameAsync(from, to, cancellationToken);
        if (renamed == 0 && merged == 0)
        {
            // Nothing carries the old tag — almost always a typo, so say so rather than
            // reporting a silent success.
            throw new InvalidOperationException($"No item is tagged '{from}'.");
        }

        return new TagRenameResult(from, to, renamed, merged);
    }

    private async Task<TaggedItem> ResolveItemAsync(string reference, CancellationToken cancellationToken)
    {
        // A bare uuid is treated as an event id when it names a real event; this is
        // how a raw capture is tagged during triage before it is ever a subject.
        if (Guid.TryParse(reference?.Trim(), out var id))
        {
            var sourceEvent = await events.FindAsync(id, cancellationToken);
            if (sourceEvent is not null)
            {
                return new TaggedItem(IsEvent: true, sourceEvent.Id, $"event {sourceEvent.Id}");
            }
        }

        var subject = await subjects.ResolveAsync(reference!, cancellationToken);
        return new TaggedItem(IsEvent: false, subject.Id, subject.Urn);
    }
}

/// <summary>The item that was tagged — a subject or an event — and a label for output.</summary>
public sealed record TaggedItem(bool IsEvent, Guid Id, string Label);

/// <summary>
/// The outcome of a tag rename: the normalized names, how many uses were relabelled,
/// and how many were folded into an item's existing copy of the new tag.
/// </summary>
public sealed record TagRenameResult(string From, string To, int Renamed, int Merged);

/// <summary>The outcome of a tag operation: what was requested and what changed.</summary>
public sealed record TagResult(
    TaggedItem Item,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    int AddedCount,
    int RemovedCount);

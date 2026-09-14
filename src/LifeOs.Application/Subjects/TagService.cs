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
public sealed class TagService(SubjectService subjects, IEventReader events, ITagRepository tags)
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

        return new TagResult(item, toAdd, toRemove, added, removed);
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

/// <summary>The outcome of a tag operation: what was requested and what changed.</summary>
public sealed record TagResult(
    TaggedItem Item,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    int AddedCount,
    int RemovedCount);

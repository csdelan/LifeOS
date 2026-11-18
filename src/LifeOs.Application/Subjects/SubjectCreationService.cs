using System.Text.Json.Nodes;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Makes an item the way every door does (<c>bsk new</c>, <c>POST /api/subjects</c>), so the
/// steps that follow a create can't differ between them (code review CR7):
/// <list type="bullet">
///   <item>under a parent, the item and its edge in one transaction (GEN-7);</item>
///   <item>a Problem is unique by title, so making one again reuses it (D4);</item>
///   <item>a Problem or an Idea is a capture-like item and goes to the Inbox (GEN-14, CAP-6);</item>
///   <item>a repeating Task gets its first repetition at once, from its start (D16).</item>
/// </list>
/// An attribute with a verb of its own (a status, a bounty, ...) is refused, as an edit refuses it
/// (<see cref="ReservedAttributes"/>), and a Value without its statement is refused as
/// <see cref="SubjectService"/> refuses it (code review CR72). Checking the door's own arguments (a
/// relation without a parent) stays with the door, which knows what its options are called.
/// The Inbox flag and the first repetition run after the item's commit, so one that fails is
/// answered as pending, never thrown (<see cref="AfterCommit"/>, code review CR139): a throw reads
/// as "Nothing was written", and a retry makes a second Idea or Task.
/// </summary>
public sealed class SubjectCreationService(
    SubjectService subjects, ChildCreationService children, TriageService triage, TaskSeriesService taskSeries,
    AfterCommit afterCommit)
{
    /// <param name="type">A creatable type (<see cref="SubjectTypes.Creatable"/>), in any case.</param>
    /// <param name="title">The item's title.</param>
    /// <param name="attributesJson">The item's attributes, as a JSON object.</param>
    /// <param name="parent">The item to make it under, as a reference <see cref="SubjectService.ResolveAsync"/> reads.</param>
    /// <param name="relation">The edge to the parent; the type's default when null.</param>
    /// <param name="cancellationToken">Cancels the create.</param>
    public async Task<SubjectCreation> CreateAsync(
        string type, string title, string attributesJson, string? parent = null, string? relation = null,
        CancellationToken cancellationToken = default)
    {
        type = SubjectTypes.ParseCreatable(type);
        if (JsonNode.Parse(attributesJson) is JsonObject attributes)
        {
            ReservedAttributes.Check(type, attributes.Select(pair => pair.Key));
        }

        SubjectRef created;
        ChildCreationResult? link = null;
        var reused = false;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            link = await children.CreateChildAsync(type, title, parent, relation, attributesJson, cancellationToken);
            created = link.Child;
        }
        else if (type == SubjectTypes.Problem)
        {
            var resolved = await subjects.ResolveOrCreateAsync(type, title, attributesJson, cancellationToken);
            created = resolved.Subject;
            reused = !resolved.Created;
        }
        else
        {
            created = await subjects.CreateAsync(type, title, attributesJson, cancellationToken: cancellationToken);
        }

        var pending = false;
        if (type is SubjectTypes.Problem or SubjectTypes.Idea)
        {
            pending |= (await afterCommit.RunAsync(
                "flagging the new item for the Inbox", created.Urn,
                ct => triage.FlagItemAsync(isEvent: false, created.Id, ct), Guid.Empty)).Pending;
        }

        if (type == SubjectTypes.Task)
        {
            pending |= (await afterCommit.RunAsync(
                "making the new task's first repetition", created.Urn,
                ct => taskSeries.AfterPatternAsync(created, cancellationToken: ct), (IReadOnlyList<SubjectRef>)[])).Pending;
        }

        return new SubjectCreation(created, reused, link) { FollowUpsPending = pending };
    }
}

/// <summary>
/// What <see cref="SubjectCreationService.CreateAsync"/> made: the item, whether it was an existing
/// Problem of that title, and its edge to the parent when it was made under one.
/// </summary>
public sealed record SubjectCreation(SubjectRef Subject, bool Reused, ChildCreationResult? Link)
{
    /// <summary>True when the Inbox flag or the first repetition failed after the commit and is left to the catch-up.</summary>
    public bool FollowUpsPending { get; init; }
}

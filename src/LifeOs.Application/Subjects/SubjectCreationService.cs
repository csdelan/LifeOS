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
/// (<see cref="ReservedAttributes"/>). Checking the door's own arguments (a Value's statement, a
/// relation without a parent) stays with the door, which knows what its options are called.
/// </summary>
public sealed class SubjectCreationService(
    SubjectService subjects, ChildCreationService children, TriageService triage, TaskSeriesService taskSeries)
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

        if (type is SubjectTypes.Problem or SubjectTypes.Idea)
        {
            await triage.FlagItemAsync(isEvent: false, created.Id, cancellationToken);
        }

        if (type == SubjectTypes.Task)
        {
            await taskSeries.AfterPatternAsync(created, cancellationToken: cancellationToken);
        }

        return new SubjectCreation(created, reused, link);
    }
}

/// <summary>
/// What <see cref="SubjectCreationService.CreateAsync"/> made: the item, whether it was an existing
/// Problem of that title, and its edge to the parent when it was made under one.
/// </summary>
public sealed record SubjectCreation(SubjectRef Subject, bool Reused, ChildCreationResult? Link);

using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
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
    AfterCommit afterCommit, IEventReader events, EventSource eventSource)
{
    /// <param name="type">A creatable type (<see cref="SubjectTypes.Creatable"/>), in any case.</param>
    /// <param name="title">The item's title.</param>
    /// <param name="attributesJson">The item's attributes, as a JSON object.</param>
    /// <param name="parent">The item to make it under, as a reference <see cref="SubjectService.ResolveAsync"/> reads.</param>
    /// <param name="relation">The edge to the parent; the type's default when null.</param>
    /// <param name="externalId">
    /// A request id (agent-access plan §5.4, step 2.2 of §5.6): the item is born at its type's
    /// first status, which carries the id, so its source makes it once (invariant 5). A retry, or a
    /// call racing the first, answers the item the first call made,
    /// <see cref="SubjectCreation.AlreadyWritten"/>, and finishes what that call left undone. A
    /// Problem is unique by title, so making one again finds it with or without an id. An id its
    /// source used for another kind of write is refused.
    /// </param>
    /// <param name="cancellationToken">Cancels the create.</param>
    public async Task<SubjectCreation> CreateAsync(
        string type, string title, string attributesJson, string? parent = null, string? relation = null,
        string? externalId = null, CancellationToken cancellationToken = default)
    {
        type = SubjectTypes.ParseCreatable(type);
        if (JsonNode.Parse(attributesJson) is JsonObject attributes)
        {
            ReservedAttributes.Check(type, attributes.Select(pair => pair.Key));
        }

        var request = type == SubjectTypes.Problem || string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim();
        if (request is not null && await EarlierAsync(request, cancellationToken) is { } earlier)
        {
            return await FollowUpAsync(new SubjectCreation(earlier, Reused: false, Link: null) { AlreadyWritten = true }, cancellationToken);
        }

        SubjectRef created;
        ChildCreationResult? link = null;
        var reused = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(parent))
            {
                link = await children.CreateChildAsync(type, title, parent, relation, attributesJson, request, cancellationToken);
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
                created = await subjects.CreateAsync(type, title, attributesJson, externalId: request, cancellationToken: cancellationToken);
            }
        }
        catch (RequestAlreadyWrittenException) when (request is not null)
        {
            // A call with this id made the item while this one was making its own, which rolled back.
            var first = await EarlierAsync(request, cancellationToken)
                ?? throw new InvalidOperationException($"request_id '{request}' was written, but nothing was found under it.");
            return await FollowUpAsync(new SubjectCreation(first, Reused: false, Link: null) { AlreadyWritten = true }, cancellationToken);
        }

        return await FollowUpAsync(new SubjectCreation(created, reused, link), cancellationToken);
    }

    /// <summary>
    /// The item this source already made under <paramref name="request"/>, or null when it made
    /// none: the subject its first status change names. An id used for another write is refused.
    /// </summary>
    private async Task<SubjectRef?> EarlierAsync(string request, CancellationToken cancellationToken)
    {
        if (await events.FindWriteAsync(eventSource.Id, request, cancellationToken) is not { } earlier)
        {
            return null;
        }

        RequestReusedException.ThrowIfOtherKind(earlier.Kind, EventKinds.StateChange, request);
        return earlier.PayloadSubjectId is { } id
            ? await subjects.ResolveAsync(id.ToString(), cancellationToken)
            : throw new RequestReusedException(
                $"request_id '{request}' was already used for a different write. Send a new UUID for each write.");
    }

    /// <summary>
    /// What follows a create, after its commit: a Problem or an Idea goes to the Inbox, and a
    /// repeating Task gets its first repetition. For an item a retry finds, each is done only if
    /// its first call didn't: the flag only if the item was never triaged, so one Chris dealt with
    /// stays dealt with (code review CR63), and the repetition only if it isn't there.
    /// </summary>
    private async Task<SubjectCreation> FollowUpAsync(SubjectCreation made, CancellationToken cancellationToken)
    {
        var created = made.Subject;
        var pending = false;
        if (created.Type is SubjectTypes.Problem or SubjectTypes.Idea)
        {
            pending |= made.AlreadyWritten
                ? (await afterCommit.RunAsync(
                    "flagging the new item for the Inbox", created.Urn,
                    ct => triage.FlagIfNeverTriagedAsync(isEvent: false, created.Id, ct), false)).Pending
                : (await afterCommit.RunAsync(
                    "flagging the new item for the Inbox", created.Urn,
                    ct => triage.FlagItemAsync(isEvent: false, created.Id, ct), Guid.Empty)).Pending;
        }

        if (created.Type == SubjectTypes.Task)
        {
            pending |= (await afterCommit.RunAsync(
                "making the new task's first repetition", created.Urn,
                ct => taskSeries.AfterPatternAsync(created, cancellationToken: ct), (IReadOnlyList<SubjectRef>)[])).Pending;
        }

        return made with { FollowUpsPending = pending };
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

    /// <summary>
    /// A create with a request id its source already made: the item is the one the first call
    /// made, and nothing new was written. <see cref="Link"/> is then null.
    /// </summary>
    public bool AlreadyWritten { get; init; }
}

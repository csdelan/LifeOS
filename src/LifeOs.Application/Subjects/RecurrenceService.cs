using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Sets or clears a subject's structured recurrence in <c>attributes.recurrence</c>
/// (D8) — the write path behind <c>bsk recur</c>. The recurrence is stored as a real
/// jsonb <em>object</em> (not a string), so <c>bsk.recurrence_occurrences</c> can
/// expand it. Validation and canonicalization happen in <see cref="LifeOs.Domain.Recurrence"/>;
/// this just merges the object into the subject's attributes.
/// </summary>
public sealed class RecurrenceService(SubjectService subjects, ISubjectRepository repository)
{
    /// <summary>Sets the recurrence from a canonical recurrence JSON object string.</summary>
    public async Task<SubjectRef> SetAsync(
        string reference, string recurrenceJson, CancellationToken cancellationToken = default)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);

        var patch = new JsonObject { ["recurrence"] = JsonNode.Parse(recurrenceJson) };
        var updated = await repository.UpdateAttributesAsync(
            subject.Id, patch.ToJsonString(), [], cancellationToken);
        if (!updated)
        {
            throw new SubjectNotFoundException(reference);
        }

        return subject;
    }

    /// <summary>Removes any recurrence from the subject.</summary>
    public async Task<SubjectRef> ClearAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        var updated = await repository.UpdateAttributesAsync(
            subject.Id, "{}", ["recurrence"], cancellationToken);
        if (!updated)
        {
            throw new SubjectNotFoundException(reference);
        }

        return subject;
    }
}

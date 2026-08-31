using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Edits a subject's freeform <c>attributes</c> after creation — due dates, review
/// dates, cadence, and any other jsonb key. This is the one write path that changes
/// an attribute in place (subjects are interpreted truth, revisable). Status is
/// deliberately out of scope: it moves only by a <c>state_change</c> event
/// (<see cref="StatusService"/>), so the record of how a subject's state changed
/// stays append-only and explainable.
/// </summary>
public sealed class AttributeService(SubjectService subjects, ISubjectRepository repository)
{
    /// <summary>
    /// Sets or removes attributes on the subject named by <paramref name="reference"/>
    /// (urn, short id, or title). An assignment with a null/blank value removes its key.
    /// </summary>
    public async Task<AttributeUpdateResult> SetAsync(
        string reference, IReadOnlyList<AttributeAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        if (assignments.Count == 0)
        {
            throw new ArgumentException("At least one key=value assignment is required.", nameof(assignments));
        }

        var subject = await subjects.ResolveAsync(reference, cancellationToken);

        var patch = new JsonObject();
        var removed = new List<string>();
        foreach (var assignment in assignments)
        {
            var key = assignment.Key.Trim();
            if (key.Length == 0)
            {
                throw new ArgumentException("An attribute key must not be blank.", nameof(assignments));
            }

            if (string.IsNullOrWhiteSpace(assignment.Value))
            {
                removed.Add(key);
            }
            else
            {
                patch[key] = assignment.Value.Trim();
            }
        }

        var updated = await repository.UpdateAttributesAsync(
            subject.Id, patch.ToJsonString(), removed, cancellationToken);
        if (!updated)
        {
            // The subject resolved a moment ago; a missing row now is a real fault.
            throw new SubjectNotFoundException(reference);
        }

        var setKeys = patch.Select(pair => pair.Key).ToList();
        return new AttributeUpdateResult(subject, setKeys, removed);
    }
}

/// <summary>One attribute to set (non-blank value) or remove (null/blank value).</summary>
public sealed record AttributeAssignment(string Key, string? Value);

/// <summary>The outcome of a <c>set</c>: the subject and which keys were set / removed.</summary>
public sealed record AttributeUpdateResult(
    SubjectRef Subject, IReadOnlyList<string> SetKeys, IReadOnlyList<string> RemovedKeys);

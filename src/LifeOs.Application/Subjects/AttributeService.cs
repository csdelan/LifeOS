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
/// <para>
/// On a repeating Task's series (D16), an edit to a field its repetitions copy follows to the
/// open ones still holding the series' old value, and a new pattern or start re-plans it
/// (<see cref="TaskSeriesService"/>). A repetition's <c>series</c> and <c>occurrence</c> are
/// the series' to set.
/// </para>
/// <para>
/// A key with a verb of its own (a title, a status, a cover, a bounty, ...) is refused, from the
/// one table every generic write reads (<see cref="ReservedAttributes"/>).
/// </para>
/// </summary>
public sealed class AttributeService(
    SubjectService subjects, ISubjectRepository repository, TaskSeriesService taskSeries)
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
            throw new ArgumentException("At least one key=value assignment is required.");
        }

        var subject = await subjects.ResolveAsync(reference, cancellationToken);

        if (assignments.Any(a => a.Key.Trim().Length == 0))
        {
            throw new ArgumentException("An attribute key must not be blank.");
        }

        // A key with a verb of its own is refused before anything is written (code review CR49).
        ReservedAttributes.Check(subject.Type, assignments.Select(a => a.Key));

        // A Value keeps its statement (code review CR72): clearing it is refused in words.
        if (subject.Type == SubjectTypes.Value
            && assignments.Any(a => string.Equals(a.Key.Trim(), ValueStatements.AttributeKey, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(a.Value)))
        {
            throw new ArgumentException(ValueStatements.Missing);
        }

        if (subject.Type == SubjectTypes.Task)
        {
            var pattern = assignments.FirstOrDefault(a =>
                string.Equals(a.Key.Trim(), Recurrence.AttributeKey, StringComparison.OrdinalIgnoreCase));
            if (pattern is not null)
            {
                await taskSeries.GuardPatternAsync(
                    subject, clearing: string.IsNullOrWhiteSpace(pattern.Value), cancellationToken);
            }
        }

        var edit = await taskSeries.BeforeEditAsync(subject, assignments.Select(a => a.Key.Trim()), cancellationToken);

        var patch = new JsonObject();
        var removed = new List<string>();
        foreach (var assignment in assignments)
        {
            var key = assignment.Key.Trim();
            if (string.IsNullOrWhiteSpace(assignment.Value))
            {
                removed.Add(key);
            }
            else if (string.Equals(key, Recurrence.AttributeKey, StringComparison.OrdinalIgnoreCase))
            {
                // A recurrence is an object the expander reads (D8), never a string: text
                // holding one is checked and stored as the object.
                var canonical = Recurrence.Canonicalize(assignment.Value.Trim());
                if (subject.Type == SubjectTypes.Task)
                {
                    TaskSeries.CheckTaskPattern(canonical);
                }

                patch[Recurrence.AttributeKey] = JsonNode.Parse(canonical);
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

        if (subject.Type == SubjectTypes.Task)
        {
            if (edit is not null)
            {
                var values = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var key in setKeys)
                {
                    values[key] = patch[key] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
                }

                foreach (var key in removed)
                {
                    values[key] = null;
                }

                await taskSeries.FollowAsync(edit, values, cancellationToken);
            }

            if (setKeys.Concat(removed).Any(k => k is Recurrence.AttributeKey or TaskSeries.StartKey))
            {
                await taskSeries.AfterPatternAsync(subject, cancellationToken: cancellationToken);
            }
        }

        return new AttributeUpdateResult(subject, setKeys, removed);
    }
}

/// <summary>One attribute to set (non-blank value) or remove (null/blank value).</summary>
public sealed record AttributeAssignment(string Key, string? Value);

/// <summary>The outcome of a <c>set</c>: the subject and which keys were set / removed.</summary>
public sealed record AttributeUpdateResult(
    SubjectRef Subject, IReadOnlyList<string> SetKeys, IReadOnlyList<string> RemovedKeys);

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
/// </summary>
public sealed class AttributeService(
    SubjectService subjects, ISubjectRepository repository, TaskSeriesService? taskSeries = null)
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

        if (subject.Type == SubjectTypes.Task)
        {
            var reserved = assignments.FirstOrDefault(a =>
                string.Equals(a.Key.Trim(), TaskSeries.SeriesKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Key.Trim(), TaskSeries.OccurrenceKey, StringComparison.OrdinalIgnoreCase));
            if (reserved is not null)
            {
                throw new ArgumentException(
                    $"A Task's '{reserved.Key.Trim()}' is set by its repeating task, not `bsk set`.", nameof(assignments));
            }

            var pattern = assignments.FirstOrDefault(a =>
                string.Equals(a.Key.Trim(), Recurrence.AttributeKey, StringComparison.OrdinalIgnoreCase));
            if (pattern is not null && taskSeries is not null)
            {
                await taskSeries.GuardPatternAsync(
                    subject, clearing: string.IsNullOrWhiteSpace(pattern.Value), cancellationToken);
            }
        }

        var edit = taskSeries is null
            ? null
            : await taskSeries.BeforeEditAsync(subject, assignments.Select(a => a.Key.Trim()), cancellationToken);

        var patch = new JsonObject();
        var removed = new List<string>();
        foreach (var assignment in assignments)
        {
            var key = assignment.Key.Trim();
            if (key.Length == 0)
            {
                throw new ArgumentException("An attribute key must not be blank.", nameof(assignments));
            }

            // `title` is a first-class column, not an attribute. Writing it here would
            // silently create a shadow `title` key that is never read, so redirect to
            // the verb that actually changes it (keeps `set` = attributes only).
            if (string.Equals(key, "title", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Title is not an attribute — use `bsk rename` to change a subject's title.",
                    nameof(assignments));
            }

            // A cover is a checked Unsplash reference with one writer (D15). Written here it
            // would be a string no page can show, so point at the verb that sets it.
            if (string.Equals(key, Cover.AttributeKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Cover is set with `bsk cover` (a photo from Unsplash), not `bsk set`.",
                    nameof(assignments));
            }

            // A space's weight and pace are checked whole numbers the Home Score reads (HOME-1),
            // with one writer; written here they'd be text the score ignores.
            if (subject.Type == SubjectTypes.Space
                && (string.Equals(key, SpaceLimits.WeightKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, SpaceLimits.PaceKey, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "A space's weight and pace are set with `bsk space set`, not `bsk set`.",
                    nameof(assignments));
            }

            // A space's shots and their baselines (HOME-4) are checked JSON with one writer.
            if (subject.Type == SubjectTypes.Space
                && string.Equals(key, SpaceLimits.ShotsKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "A space's shots are set with `bsk space shot` and `bsk space baseline`, not `bsk set`.",
                    nameof(assignments));
            }

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

        if (taskSeries is not null && subject.Type == SubjectTypes.Task)
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

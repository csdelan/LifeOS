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
/// <para>
/// On a Task the pattern makes it a repeating Task (D16): <see cref="TaskSeriesService"/>
/// gives it a start and makes its first repetition, or re-dates the open one when the
/// pattern changes. Once each week and after the last completion are for Tasks only.
/// </para>
/// </summary>
public sealed class RecurrenceService(
    SubjectService subjects, ISubjectRepository repository, TaskSeriesService? taskSeries = null)
{
    /// <summary>Sets the recurrence from a canonical recurrence JSON object string.</summary>
    public async Task<SubjectRef> SetAsync(
        string reference, string recurrenceJson, CancellationToken cancellationToken = default)
        => (await SetAsync(reference, recurrenceJson, start: null, cancellationToken)).Subject;

    /// <summary>
    /// Sets the recurrence and, for a repeating Task, the date it starts from
    /// (<paramref name="start"/>; else its current start, its due date or today). Returns the
    /// repetitions it made.
    /// </summary>
    public async Task<RecurrenceResult> SetAsync(
        string reference, string recurrenceJson, DateOnly? start, CancellationToken cancellationToken = default)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        var canonical = Recurrence.Canonicalize(recurrenceJson);

        var kind = TaskSeries.KindOf(canonical);
        if (subject.Type != SubjectTypes.Task && kind is RepeatKind.EachWeek or RepeatKind.AfterCompletion)
        {
            throw new ArgumentException(
                "Once each week and after the last completion are for repeating Tasks.");
        }

        if (subject.Type == SubjectTypes.Task)
        {
            TaskSeries.CheckTaskPattern(canonical);
        }

        if (subject.Type != SubjectTypes.Task && start is not null)
        {
            throw new ArgumentException("A start date here is for repeating Tasks; set another subject's start with `bsk set`.");
        }

        if (taskSeries is not null)
        {
            await taskSeries.GuardPatternAsync(subject, clearing: false, cancellationToken);
        }

        var patch = new JsonObject { ["recurrence"] = JsonNode.Parse(canonical) };
        var updated = await repository.UpdateAttributesAsync(
            subject.Id, patch.ToJsonString(), [], cancellationToken);
        if (!updated)
        {
            throw new SubjectNotFoundException(reference);
        }

        var made = taskSeries is null
            ? []
            : await taskSeries.AfterPatternAsync(subject, start, cancellationToken);
        return new RecurrenceResult(subject, canonical, made);
    }

    /// <summary>Removes any recurrence from the subject.</summary>
    public async Task<SubjectRef> ClearAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        if (taskSeries is not null)
        {
            await taskSeries.GuardPatternAsync(subject, clearing: true, cancellationToken);
        }

        var updated = await repository.UpdateAttributesAsync(
            subject.Id, "{}", ["recurrence"], cancellationToken);
        if (!updated)
        {
            throw new SubjectNotFoundException(reference);
        }

        return subject;
    }
}

/// <summary>A recurrence set: the subject, the pattern as stored, and any repetitions it made (D16).</summary>
public sealed record RecurrenceResult(SubjectRef Subject, string Recurrence, IReadOnlyList<SubjectRef> Made);

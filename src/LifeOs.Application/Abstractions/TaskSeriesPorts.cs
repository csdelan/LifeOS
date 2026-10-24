using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

/// <summary>
/// A Task as a repeating Task sees it (D16): its pattern (a series has one), the start it's
/// counted from, its own status, the series it belongs to when it's a repetition, and the
/// latest repetition made from it. <see cref="Copied"/> holds its values for
/// <see cref="TaskSeries.CopiedKeys"/> that it has.
/// </summary>
public sealed record TaskSeriesState(
    SubjectRef Series,
    string? RecurrenceJson,
    DateOnly? Start,
    DateOnly? Due,
    string? Status,
    bool Archived,
    string? RepetitionOf,
    IReadOnlyDictionary<string, string> Copied,
    TaskRepetition? Latest)
{
    /// <summary>A series: a Task with a pattern that isn't itself a repetition.</summary>
    public bool IsSeries => RecurrenceJson is not null && RepetitionOf is null;

    /// <summary>Cancelled (or otherwise finished) or archived: it makes no more repetitions.</summary>
    public bool Stopped => Archived || StatusVocabulary.IsTerminal(Status ?? "");
}

/// <summary>One repetition of a series (<c>bsk.v_task_repetition</c>).</summary>
public sealed record TaskRepetition(
    Guid Id,
    string Urn,
    string Title,
    DateOnly Occurrence,
    DateOnly? Due,
    DateOnly? Scheduled,
    string? Status,
    bool Closed,
    DateOnly? ClosedOn,
    bool Archived);

/// <summary>A repetition to make: its urn and title, the date it stands for, and its attributes.</summary>
public sealed record NewTaskRepetition(string Urn, string Title, DateOnly Occurrence, string AttributesJson);

/// <summary>
/// Reads and writes repeating Tasks (D16). Every write to a series' repetitions runs under a
/// transaction-scoped advisory lock for that series and re-checks its latest repetition, and
/// the store holds one repetition per (series, occurrence), so two settles at once can't make
/// a date twice.
/// </summary>
public interface ITaskSeriesRepository
{
    /// <summary>The Task with id <paramref name="taskId"/>, or null when there's no such Task.</summary>
    Task<TaskSeriesState?> GetAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Every series still repeating: not Cancelled, not archived.</summary>
    Task<IReadOnlyList<Guid>> RepeatingAsync(CancellationToken cancellationToken = default);

    /// <summary>A series' repetitions, oldest date first.</summary>
    Task<IReadOnlyList<TaskRepetition>> RepetitionsAsync(string seriesUrn, CancellationToken cancellationToken = default);

    /// <summary>
    /// The first date of <paramref name="recurrenceJson"/> on or after <paramref name="from"/>,
    /// counted from <paramref name="anchor"/> (<c>bsk.recurrence_occurrences</c>), or null when
    /// it has none in the next few years.
    /// </summary>
    Task<DateOnly?> NextOccurrenceAsync(
        string recurrenceJson, DateOnly anchor, DateOnly from, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes <paramref name="repetitions"/>, each with the series' tags and parent links, in one
    /// transaction under the series' lock. Returns null, writing nothing, when the series' latest
    /// repetition is no longer <paramref name="expectedLatest"/> (another writer got there first).
    /// </summary>
    Task<IReadOnlyList<SubjectRef>?> CreateAsync(
        TaskSeriesState series, DateOnly? expectedLatest, IReadOnlyList<NewTaskRepetition> repetitions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an open repetition to a new date under the series' lock: merges
    /// <paramref name="patchJson"/> (its occurrence, due and do-date) and removes
    /// <paramref name="removeKeys"/>. Returns false, changing nothing, when another repetition
    /// already stands for that date.
    /// </summary>
    Task<bool> RedateAsync(
        Guid seriesId, Guid repetitionId, string patchJson, IReadOnlyList<string> removeKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a Task and, in the same transaction, its open repetitions that still carry
    /// <paramref name="previousTitle"/> (each copied it when it was made). For any other Task
    /// nothing else matches. Returns whether the Task existed and how many repetitions followed.
    /// </summary>
    Task<(bool Updated, int RepetitionsRenamed)> RenameWithOpenRepetitionsAsync(
        Guid id, string urn, string previousTitle, string newTitle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Follows a series edit of <paramref name="key"/> to its open repetitions that still carry
    /// the series' <paramref name="previousValue"/> (null: they don't have the key either):
    /// sets <paramref name="newValue"/>, or removes the key when it's null. Returns how many changed.
    /// </summary>
    Task<int> FollowAttributeAsync(
        string seriesUrn, string key, string? previousValue, string? newValue,
        CancellationToken cancellationToken = default);

    /// <summary>Adds and removes tags on a series' open repetitions. Returns how many repetitions there were.</summary>
    Task<int> FollowTagsAsync(
        string seriesUrn, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove,
        CancellationToken cancellationToken = default);
}

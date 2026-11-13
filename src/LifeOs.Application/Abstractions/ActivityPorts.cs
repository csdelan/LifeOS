using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// Oura activity as LifeOS keeps it (D14).

/// <summary>
/// The newest version LifeOS holds of one Oura record (D14): what identifies it, its day, when
/// it happened, its content hash (from its <c>external_id</c>), and whether it's voided.
/// </summary>
public sealed record OuraHead(string Key, DateOnly? Day, DateTimeOffset OccurredAt, string Hash, Guid EventId, bool Voided);

/// <summary>
/// A version of an Oura record to append: an <c>observed</c> event from source <c>oura</c>,
/// stamped with the database's clock as it's written, under the sync lock, so versions written
/// from different hosts order correctly.
/// </summary>
public sealed record OuraVersion(string Kind, DateTimeOffset OccurredAt, string ExternalId, string PayloadJson);

/// <summary>
/// Oura's records and Chris's workout notes as LifeOS holds them (ACTIVITY-1..4, D14, migration
/// 0037): versioned observed events, folded by <c>bsk.v_activity_day</c>, <c>bsk.v_workout</c>,
/// <c>bsk.v_sleep_period</c>, <c>bsk.v_sleep_day</c>, <c>bsk.v_readiness_day</c> and, since
/// migration 0039, <c>bsk.v_oura_tag</c>. Days are Oura's own.
/// </summary>
public interface IActivityRepository
{
    /// <summary>
    /// Starts writing a sync's versions under a lock every sync shares, so two syncs (the API
    /// and the CLI, say) take turns: the second sees what the first wrote.
    /// </summary>
    Task<IOuraSyncLock> BeginSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>Days of activity in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<ActivityDay>> GetActivityDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Live workouts on the days [<paramref name="from"/>, <paramref name="to"/>], newest first.</summary>
    Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>The live workout with that Oura id, or null.</summary>
    Task<Workout?> GetWorkoutAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Live workouts whose Oura id starts with <paramref name="prefix"/>, newest first.</summary>
    Task<IReadOnlyList<Workout>> FindWorkoutsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Days with a sleep score or a sleep period in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<SleepDay>> GetSleepDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Days with a readiness score in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<ReadinessDay>> GetReadinessDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Live sleep periods counting on the days [<paramref name="from"/>, <paramref name="to"/>], earliest first.</summary>
    Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Live tags starting on the days [<paramref name="from"/>, <paramref name="to"/>], newest first.</summary>
    Task<IReadOnlyList<ActivityTag>> GetTagsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// A sync's writes, under the lock every sync shares (<see cref="IActivityRepository.BeginSyncAsync"/>).
/// They take effect at <see cref="CommitAsync"/>; disposing without it rolls them back.
/// </summary>
public interface IOuraSyncLock : IAsyncDisposable
{
    /// <summary>
    /// The newest version of each record in <paramref name="collection"/> that is one of
    /// <paramref name="keys"/>, or whose newest version's day is in [<paramref name="from"/>,
    /// <paramref name="to"/>].
    /// </summary>
    Task<IReadOnlyList<OuraHead>> GetHeadsAsync(
        OuraCollection collection, IReadOnlyCollection<string> keys, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends the versions; one whose <c>external_id</c> is already held is skipped. Answers
    /// how many were written.
    /// </summary>
    Task<int> AppendAsync(IReadOnlyList<OuraVersion> versions, CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

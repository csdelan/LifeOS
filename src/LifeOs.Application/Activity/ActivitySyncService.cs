using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;

namespace LifeOs.Application.Activity;

/// <summary>
/// Syncs Oura, then settles the activity XP the days it brought in pay (activity plan §3.8), as
/// one step, so no door syncs without paying what the sync made due (code review CR46): the
/// Activity page's sync and <c>bsk oura sync</c>. A page sync skipped as too recent still
/// settles, since a day may have finished since. The settle runs after the sync has committed, so
/// one that fails is answered as pending, never thrown (code review CR129).
/// </summary>
public sealed class ActivitySyncService(OuraSyncService oura, ActivityRewards rewards, AfterCommit afterCommit)
{
    /// <summary>The Activity page's sync (<see cref="OuraSyncService.SyncForPageAsync"/>), then the settle.</summary>
    public async Task<SettledSync<OuraPageSync>> SyncForPageAsync(bool force, CancellationToken cancellationToken = default)
    {
        var sync = await oura.SyncForPageAsync(force, cancellationToken);
        var earned = await SettleAsync();
        return new SettledSync<OuraPageSync>(sync, earned.Value) { FollowUpsPending = earned.Pending };
    }

    /// <summary>A sync (<see cref="OuraSyncService.SyncAsync"/>), then the settle.</summary>
    public async Task<SettledSync<OuraSyncResult>> SyncAsync(
        OuraSyncOptions? options = null, CancellationToken cancellationToken = default)
    {
        var sync = await oura.SyncAsync(options, cancellationToken);
        var earned = await SettleAsync();
        return new SettledSync<OuraSyncResult>(sync, earned.Value) { FollowUpsPending = earned.Pending };
    }

    private Task<FollowUp<Earned>> SettleAsync()
        => afterCommit.RunAsync("settling the activity days", "an Oura sync", ct => rewards.SettleAsync(ct), Earned.Nothing);
}

/// <summary>What a sync did, and what the settle after it paid.</summary>
public sealed record SettledSync<T>(T Sync, Earned Earned)
{
    /// <summary>The sync landed, but the settle after it failed and is left to the catch-up.</summary>
    public bool FollowUpsPending { get; init; }
}

using LifeOs.Application.Rewards;

namespace LifeOs.Application.Activity;

/// <summary>
/// Syncs Oura, then settles the activity XP the days it brought in pay (activity plan §3.8), as
/// one step, so no door syncs without paying what the sync made due (code review CR46): the
/// Activity page's sync and <c>bsk oura sync</c>. A page sync skipped as too recent still
/// settles, since a day may have finished since.
/// </summary>
public sealed class ActivitySyncService(OuraSyncService oura, ActivityRewards rewards)
{
    /// <summary>The Activity page's sync (<see cref="OuraSyncService.SyncForPageAsync"/>), then the settle.</summary>
    public async Task<SettledSync<OuraPageSync>> SyncForPageAsync(bool force, CancellationToken cancellationToken = default)
    {
        var sync = await oura.SyncForPageAsync(force, cancellationToken);
        return new SettledSync<OuraPageSync>(sync, await rewards.SettleAsync(cancellationToken));
    }

    /// <summary>A sync (<see cref="OuraSyncService.SyncAsync"/>), then the settle.</summary>
    public async Task<SettledSync<OuraSyncResult>> SyncAsync(
        OuraSyncOptions? options = null, CancellationToken cancellationToken = default)
    {
        var sync = await oura.SyncAsync(options, cancellationToken);
        return new SettledSync<OuraSyncResult>(sync, await rewards.SettleAsync(cancellationToken));
    }
}

/// <summary>What a sync did, and what the settle after it paid.</summary>
public sealed record SettledSync<T>(T Sync, Earned Earned);

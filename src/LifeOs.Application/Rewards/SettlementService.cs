using LifeOs.Application.Activity;
using LifeOs.Application.Food;

namespace LifeOs.Application.Rewards;

/// <summary>
/// Brings the rewards up to date, the one way every door does it (code review CR46): the food
/// days that closed themselves (food-log plan §3.4), the activity days Oura has finished
/// (activity plan §3.8), then any rank-up the domains have reached, by time alone included
/// (xp-domains plan §3.7). Safe to repeat: nothing pays twice.
/// <para>
/// With <c>items</c>, it first heals every item whose own reconcile never ran (rewards plan §3.3).
/// That reads every item ever paid, so it grows with history: <c>bsk rewards reconcile</c> asks
/// for it, while the Character page, which settles each time it opens, leaves each item to the
/// reconcile its own write runs.
/// </para>
/// </summary>
public sealed class SettlementService(RewardService rewards, FoodRewards food, ActivityRewards activity)
{
    /// <summary>Every entry written, and each domain whose level or rank moved.</summary>
    public async Task<Earned> SettleAsync(bool items = false, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Award> healed = items ? await rewards.ReconcileAllAsync(cancellationToken) : [];
        var fed = await food.SettleAsync(cancellationToken: cancellationToken);
        var moved = await activity.SettleAsync(cancellationToken);
        var ranked = await rewards.SettleDomainsAsync(cancellationToken);
        return new Earned(
            [.. healed, .. fed.Awards, .. moved.Awards, .. ranked.Awards],
            [.. fed.Progress, .. moved.Progress, .. ranked.Progress]);
    }
}

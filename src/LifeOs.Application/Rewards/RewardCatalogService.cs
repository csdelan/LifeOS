using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// The reward catalog (REWARD-3) — the write path behind <c>bsk rewards add / price /
/// target / list</c>. A catalog entry is a <c>Reward</c> subject: an instant reward with a
/// price (<c>attributes.price</c>) or a savings goal with an optional dollar target
/// (<c>attributes.target_usd</c>). Rewards have no status; rename or archive them like any
/// subject. Redeeming against one goes through <see cref="RewardService"/>;
/// what has been redeemed is folded by <c>bsk.v_reward</c>, so nothing here is a balance.
/// </summary>
public sealed class RewardCatalogService(
    SubjectService subjects, ISubjectRepository repository, ILedgerRepository ledger)
{
    /// <summary>The catalog by name, with what has been redeemed against each; archived Rewards only when asked.</summary>
    public Task<IReadOnlyList<CatalogReward>> ListAsync(
        bool includeArchived = false, CancellationToken cancellationToken = default)
        => ledger.GetCatalogAsync(includeArchived, cancellationToken);

    /// <summary>One Reward's catalog entry, named by urn, short id or title.</summary>
    public async Task<CatalogReward> GetAsync(string reference, CancellationToken cancellationToken = default)
        => await ReadAsync(await ResolveRewardAsync(reference, cancellationToken), cancellationToken);

    /// <summary>
    /// Adds a Reward to the catalog. <paramref name="kind"/> is instant (the default) or
    /// savings. An instant reward may carry a <paramref name="price"/> in spendable
    /// currencies; a savings goal may carry a <paramref name="targetUsd"/> above zero
    /// instead. Refused, with nothing written, for an unknown kind, a price on a savings
    /// goal, a target on an instant reward, or an invalid price or target.
    /// </summary>
    public async Task<CatalogReward> AddAsync(
        string title, string? kind = null, IReadOnlyDictionary<string, long>? price = null,
        decimal? targetUsd = null, string? description = null,
        CancellationToken cancellationToken = default)
    {
        var rewardKind = string.IsNullOrWhiteSpace(kind)
            ? RewardKinds.Instant
            : RewardKinds.Find(kind) ?? throw new ArgumentException(
                $"A Reward is {string.Join(" or ", RewardKinds.All)}; got '{kind.Trim()}'.");

        var attributes = new JsonObject { [RewardAttributes.Kind] = rewardKind };
        if (price is { Count: > 0 })
        {
            if (rewardKind == RewardKinds.Savings)
            {
                throw new ArgumentException(
                    "A savings goal has no price: Coins go into it as you deposit them. Give it a target instead.");
            }

            attributes[RewardAttributes.Price] = RewardPrice.ToJson(RewardPrice.Normalize(price));
        }

        if (targetUsd is { } target)
        {
            if (rewardKind != RewardKinds.Savings)
            {
                throw new ArgumentException("Only a savings goal has a target; an instant reward has a price.");
            }

            attributes[RewardAttributes.TargetUsd] = NormalizeTarget(target);
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            attributes["description"] = description.Trim();
        }

        var reward = await subjects.CreateAsync(
            SubjectTypes.Reward, title, attributes.ToJsonString(), cancellationToken: cancellationToken);
        return await ReadAsync(reward, cancellationToken);
    }

    /// <summary>
    /// Sets an instant reward's catalog price to exactly <paramref name="price"/>. Past
    /// redemptions keep what they cost. Refused for a savings goal or an invalid price.
    /// </summary>
    public async Task<CatalogReward> SetPriceAsync(
        string reference, IReadOnlyDictionary<string, long> price, CancellationToken cancellationToken = default)
    {
        if (price is null || price.Count == 0)
        {
            throw new ArgumentException("Say what it costs, e.g. freedom=120.");
        }

        var normalized = RewardPrice.Normalize(price);
        var reward = await ResolveRewardAsync(reference, cancellationToken);
        var current = await ReadAsync(reward, cancellationToken);
        if (current.Kind == RewardKinds.Savings)
        {
            throw new InvalidOperationException(
                $"'{reward.Title}' is a savings goal: Coins go into it as you deposit them, so it has no price.");
        }

        await WriteAsync(
            reward,
            new JsonObject { [RewardAttributes.Price] = RewardPrice.ToJson(normalized) },
            [],
            cancellationToken);
        return await ReadAsync(reward, cancellationToken);
    }

    /// <summary>
    /// Sets a savings goal's target in dollars, or with <c>null</c> removes it. Deposits
    /// are untouched; only the progress against them changes. Refused for an instant
    /// reward or a target that isn't above zero.
    /// </summary>
    public async Task<CatalogReward> SetTargetAsync(
        string reference, decimal? targetUsd, CancellationToken cancellationToken = default)
    {
        var normalized = targetUsd is { } target ? NormalizeTarget(target) : (decimal?)null;
        var reward = await ResolveRewardAsync(reference, cancellationToken);
        var current = await ReadAsync(reward, cancellationToken);
        if (current.Kind != RewardKinds.Savings)
        {
            throw new InvalidOperationException(
                $"'{reward.Title}' is an instant reward: it has a price, not a target.");
        }

        if (normalized is { } value)
        {
            await WriteAsync(reward, new JsonObject { [RewardAttributes.TargetUsd] = value }, [], cancellationToken);
        }
        else
        {
            await WriteAsync(reward, new JsonObject(), [RewardAttributes.TargetUsd], cancellationToken);
        }

        return await ReadAsync(reward, cancellationToken);
    }

    private async Task<SubjectRef> ResolveRewardAsync(string reference, CancellationToken cancellationToken)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        return subject.Type == SubjectTypes.Reward
            ? subject
            : throw new InvalidOperationException($"'{subject.Title}' is a {subject.Type}, not a Reward.");
    }

    private async Task<CatalogReward> ReadAsync(SubjectRef reward, CancellationToken cancellationToken)
        => await ledger.GetCatalogRewardAsync(reward.Id, cancellationToken)
           ?? throw new SubjectNotFoundException(reward.Urn);

    private async Task WriteAsync(
        SubjectRef reward, JsonObject patch, IReadOnlyList<string> removeKeys, CancellationToken cancellationToken)
    {
        if (!await repository.UpdateAttributesAsync(reward.Id, patch.ToJsonString(), removeKeys, cancellationToken))
        {
            // The Reward resolved a moment ago; a missing row now is a real fault.
            throw new SubjectNotFoundException(reward.Urn);
        }
    }

    /// <summary>A target above zero, to the cent.</summary>
    private static decimal NormalizeTarget(decimal target)
    {
        var cents = Math.Round(target, 2, MidpointRounding.AwayFromZero);
        return cents > 0
            ? cents
            : throw new ArgumentException($"A savings target is a dollar amount above zero; got {target}.");
    }
}

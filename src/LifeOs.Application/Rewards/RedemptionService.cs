using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// Spending the wallet (REWARD-1 / REWARD-3 / D11) — the write path behind <c>bsk redeem</c>,
/// moved out of <see cref="RewardService"/>, which earns (code review CR19).
/// <para>
/// A redemption is <c>declared</c>, may only spend spendable currencies (XP and Resolve
/// are tracked, never spent), and is refused if it would take a balance below zero —
/// there is no overdraft. It is either against a catalog Reward (REWARD-3) — an instant
/// reward at its price, or Coins deposited into a savings goal — or a custom one whose
/// note says what it was for. Coins are real money: every redemption that spends them
/// records the dollars moved at the configured rate (<see cref="RewardService.CoinValueUsd"/>), so a
/// later rate change never rewrites a past deposit.
/// </para>
/// </summary>
public sealed class RedemptionService(
    ILedgerRepository ledger, SubjectService resolver, RewardService wallet, IClock clock, EventSource eventSource)
{
    private static readonly JsonSerializerOptions OmitNulls = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Spends <paramref name="amounts"/> (currency key → a whole number above zero) on
    /// something described by <paramref name="note"/> — a custom redemption, with no
    /// catalog Reward. Refused, with nothing written, when a currency is unknown or
    /// tracked, an amount is not positive, the note is blank, or any balance would go
    /// below zero.
    /// </summary>
    public Task<Redemption> RedeemAsync(
        IReadOnlyDictionary<string, long> amounts, string? note,
        CancellationToken cancellationToken = default)
        => RedeemAsync(reward: null, amounts, note, cancellationToken);

    /// <summary>
    /// Redeems against the catalog Reward named by <paramref name="reward"/> (urn, short id
    /// or title), or — when it is null — makes a custom redemption that needs a note.
    /// <list type="bullet">
    /// <item>An instant reward costs its catalog price; <paramref name="amounts"/>, when
    /// given, override it for this redemption.</item>
    /// <item>A savings goal takes the Coins in <paramref name="amounts"/> as a deposit,
    /// and only Coins.</item>
    /// </list>
    /// The note is optional against a Reward. Refused, with nothing written, for anything
    /// that isn't a Reward, an archived Reward, an instant reward with no price and no
    /// amounts, invalid amounts, or a spend any balance can't cover.
    /// </summary>
    public async Task<Redemption> RedeemAsync(
        string? reward, IReadOnlyDictionary<string, long>? amounts, string? note,
        CancellationToken cancellationToken = default)
    {
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (string.IsNullOrWhiteSpace(reward))
        {
            if (amounts is null || amounts.Count == 0)
            {
                throw new ArgumentException("Say what to redeem, e.g. freedom=45.");
            }

            var spend = RewardPrice.Normalize(amounts);
            if (trimmedNote is null)
            {
                throw new ArgumentException("Say what this redemption is for: a note is required.");
            }

            return await WriteRedemptionAsync(null, spend, trimmedNote, cancellationToken);
        }

        var subject = await resolver.ResolveAsync(reward, cancellationToken);
        if (subject.Type != SubjectTypes.Reward)
        {
            throw new InvalidOperationException(
                $"'{subject.Title}' is a {subject.Type}, not a Reward; redeem against a Reward, or give a note instead.");
        }

        var item = await ledger.GetCatalogRewardAsync(subject.Id, cancellationToken)
                   ?? throw new SubjectNotFoundException(subject.Urn);
        if (item.Archived)
        {
            throw new InvalidOperationException($"'{subject.Title}' is archived; restore it to redeem against it.");
        }

        var cost = item.Kind == RewardKinds.Savings ? DepositFor(item, amounts) : CostOf(item, amounts);
        return await WriteRedemptionAsync(subject, cost, trimmedNote, cancellationToken);
    }

    private async Task<Redemption> WriteRedemptionAsync(
        SubjectRef? reward, Dictionary<string, long> spend, string? note, CancellationToken cancellationToken)
    {
        // Coins are real money: stamp what this moved and the rate it moved at.
        decimal? dollars = null;
        decimal? rate = null;
        if (spend.TryGetValue(Currencies.Coins, out var coins))
        {
            rate = wallet.CoinValueUsd;
            dollars = CoinRates.Dollars(coins, wallet.CoinValueUsd);
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                entry = LedgerEntries.Redeem,
                amounts = spend.ToDictionary(kv => kv.Key, kv => -kv.Value),
                subject_id = reward?.Id.ToString(),
                note,
                dollars,
                coin_rate = rate
            },
            OmitNulls);

        var now = clock.UtcNow;
        var redeemEvent = new NewEvent(
            Kind: EventKinds.Ledger,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: eventSource.Id,
            PayloadJson: payload);
        IReadOnlyList<SubjectEventEdge> edges = reward is null
            ? []
            : [new SubjectEventEdge(reward.Id, SubjectEventRelations.Concerns, Provenances.Declared)];

        var ids = await ledger.AppendAsync(
            [new LedgerWrite(redeemEvent, edges, GuardBalance: true)], cancellationToken);

        var item = reward is null ? null : await ledger.GetCatalogRewardAsync(reward.Id, cancellationToken);
        return new Redemption(
            ids[0], spend, note, dollars, rate, item, await wallet.GetWalletAsync(cancellationToken));
    }

    /// <summary>A deposit into a savings goal: Coins, and only Coins.</summary>
    private static Dictionary<string, long> DepositFor(
        CatalogReward item, IReadOnlyDictionary<string, long>? amounts)
    {
        if (amounts is null || amounts.Count == 0)
        {
            throw new ArgumentException(
                $"Say how many Coins to put toward '{item.Reward.Title}', e.g. coins=50.");
        }

        var deposit = RewardPrice.Normalize(amounts);
        if (deposit.Keys.Any(k => k != Currencies.Coins))
        {
            throw new ArgumentException($"'{item.Reward.Title}' is a savings goal: only Coins go toward it.");
        }

        return deposit;
    }

    /// <summary>An instant reward's cost: the amounts given, else its catalog price.</summary>
    private static Dictionary<string, long> CostOf(
        CatalogReward item, IReadOnlyDictionary<string, long>? amounts)
    {
        if (amounts is { Count: > 0 })
        {
            return RewardPrice.Normalize(amounts);
        }

        return item.Price.Count > 0
            ? new Dictionary<string, long>(item.Price)
            : throw new ArgumentException(
                $"'{item.Reward.Title}' has no price yet: say what it costs this time, e.g. freedom=45, or give it a price.");
    }
}

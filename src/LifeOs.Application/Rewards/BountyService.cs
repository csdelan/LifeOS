using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// Prices an item (REWARD-2) — the write path behind <c>bsk bounty</c>. A bounty lives in
/// the item's <c>attributes.bounty</c>, trigger → amounts. An item that never names a
/// trigger pays its type default (<see cref="BountyDefaults"/>), so retuning a default
/// still reaches every item Chris never priced; changing a trigger stores its full set of
/// amounts from then on.
/// <para>
/// Pricing never changes what was already paid: each earn records its own amounts
/// (rewards plan §3.3). A bounty is read when the trigger is reconciled.
/// </para>
/// </summary>
public sealed class BountyService(SubjectService subjects, ISubjectRepository repository)
{
    /// <summary>What each of the item's triggers pays, and whether that is its own price or the default.</summary>
    public async Task<BountyView> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var subject = await ResolveEarningAsync(reference, cancellationToken);
        var stored = Bounty.Parse(await repository.GetAttributeValueAsync(
            subject.Id, Bounty.AttributeKey, cancellationToken));
        return View(subject, stored);
    }

    /// <summary>
    /// Sets currencies on a trigger's price, starting from what it pays now (the default
    /// when it has never been priced): <c>karma=3</c> adds Karma, <c>xp=0</c> drops XP.
    /// <paramref name="trigger"/> may be omitted when the type has only one. Refused, with
    /// nothing written, for an unknown currency, an amount below zero, a currency listed
    /// twice, or a trigger the type does not pay on.
    /// </summary>
    public async Task<BountyView> SetAsync(
        string reference, string? trigger, IReadOnlyDictionary<string, long> amounts,
        CancellationToken cancellationToken = default)
    {
        var changes = Normalize(amounts);
        var subject = await ResolveEarningAsync(reference, cancellationToken);
        var on = TriggerFor(subject, trigger);
        var stored = await ReadAsync(subject, cancellationToken);

        var price = Bounty.Price(subject.Type, on, stored);
        foreach (var (currency, amount) in changes)
        {
            if (amount == 0)
            {
                price.Remove(currency);
            }
            else
            {
                price[currency] = amount;
            }
        }

        stored[on] = Bounty.InDisplayOrder(price);
        return await WriteAsync(subject, stored, cancellationToken);
    }

    /// <summary>Makes a trigger pay nothing at all (stores an explicit <c>{}</c>).</summary>
    public async Task<BountyView> SetNothingAsync(
        string reference, string? trigger, CancellationToken cancellationToken = default)
    {
        var subject = await ResolveEarningAsync(reference, cancellationToken);
        var on = TriggerFor(subject, trigger);
        var stored = await ReadAsync(subject, cancellationToken);
        stored[on] = [];
        return await WriteAsync(subject, stored, cancellationToken);
    }

    /// <summary>
    /// Puts a trigger back on its type default — or, with no <paramref name="trigger"/>,
    /// every trigger, removing the bounty altogether.
    /// </summary>
    public async Task<BountyView> ClearAsync(
        string reference, string? trigger, CancellationToken cancellationToken = default)
    {
        var subject = await ResolveEarningAsync(reference, cancellationToken);
        var stored = await ReadAsync(subject, cancellationToken);
        if (string.IsNullOrWhiteSpace(trigger))
        {
            stored.Clear();
        }
        else
        {
            stored.Remove(TriggerFor(subject, trigger));
        }

        return await WriteAsync(subject, stored, cancellationToken);
    }

    private async Task<SubjectRef> ResolveEarningAsync(string reference, CancellationToken cancellationToken)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        if (!BountyTriggers.Earns(subject.Type))
        {
            throw new InvalidOperationException(
                $"'{subject.Title}' is a {subject.Type}; only {string.Join(", ", BountyTriggers.EarningTypes)} earn rewards.");
        }

        return subject;
    }

    private static string TriggerFor(SubjectRef subject, string? trigger)
    {
        var triggers = BountyTriggers.For(subject.Type);
        if (string.IsNullOrWhiteSpace(trigger))
        {
            return triggers.Count == 1
                ? triggers[0]
                : throw new ArgumentException(
                    $"Say which trigger to price: {string.Join(", ", triggers)}.");
        }

        if (string.Equals(trigger.Trim(), BountyTriggers.Partial, StringComparison.OrdinalIgnoreCase)
            && triggers.Contains(BountyTriggers.Followed))
        {
            throw new ArgumentException(
                $"A partial day always pays half of {BountyTriggers.Followed}; price {BountyTriggers.Followed} instead.");
        }

        return triggers.FirstOrDefault(t => string.Equals(t, trigger.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException(
                   $"A {subject.Type} doesn't pay on '{trigger.Trim()}'. Expected: {string.Join(", ", triggers)}.");
    }

    private static Dictionary<string, long> Normalize(IReadOnlyDictionary<string, long>? amounts)
    {
        if (amounts is null || amounts.Count == 0)
        {
            throw new ArgumentException("Say what it pays, e.g. xp=20 karma=3.");
        }

        var byKey = new Dictionary<string, long>();
        foreach (var (key, amount) in amounts)
        {
            var currency = Currencies.Find(key) ?? throw new ArgumentException(
                $"Unknown currency '{key}'. Expected one of: {string.Join(", ", Currencies.All.Select(c => c.Key))}.");

            if (amount < 0)
            {
                throw new ArgumentException(
                    $"A bounty pays a whole number of {currency.Label} from zero up; got {amount}.");
            }

            if (!byKey.TryAdd(currency.Key, amount))
            {
                throw new ArgumentException($"{currency.Label} is listed twice.");
            }
        }

        return byKey;
    }

    private async Task<Dictionary<string, Dictionary<string, long>>> ReadAsync(
        SubjectRef subject, CancellationToken cancellationToken)
        => Bounty.Parse(await repository.GetAttributeValueAsync(subject.Id, Bounty.AttributeKey, cancellationToken));

    private async Task<BountyView> WriteAsync(
        SubjectRef subject, Dictionary<string, Dictionary<string, long>> stored,
        CancellationToken cancellationToken)
    {
        var updated = stored.Count == 0
            ? await repository.UpdateAttributesAsync(subject.Id, "{}", [Bounty.AttributeKey], cancellationToken)
            : await repository.UpdateAttributesAsync(
                subject.Id,
                new JsonObject { [Bounty.AttributeKey] = Bounty.ToJson(stored) }.ToJsonString(),
                [],
                cancellationToken);
        if (!updated)
        {
            // The subject resolved a moment ago; a missing row now is a real fault.
            throw new SubjectNotFoundException(subject.Urn);
        }

        return View(subject, stored);
    }

    private static BountyView View(SubjectRef subject, IReadOnlyDictionary<string, Dictionary<string, long>> stored)
        => new(subject, BountyTriggers.For(subject.Type)
            .Select(trigger => new BountyLine(
                trigger, Bounty.Price(subject.Type, trigger, stored), IsCustom: stored.ContainsKey(trigger)))
            .ToList());
}

/// <summary>What one trigger pays, and whether that is the item's own price (else its type default).</summary>
public sealed record BountyLine(string Trigger, IReadOnlyDictionary<string, long> Amounts, bool IsCustom);

/// <summary>An item and what each of its triggers pays.</summary>
public sealed record BountyView(SubjectRef Subject, IReadOnlyList<BountyLine> Lines);

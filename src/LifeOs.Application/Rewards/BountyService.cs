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
        var (stored, every) = await ReadAsync(subject, cancellationToken);
        return View(subject, stored, every);
    }

    /// <summary>
    /// Sets currencies on a trigger's price, starting from what it pays now (the default
    /// when it has never been priced): <c>karma=3</c> adds Karma, <c>xp=0</c> drops XP.
    /// <paramref name="trigger"/> may be omitted when the type has only one, or for
    /// <c>completed</c> on a type that pays on completion. <paramref name="everyMinutes"/>
    /// sets how many minutes on the timer each <c>timed</c> payment takes; with it, the
    /// amounts may be left out. Refused, with nothing written, for an unknown currency, an
    /// amount below zero, a currency listed twice, a trigger the type does not pay on, or
    /// minutes that aren't a whole number from 1 up or aren't for <c>timed</c>.
    /// </summary>
    public async Task<BountyView> SetAsync(
        string reference, string? trigger, IReadOnlyDictionary<string, long>? amounts,
        int? everyMinutes = null, CancellationToken cancellationToken = default)
    {
        var changes = amounts is { Count: > 0 } || everyMinutes is null ? Normalize(amounts) : [];
        var subject = await ResolveEarningAsync(reference, cancellationToken);
        var on = TriggerFor(subject, trigger);
        CheckEveryMinutes(on, everyMinutes);
        var (stored, every) = await ReadAsync(subject, cancellationToken);

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
        return await WriteAsync(subject, stored, everyMinutes ?? every, cancellationToken);
    }

    /// <summary>
    /// Makes a trigger pay nothing at all (stores an explicit <c>{}</c>), optionally setting
    /// the <c>timed</c> trigger's <paramref name="everyMinutes"/> at the same time.
    /// </summary>
    public async Task<BountyView> SetNothingAsync(
        string reference, string? trigger, int? everyMinutes = null, CancellationToken cancellationToken = default)
    {
        var subject = await ResolveEarningAsync(reference, cancellationToken);
        var on = TriggerFor(subject, trigger);
        CheckEveryMinutes(on, everyMinutes);
        var (stored, every) = await ReadAsync(subject, cancellationToken);
        stored[on] = [];
        return await WriteAsync(subject, stored, everyMinutes ?? every, cancellationToken);
    }

    /// <summary>
    /// Puts a trigger back on its type default — or, with no <paramref name="trigger"/>,
    /// every trigger, removing the bounty altogether.
    /// </summary>
    public async Task<BountyView> ClearAsync(
        string reference, string? trigger, CancellationToken cancellationToken = default)
    {
        var subject = await ResolveEarningAsync(reference, cancellationToken);
        var (stored, every) = await ReadAsync(subject, cancellationToken);
        if (string.IsNullOrWhiteSpace(trigger))
        {
            stored.Clear();
        }
        else
        {
            stored.Remove(TriggerFor(subject, trigger));
        }

        // Clearing `timed` clears its minutes with it: ToJson only writes them inside it.
        return await WriteAsync(subject, stored, every, cancellationToken);
    }

    private static void CheckEveryMinutes(string trigger, int? everyMinutes)
    {
        if (everyMinutes is not { } minutes)
        {
            return;
        }

        if (!string.Equals(trigger, BountyTriggers.Timed, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Only {BountyTriggers.Timed} pays by the minutes on the timer; {trigger} pays once.");
        }

        if (minutes < 1)
        {
            throw new ArgumentException($"Pay for every whole number of minutes on the timer from 1 up; got {minutes}.");
        }
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
            return BountyTriggers.Implied(subject.Type)
                   ?? throw new ArgumentException(
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

    private async Task<(Dictionary<string, Dictionary<string, long>> Stored, int? EveryMinutes)> ReadAsync(
        SubjectRef subject, CancellationToken cancellationToken)
    {
        var json = await repository.GetAttributeValueAsync(subject.Id, Bounty.AttributeKey, cancellationToken)
                   ?? await SeriesBountyAsync(subject, cancellationToken);
        return (Bounty.Parse(json), Bounty.ParseEveryMinutes(json));
    }

    /// <summary>
    /// A repetition of a repeating Task without a price of its own pays its series' (D16), so
    /// it reads, and is priced from, the series' bounty: changing one trigger on it keeps the
    /// rest of the series' price rather than falling back to the defaults.
    /// </summary>
    private async Task<string?> SeriesBountyAsync(SubjectRef subject, CancellationToken cancellationToken)
        => subject.Type == SubjectTypes.Task
           && await repository.GetAttributeValueAsync(subject.Id, TaskSeries.SeriesKey, cancellationToken) is { } urn
           && await repository.FindByUrnAsync(urn, cancellationToken) is { } series
            ? await repository.GetAttributeValueAsync(series.Id, Bounty.AttributeKey, cancellationToken)
            : null;

    private async Task<BountyView> WriteAsync(
        SubjectRef subject, Dictionary<string, Dictionary<string, long>> stored, int? everyMinutes,
        CancellationToken cancellationToken)
    {
        var updated = stored.Count == 0
            ? await repository.UpdateAttributesAsync(subject.Id, "{}", [Bounty.AttributeKey], cancellationToken)
            : await repository.UpdateAttributesAsync(
                subject.Id,
                new JsonObject { [Bounty.AttributeKey] = Bounty.ToJson(stored, everyMinutes) }.ToJsonString(),
                [],
                cancellationToken);
        if (!updated)
        {
            // The subject resolved a moment ago; a missing row now is a real fault.
            throw new SubjectNotFoundException(subject.Urn);
        }

        return View(subject, stored, stored.ContainsKey(BountyTriggers.Timed) ? everyMinutes : null);
    }

    private static BountyView View(
        SubjectRef subject, IReadOnlyDictionary<string, Dictionary<string, long>> stored, int? everyMinutes)
        => new(subject, BountyTriggers.For(subject.Type)
            .Select(trigger => new BountyLine(
                trigger, Bounty.Price(subject.Type, trigger, stored), IsCustom: stored.ContainsKey(trigger),
                EveryMinutes: trigger == BountyTriggers.Timed ? everyMinutes ?? Bounty.DefaultEveryMinutes : null))
            .ToList());
}

/// <summary>
/// What one trigger pays, and whether that is the item's own price (else its type default).
/// For <c>timed</c>, <see cref="EveryMinutes"/> is how many minutes on the timer each
/// payment takes; null for every other trigger.
/// </summary>
public sealed record BountyLine(
    string Trigger, IReadOnlyDictionary<string, long> Amounts, bool IsCustom, int? EveryMinutes = null);

/// <summary>An item and what each of its triggers pays.</summary>
public sealed record BountyView(SubjectRef Subject, IReadOnlyList<BountyLine> Lines);

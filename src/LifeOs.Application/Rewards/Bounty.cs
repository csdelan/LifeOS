using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// Reads and writes an item's <c>attributes.bounty</c> (rewards plan §3.4): trigger →
/// amounts, e.g. <c>{"completed": {"xp": 10, "karma": 3}}</c>. A trigger the bounty does
/// not name pays its type default; an explicit <c>{}</c> pays nothing.
/// <para>
/// A Task's <c>timed</c> trigger also says how often it pays: <c>{"timed": {"xp": 1,
/// "every_minutes": 15}}</c> pays 1 XP for every 15 minutes on the timer. Without
/// <c>every_minutes</c> it pays every minute.
/// </para>
/// <para>
/// Reading is lenient — a bounty hand-edited into some other shape, a currency the
/// vocabulary no longer names, or an amount that is not a whole number above zero is
/// skipped rather than failing a status change. <see cref="BountyService"/> is the
/// strict write path.
/// </para>
/// </summary>
public static class Bounty
{
    /// <summary>The attribute that holds an item's bounty.</summary>
    public const string AttributeKey = "bounty";

    /// <summary>The key, inside the <c>timed</c> price, for how many minutes each payment takes.</summary>
    public const string EveryMinutesKey = "every_minutes";

    /// <summary>How many minutes on the timer each <c>timed</c> payment takes when the item doesn't say.</summary>
    public const int DefaultEveryMinutes = 1;

    /// <summary>
    /// The stored bounty, trigger → amounts (each in display order), from the raw
    /// <c>attributes-&gt;&gt;'bounty'</c> text, or empty when there is none or it is unreadable.
    /// </summary>
    public static Dictionary<string, Dictionary<string, long>> Parse(string? json)
    {
        var bounty = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return bounty;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return bounty;
        }

        if (root is not JsonObject triggers)
        {
            return bounty;
        }

        foreach (var (trigger, value) in triggers)
        {
            if (value is not JsonObject amounts)
            {
                continue;
            }

            var byKey = new Dictionary<string, long>();
            foreach (var (key, amount) in amounts)
            {
                if (Currencies.Find(key) is { } currency
                    && amount is JsonValue number
                    && number.TryGetValue<long>(out var whole)
                    && whole > 0)
                {
                    byKey[currency.Key] = whole;
                }
            }

            bounty[trigger] = InDisplayOrder(byKey);
        }

        return bounty;
    }

    /// <summary>
    /// What <paramref name="trigger"/> pays on an item of <paramref name="type"/>: its own
    /// bounty's amounts when it names the trigger, otherwise the type default. A partial
    /// habit day pays half of what a followed one does, rounded down; a currency that
    /// halves to nothing is dropped.
    /// </summary>
    public static Dictionary<string, long> Price(
        string type, string trigger, IReadOnlyDictionary<string, Dictionary<string, long>> stored)
    {
        if (string.Equals(trigger, BountyTriggers.Partial, StringComparison.OrdinalIgnoreCase))
        {
            return Price(type, BountyTriggers.Followed, stored)
                .Where(kv => kv.Value / 2 > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value / 2);
        }

        return stored.TryGetValue(trigger, out var custom)
            ? new Dictionary<string, long>(custom)
            : new Dictionary<string, long>(BountyDefaults.For(type, trigger));
    }

    /// <summary>
    /// The stored <c>timed</c> price's <c>every_minutes</c>, or null when it doesn't set one
    /// (or it isn't a whole number from 1 up) — read leniently, like <see cref="Parse"/>.
    /// </summary>
    public static int? ParseEveryMinutes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) is JsonObject root
                   && root[BountyTriggers.Timed] is JsonObject timed
                   && timed[EveryMinutesKey] is JsonValue value
                   && value.TryGetValue<int>(out var minutes)
                   && minutes >= 1
                ? minutes
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a completed task's timer time pays: <paramref name="perBlock"/> once for every
    /// <paramref name="everyMinutes"/> in <paramref name="seconds"/>, rounded down — nothing
    /// for less than one block.
    /// </summary>
    public static Dictionary<string, long> PriceTimed(
        IReadOnlyDictionary<string, long> perBlock, long seconds, int everyMinutes)
    {
        var blocks = seconds / (60L * Math.Max(everyMinutes, 1));
        return blocks <= 0
            ? []
            : InDisplayOrder(perBlock.ToDictionary(kv => kv.Key, kv => kv.Value * blocks));
    }

    /// <summary>
    /// The bounty as the jsonb value to store; <paramref name="everyMinutes"/>, when other than
    /// the default, goes into the <c>timed</c> price (and is dropped when there is none).
    /// </summary>
    public static JsonObject ToJson(
        IReadOnlyDictionary<string, Dictionary<string, long>> bounty, int? everyMinutes = null)
    {
        var root = new JsonObject();
        foreach (var (trigger, amounts) in bounty.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var node = new JsonObject();
            foreach (var (currency, amount) in amounts)
            {
                node[currency] = amount;
            }

            if (string.Equals(trigger, BountyTriggers.Timed, StringComparison.OrdinalIgnoreCase)
                && everyMinutes is { } minutes && minutes != DefaultEveryMinutes)
            {
                node[EveryMinutesKey] = minutes;
            }

            root[trigger] = node;
        }

        return root;
    }

    /// <summary>The amounts re-keyed in currency display order.</summary>
    public static Dictionary<string, long> InDisplayOrder(IReadOnlyDictionary<string, long> amounts)
        => Currencies.All
            .Where(c => amounts.ContainsKey(c.Key))
            .ToDictionary(c => c.Key, c => amounts[c.Key]);
}

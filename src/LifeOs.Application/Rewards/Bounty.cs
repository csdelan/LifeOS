using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// Reads and writes an item's <c>attributes.bounty</c> (rewards plan §3.4): trigger →
/// amounts, e.g. <c>{"completed": {"xp": 10, "karma": 3}}</c>. A trigger the bounty does
/// not name pays its type default; an explicit <c>{}</c> pays nothing.
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
    /// bounty's amounts when it names the trigger, otherwise the type default.
    /// </summary>
    public static Dictionary<string, long> Price(
        string type, string trigger, IReadOnlyDictionary<string, Dictionary<string, long>> stored)
        => stored.TryGetValue(trigger, out var custom)
            ? custom
            : new Dictionary<string, long>(BountyDefaults.For(type, trigger));

    /// <summary>The bounty as the jsonb value to store.</summary>
    public static JsonObject ToJson(IReadOnlyDictionary<string, Dictionary<string, long>> bounty)
    {
        var root = new JsonObject();
        foreach (var (trigger, amounts) in bounty.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var node = new JsonObject();
            foreach (var (currency, amount) in amounts)
            {
                node[currency] = amount;
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

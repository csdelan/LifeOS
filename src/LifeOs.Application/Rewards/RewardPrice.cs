using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// Amounts that can be spent — a redemption, or a Reward's catalog price
/// (<c>attributes.price</c>, currency → amount, e.g. <c>{"freedom": 120}</c>).
/// <para>
/// <see cref="Parse"/> is lenient, as reading a bounty is: a price hand-edited into
/// another shape, a currency that can't be spent, or an amount that isn't a whole number
/// above zero is skipped. <see cref="Normalize"/> is the strict check every write makes.
/// </para>
/// </summary>
public static class RewardPrice
{
    /// <summary>
    /// The price in a raw <c>attributes-&gt;&gt;'price'</c> text, keyed by currency in
    /// display order, or empty when there is none or it is unreadable.
    /// </summary>
    public static Dictionary<string, long> Parse(string? json)
    {
        var price = new Dictionary<string, long>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return price;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return price;
        }

        if (root is not JsonObject amounts)
        {
            return price;
        }

        foreach (var (key, amount) in amounts)
        {
            if (Currencies.Find(key) is { Spendable: true } currency
                && amount is JsonValue number
                && number.TryGetValue<long>(out var whole)
                && whole > 0)
            {
                price[currency.Key] = whole;
            }
        }

        return Bounty.InDisplayOrder(price);
    }

    /// <summary>
    /// Validates amounts to spend and returns them keyed by canonical currency key, in
    /// display order. Refused for an unknown or tracked currency, an amount that isn't
    /// above zero, or a currency named twice. Callers check for no amounts at all, so
    /// each can say what it needed.
    /// </summary>
    public static Dictionary<string, long> Normalize(IReadOnlyDictionary<string, long> amounts)
    {
        var byKey = new Dictionary<string, long>();
        foreach (var (key, amount) in amounts)
        {
            var currency = Currencies.Find(key) ?? throw new ArgumentException(
                $"Unknown currency '{key}'. Expected one of: {string.Join(", ", Currencies.All.Select(c => c.Key))}.");

            if (!currency.Spendable)
            {
                throw new ArgumentException($"{currency.Label} is tracked, not spent; it can't be redeemed.");
            }

            if (amount <= 0)
            {
                throw new ArgumentException($"Use a whole number of {currency.Label} above zero; got {amount}.");
            }

            if (!byKey.TryAdd(currency.Key, amount))
            {
                throw new ArgumentException($"{currency.Label} is listed twice.");
            }
        }

        return Bounty.InDisplayOrder(byKey);
    }

    /// <summary>The price as the jsonb value to store.</summary>
    public static JsonObject ToJson(IReadOnlyDictionary<string, long> price)
    {
        var node = new JsonObject();
        foreach (var (currency, amount) in price)
        {
            node[currency] = amount;
        }

        return node;
    }
}

namespace LifeOs.Domain;

/// <summary>
/// What can earn a reward (REWARD-1 / REWARD-2): the triggers each subject type pays
/// on. A bounty — <c>attributes.bounty</c> — maps these triggers to amounts. Types with
/// no triggers never earn.
/// <para>
/// <c>bsk.v_reward_due</c> (migration 0026) decides <em>when</em> each trigger is due
/// and must cover exactly the types listed here; a drift test keeps the two in step.
/// </para>
/// </summary>
public static class BountyTriggers
{
    /// <summary>The item reached its Completed status.</summary>
    public const string Completed = "completed";

    private static readonly Dictionary<string, string[]> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        [SubjectTypes.Task] = [Completed],
        [SubjectTypes.Project] = [Completed],
        [SubjectTypes.Goal] = [Completed],
    };

    /// <summary>The types that can earn rewards.</summary>
    public static IReadOnlyCollection<string> EarningTypes => ByType.Keys;

    /// <summary>The triggers <paramref name="type"/> pays on, or empty when it never earns.</summary>
    public static IReadOnlyList<string> For(string type)
        => ByType.TryGetValue(type, out var triggers) ? triggers : [];

    /// <summary>True when <paramref name="type"/> earns rewards at all.</summary>
    public static bool Earns(string type) => For(type).Count > 0;
}

/// <summary>
/// The default bounty per type and trigger (rewards plan §3.4) — what an item pays
/// when its own bounty does not name that trigger. Placeholders to tune by feel;
/// retuning one never rewrites a past earn, because each earn records its amounts.
/// Karma and Resolve default to zero: they are set per item.
/// </summary>
public static class BountyDefaults
{
    private static readonly Dictionary<(string Type, string Trigger), Dictionary<string, long>> Defaults = new()
    {
        [(SubjectTypes.Task, BountyTriggers.Completed)] = new()
        {
            [Currencies.Coins] = 2, [Currencies.Freedom] = 10, [Currencies.Xp] = 10
        },
        [(SubjectTypes.Project, BountyTriggers.Completed)] = new()
        {
            [Currencies.Coins] = 10, [Currencies.Freedom] = 30, [Currencies.Gems] = 1, [Currencies.Xp] = 50
        },
        [(SubjectTypes.Goal, BountyTriggers.Completed)] = new()
        {
            [Currencies.Coins] = 25, [Currencies.Freedom] = 60, [Currencies.Gems] = 3, [Currencies.Xp] = 150
        },
    };

    /// <summary>
    /// The default amounts for <paramref name="trigger"/> on <paramref name="type"/>, in
    /// display order, or empty when there is none.
    /// </summary>
    public static IReadOnlyDictionary<string, long> For(string type, string trigger)
    {
        var key = (Canonical(type), trigger);
        return Defaults.TryGetValue(key, out var amounts)
            ? Currencies.All.Where(c => amounts.ContainsKey(c.Key)).ToDictionary(c => c.Key, c => amounts[c.Key])
            : new Dictionary<string, long>();
    }

    private static string Canonical(string type)
        => BountyTriggers.EarningTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase))
           ?? type;
}

/// <summary>
/// Award keys (rewards plan §3.2): one per thing that can be paid for, so the ledger can
/// say whether it is currently paid (its entries net to non-zero) and reconcile can give
/// back exactly what it paid.
/// </summary>
public static class AwardKeys
{
    /// <summary>The key for completing <paramref name="subjectId"/>.</summary>
    public static string Completed(Guid subjectId) => $"{BountyTriggers.Completed}:{subjectId}";

    /// <summary>The trigger an award key belongs to: the part before its first <c>:</c>.</summary>
    public static string TriggerOf(string awardKey)
    {
        var separator = awardKey.IndexOf(':');
        return separator > 0 ? awardKey[..separator] : awardKey;
    }
}

namespace LifeOs.Domain;

/// <summary>
/// What can earn a reward (REWARD-1 / REWARD-2): the triggers each subject type pays
/// on. A bounty — <c>attributes.bounty</c> — maps these triggers to amounts. Types with
/// no triggers never earn.
/// <para>
/// <c>bsk.v_reward_trigger</c> (migrations 0026, 0027) decides <em>when</em> each trigger
/// is due and must cover exactly the types and triggers listed here; a drift test keeps
/// the two in step.
/// </para>
/// </summary>
public static class BountyTriggers
{
    /// <summary>A Task, Project or Goal reached its Completed status.</summary>
    public const string Completed = "completed";

    /// <summary>A habit occurrence was followed.</summary>
    public const string Followed = "followed";

    /// <summary>
    /// A habit occurrence was partly followed. Not priced on its own: it pays half the
    /// <see cref="Followed"/> bounty, rounded down.
    /// </summary>
    public const string Partial = "partial";

    /// <summary>A habit reached its Established status.</summary>
    public const string Established = "established";

    /// <summary>The streak lengths that pay a milestone bonus.</summary>
    public static readonly IReadOnlyList<int> StreakMilestones = [7, 30, 100];

    /// <summary>The trigger for a run of <paramref name="days"/> followed occurrences, e.g. <c>streak_7</c>.</summary>
    public static string Streak(int days) => $"streak_{days}";

    private static readonly Dictionary<string, string[]> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        [SubjectTypes.Task] = [Completed],
        [SubjectTypes.Project] = [Completed],
        [SubjectTypes.Goal] = [Completed],
        [SubjectTypes.Habit] = [Followed, .. StreakMilestones.Select(Streak), Established],
    };

    /// <summary>The types that can earn rewards.</summary>
    public static IReadOnlyCollection<string> EarningTypes => ByType.Keys;

    /// <summary>
    /// The triggers <paramref name="type"/> can be priced on, or empty when it never
    /// earns. A Habit's <see cref="Partial"/> is not among them: it follows Followed.
    /// </summary>
    public static IReadOnlyList<string> For(string type)
        => ByType.TryGetValue(type, out var triggers) ? triggers : [];

    /// <summary>True when <paramref name="type"/> earns rewards at all.</summary>
    public static bool Earns(string type) => For(type).Count > 0;

    /// <summary>
    /// True when <paramref name="trigger"/> can pay on <paramref name="type"/>: a priced
    /// trigger, or <see cref="Partial"/> where the type pays on <see cref="Followed"/>.
    /// </summary>
    public static bool Pays(string type, string trigger)
    {
        var triggers = For(type);
        return triggers.Contains(trigger, StringComparer.OrdinalIgnoreCase)
               || (string.Equals(trigger, Partial, StringComparison.OrdinalIgnoreCase)
                   && triggers.Contains(Followed));
    }
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
        [(SubjectTypes.Habit, BountyTriggers.Followed)] = new()
        {
            [Currencies.Coins] = 1, [Currencies.Freedom] = 5, [Currencies.Xp] = 5
        },
        [(SubjectTypes.Habit, BountyTriggers.Streak(7))] = new()
        {
            [Currencies.Coins] = 5, [Currencies.Freedom] = 15, [Currencies.Xp] = 25
        },
        [(SubjectTypes.Habit, BountyTriggers.Streak(30))] = new()
        {
            [Currencies.Coins] = 15, [Currencies.Freedom] = 30, [Currencies.Xp] = 75
        },
        [(SubjectTypes.Habit, BountyTriggers.Streak(100))] = new()
        {
            [Currencies.Coins] = 40, [Currencies.Freedom] = 90, [Currencies.Gems] = 1, [Currencies.Xp] = 200
        },
        [(SubjectTypes.Habit, BountyTriggers.Established)] = new()
        {
            [Currencies.Coins] = 25, [Currencies.Freedom] = 60, [Currencies.Gems] = 1, [Currencies.Xp] = 100
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
/// back exactly what it paid. <c>bsk.v_reward_trigger</c> builds the same strings.
/// </summary>
public static class AwardKeys
{
    /// <summary>The key for completing <paramref name="subjectId"/>.</summary>
    public static string Completed(Guid subjectId) => $"{BountyTriggers.Completed}:{subjectId}";

    /// <summary>The key for establishing <paramref name="habitId"/>.</summary>
    public static string Established(Guid habitId) => $"{BountyTriggers.Established}:{habitId}";

    /// <summary>The key for one habit day, followed or partial: <c>day:&lt;habit&gt;:&lt;date&gt;:&lt;result&gt;</c>.</summary>
    public static string Day(Guid habitId, DateOnly occurrence, string result)
        => $"day:{habitId}:{occurrence:yyyy-MM-dd}:{result}";

    /// <summary>The key for a streak milestone of a run starting on <paramref name="runStart"/>.</summary>
    public static string Streak(int days, Guid habitId, DateOnly runStart)
        => $"{BountyTriggers.Streak(days)}:{habitId}:{runStart:yyyy-MM-dd}";

    /// <summary>
    /// The trigger an award key belongs to: a day key's last part (its result), otherwise
    /// the part before the first <c>:</c>.
    /// </summary>
    public static string TriggerOf(string awardKey)
    {
        if (awardKey.StartsWith("day:", StringComparison.Ordinal))
        {
            return awardKey[(awardKey.LastIndexOf(':') + 1)..];
        }

        var separator = awardKey.IndexOf(':');
        return separator > 0 ? awardKey[..separator] : awardKey;
    }
}

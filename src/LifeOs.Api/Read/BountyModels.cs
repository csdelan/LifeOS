using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// What one trigger of an item pays (REWARD-2): its own price when <see cref="IsCustom"/>,
/// otherwise its type default, which <see cref="Default"/> always carries so a form can
/// show both and put the trigger back on it.
/// </summary>
public sealed class BountyLineRow
{
    public string Trigger { get; set; } = "";
    public IReadOnlyDictionary<string, long> Amounts { get; set; } = new Dictionary<string, long>();
    public bool IsCustom { get; set; }
    public IReadOnlyDictionary<string, long> Default { get; set; } = new Dictionary<string, long>();
}

/// <summary><c>POST /api/bounty</c>: the item and what each of its triggers pays now.</summary>
public sealed class BountyResult
{
    public CreatedSubject Subject { get; set; } = new();
    public IReadOnlyList<BountyLineRow> Lines { get; set; } = [];
}

/// <summary>
/// <c>GET /api/bounty/defaults</c>: what each trigger of an earning type pays when an item
/// never names it — the values a create form pre-fills.
/// </summary>
public sealed class BountyTypeDefaults
{
    public string Type { get; set; } = "";
    public IReadOnlyList<BountyLineRow> Lines { get; set; } = [];
}

/// <summary>
/// A Building habit's progress toward its establish target, from
/// <c>bsk.v_habit_establishment</c>: adherence over the window (partial = half) and whether
/// the target is met — the "Ready to establish" suggestion. Read-only; confirming is an
/// ordinary status change to Established.
/// </summary>
public sealed class HabitEstablishment
{
    public decimal MinAdherence { get; set; }
    public int WindowDays { get; set; }
    public string WindowStart { get; set; } = "";
    public long Followed { get; set; }
    public long Partial { get; set; }
    public long Missed { get; set; }
    public long Concluded { get; set; }
    /// <summary>0..1, or null before any occurrence in the window has concluded.</summary>
    public decimal? Adherence { get; set; }
    public int AgeDays { get; set; }
    public bool TargetMet { get; set; }
}

/// <summary>
/// <c>POST /api/habits/target</c> (<c>bsk habit target</c>): the habit's establish target as
/// <c>bsk.v_habit_establishment</c> reads it — the minimum adherence as a fraction (0.8) and
/// the window in days — and whether each part is the habit's own or the default.
/// </summary>
public sealed class HabitTargetResult
{
    public CreatedSubject Habit { get; set; } = new();
    public decimal MinAdherence { get; set; }
    public int WindowDays { get; set; }
    public bool AdherenceIsCustom { get; set; }
    public bool WindowIsCustom { get; set; }

    public static HabitTargetResult From(HabitTargetView view) => new()
    {
        Habit = new CreatedSubject
        {
            Id = view.Habit.Id,
            Urn = view.Habit.Urn,
            Type = view.Habit.Type,
            Title = view.Habit.Title
        },
        MinAdherence = view.MinAdherence,
        WindowDays = view.WindowDays,
        AdherenceIsCustom = view.AdherenceIsCustom,
        WindowIsCustom = view.WindowIsCustom
    };
}

/// <summary>Builds the bounty shapes from an item's raw <c>attributes-&gt;&gt;'bounty'</c> text.</summary>
public static class BountyRows
{
    /// <summary>Every trigger the type pays on, priced; null for a type that doesn't earn.</summary>
    public static IReadOnlyList<BountyLineRow>? Lines(string type, string? bountyJson)
    {
        if (!BountyTriggers.Earns(type))
        {
            return null;
        }

        var stored = Bounty.Parse(bountyJson);
        return BountyTriggers.For(type).Select(trigger => new BountyLineRow
        {
            Trigger = trigger,
            Amounts = Bounty.Price(type, trigger, stored),
            IsCustom = stored.ContainsKey(trigger),
            Default = BountyDefaults.For(type, trigger)
        }).ToList();
    }

    /// <summary>
    /// What the type's first trigger pays — completing a Task / Project / Goal, following a
    /// Habit for a day — for a list's chip; null for a type that doesn't earn.
    /// </summary>
    public static IReadOnlyDictionary<string, long>? Headline(string type, string? bountyJson)
        => BountyTriggers.Earns(type)
            ? Bounty.Price(type, BountyTriggers.For(type)[0], Bounty.Parse(bountyJson))
            : null;

    public static IReadOnlyList<BountyTypeDefaults> Defaults()
        => BountyTriggers.EarningTypes
            .Select(type => new BountyTypeDefaults { Type = type, Lines = Lines(type, null)! })
            .ToList();

    public static BountyResult From(BountyView view) => new()
    {
        Subject = new CreatedSubject
        {
            Id = view.Subject.Id,
            Urn = view.Subject.Urn,
            Type = view.Subject.Type,
            Title = view.Subject.Title
        },
        Lines = view.Lines.Select(line => new BountyLineRow
        {
            Trigger = line.Trigger,
            Amounts = line.Amounts,
            IsCustom = line.IsCustom,
            Default = BountyDefaults.For(view.Subject.Type, line.Trigger)
        }).ToList()
    };
}

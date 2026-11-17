using LifeOs.Api.Mcp.Tools;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Api.Mcp;

/// <summary>
/// The MCP server's settings (agent-access plan §4.2), read once at startup. They're settings,
/// not a toggle on a page, so neither an agent nor a stolen browser session can change them; a
/// change is <c>fly secrets set</c> and a restart. An unknown value fails startup.
/// </summary>
public sealed class McpOptions
{
    public const string EnabledKey = "Mcp:Enabled";
    public const string DomainsKey = "Mcp:Domains";
    public const string BlockedClientsKey = "Mcp:BlockedClients";
    public const string WritesKey = "Mcp:Writes";
    public const string WriteClientsKey = "Mcp:WriteClients";

    /// <summary>The optional areas, besides the core that's always on (§4.3).</summary>
    public static readonly IReadOnlyList<string> AllDomains = [McpDomains.Health, McpDomains.Food, McpDomains.Rewards, McpDomains.Home];

    /// <summary>Off by default: <c>/mcp</c> and its metadata answer 404. The kill switch.</summary>
    public bool Enabled { get; init; }

    /// <summary>The optional areas agents can read.</summary>
    public IReadOnlySet<string> Domains { get; init; } = new HashSet<string>(AllDomains, StringComparer.OrdinalIgnoreCase);

    /// <summary>Client ids refused at once, even with a token that hasn't expired.</summary>
    public IReadOnlySet<string> BlockedClients { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// What agents may write (§5.1): <see cref="McpWrites.Off"/>, the default, or
    /// <see cref="McpWrites.Capture"/>, which adds to the Inbox and journals and clears raw
    /// captures from the Inbox. <c>full</c> isn't built yet.
    /// </summary>
    public string Writes { get; init; } = McpWrites.Off;

    /// <summary>
    /// The clients that may write when writes are on. A client must also be on LifeOS's list and
    /// act as an AI-agent Person (§5.1); every other client stays read-only.
    /// </summary>
    public IReadOnlySet<string> WriteClients { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public bool WritesOn => Writes != McpWrites.Off;

    /// <summary>Whether writes are on and <paramref name="clientId"/> is named in <c>Mcp:WriteClients</c>.</summary>
    public bool NamesWriter(string clientId) => WritesOn && WriteClients.Contains(clientId.Trim());

    public bool Allows(string domain) => Domains.Contains(domain);

    /// <summary>The optional areas switched off, which every tool leaves out.</summary>
    public IReadOnlySet<string> Excluded
        => AllDomains.Where(d => !Allows(d)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether agents may see items of <paramref name="type"/>: a Reward belongs to the rewards
    /// area, a Space to the home area and a Metric to the health area (MEASURE-6), so each goes
    /// when its area is off.
    /// </summary>
    public bool AllowsType(string type)
        => !HiddenTypes.Contains(type, StringComparer.Ordinal);

    /// <summary>The item types <see cref="AllowsType"/> leaves out, for reads that filter in SQL.</summary>
    public IReadOnlyCollection<string> HiddenTypes
        => McpAreas.All.Where(a => !Allows(a.Name)).SelectMany(a => a.Types).ToList();

    /// <summary>
    /// Whether agents may see the attribute <paramref name="key"/> on an item of any type: a
    /// bounty is the rewards area's, so it goes when that area is off (code review CR115).
    /// </summary>
    public bool AllowsAttribute(string key)
        => !McpAreas.All.Any(a => !Allows(a.Name) && a.AttributeKeys.Contains(key, StringComparer.Ordinal));

    /// <summary>The attribute keys <see cref="AllowsAttribute"/> leaves out.</summary>
    public IReadOnlySet<string> HiddenAttributes
        => McpAreas.All.Where(a => !Allows(a.Name)).SelectMany(a => a.AttributeKeys).ToHashSet(StringComparer.Ordinal);

    public bool IsBlocked(string clientId) => BlockedClients.Contains(clientId.Trim());

    /// <summary>
    /// Reads the settings. <c>Mcp:Domains</c> unset means every area, <c>none</c> means the core
    /// only; <c>Mcp:Writes</c> unset means off.
    /// </summary>
    public static McpOptions From(IConfiguration configuration)
    {
        var enabled = configuration[EnabledKey]?.Trim();
        var on = enabled switch
        {
            null or "" => false,
            _ when bool.TryParse(enabled, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"{EnabledKey} must be true or false, got '{enabled}'.")
        };

        return new McpOptions
        {
            Enabled = on,
            Domains = ParseDomains(configuration[DomainsKey]),
            BlockedClients = Split(configuration[BlockedClientsKey]).ToHashSet(StringComparer.Ordinal),
            Writes = ParseWrites(configuration[WritesKey]),
            WriteClients = Split(configuration[WriteClientsKey]).ToHashSet(StringComparer.Ordinal),
        };
    }

    private static string ParseWrites(string? text)
    {
        var writes = text?.Trim();
        if (string.IsNullOrEmpty(writes) || string.Equals(writes, McpWrites.Off, StringComparison.OrdinalIgnoreCase))
        {
            return McpWrites.Off;
        }

        if (string.Equals(writes, McpWrites.Capture, StringComparison.OrdinalIgnoreCase))
        {
            return McpWrites.Capture;
        }

        throw new InvalidOperationException(string.Equals(writes, "full", StringComparison.OrdinalIgnoreCase)
            ? $"{WritesKey} is full, which isn't built yet (agent-access plan §5.2). Use off or capture."
            : $"{WritesKey} must be off or capture, got '{writes}'.");
    }

    private static HashSet<string> ParseDomains(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HashSet<string>(AllDomains, StringComparer.OrdinalIgnoreCase);
        }

        if (string.Equals(text.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in Split(text))
        {
            if (!AllDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{DomainsKey} names '{domain}', which isn't an area. Use any of {string.Join(", ", AllDomains)}, or none.");
            }

            domains.Add(domain);
        }

        return domains;
    }

    private static IEnumerable<string> Split(string? text)
        => (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>The levels of <c>Mcp:Writes</c> (agent-access plan §5.1).</summary>
public static class McpWrites
{
    public const string Off = "off";

    /// <summary>
    /// <c>capture</c>, <c>add_journal_entry</c> and <c>dismiss</c>: they add, and clear a raw
    /// capture from the Inbox, and change nothing else (§5.2, notes plan N7).
    /// </summary>
    public const string Capture = "capture";
}

/// <summary>The optional areas' names, as <c>Mcp:Domains</c> spells them.</summary>
public static class McpDomains
{
    /// <summary>Oura: sleep, readiness, steps, workouts and tags (Chris accepted the risk, §2 decision 3).</summary>
    public const string Health = "health";
    public const string Food = "food";
    public const string Rewards = "rewards";
    public const string Home = "home";
}

/// <summary>
/// What belongs to one optional area. An event is the area's when its payload holds one of
/// <see cref="PayloadKeys"/>, it's about an item of one of <see cref="Types"/>, it came from one
/// of <see cref="Sources"/>, it's one of <see cref="Kinds"/>, or it changes one of
/// <see cref="AttributeKeys"/>. <see cref="Tools"/> are the tools that read only this area, which
/// the host lists while it's on.
/// </summary>
public sealed record McpArea(
    string Name,
    IReadOnlyList<string> PayloadKeys,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Kinds,
    Type Tools)
{
    /// <summary>
    /// Attribute keys the area owns on an item of any type (code review CR115): while it's off,
    /// <c>get_item</c> leaves them out and an attribute change of one is the area's. A key that
    /// lives only on one of <see cref="Types"/> needn't be listed: the item goes with its type.
    /// </summary>
    public IReadOnlyList<string> AttributeKeys { get; init; } = [];
}

/// <summary>
/// The one table of what each optional area holds (§4.3), which every read that leaves a
/// switched-off area out goes by. Leaving out fails closed: while any area is off, an event that
/// belongs to no area shows only when every key in its payload is one of
/// <see cref="CorePayloadKeys"/>. So a new feature's events stay hidden from agents until its
/// keys are added here, rather than showing in an area Chris switched off.
/// </summary>
public static class McpAreas
{
    public static readonly McpArea Health = new(
        McpDomains.Health,
        [MetricLimits.ReadingKey, LabLimits.VisitKey, "workout_note", .. OuraCollections.All.Select(c => c.PayloadKey)],
        [SubjectTypes.Metric],
        [KernelSources.Oura],
        [],
        typeof(HealthTools));

    public static readonly McpArea Food = new(
        McpDomains.Food, ["meal", "fast", "food_day", "food_photo"], [], [], [], typeof(FoodTools));

    /// <summary>
    /// What an item pays is the rewards area's (REWARD-1). A habit's establish target stays the
    /// core's: it says when a habit is ready for Established, a status of the core's, and the
    /// habits tool's progress toward it reads it.
    /// </summary>
    public static readonly McpArea Rewards = new(
        McpDomains.Rewards, [ExceptionDayService.PayloadKey], [SubjectTypes.Reward], [], [EventKinds.Ledger], typeof(RewardsTools))
    {
        AttributeKeys = [Bounty.AttributeKey],
    };

    public static readonly McpArea Home = new(
        McpDomains.Home, [SpaceLimits.RatingKey, SpaceLimits.PhotoKey], [SubjectTypes.Space], [], [], typeof(HomeTools));

    public static readonly IReadOnlyList<McpArea> All = [Health, Food, Rewards, Home];

    /// <summary>
    /// Every payload key the core's own writes use: status, archive, triage and attribute changes,
    /// timers, habit check-offs, activity text, idea sessions, review reflections, the Primary
    /// focus, an agent's Person and a Person's photo. A capture's payload is empty.
    /// </summary>
    public static readonly IReadOnlyList<string> CorePayloadKeys =
    [
        "subject_id", "status", "archived", "timer", "item_kind", "item_id", "state", "title", "attributes",
        "occurrence", "result", "note", "text", "problem", "ideas", ReviewReflections.PayloadKey, PrimaryFocus.PayloadKey,
        AgentWrites.PersonField, PersonPhotoService.PayloadKey,
    ];
}

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
    /// The highest level any agent writes at (§5.1, decision 7): <see cref="McpWrites.Off"/>, the
    /// default, <see cref="McpWrites.Capture"/> or <see cref="McpWrites.Full"/>. Each client's own
    /// level, in <see cref="WriteClients"/>, is held to it.
    /// </summary>
    public string Writes { get; init; } = McpWrites.Off;

    /// <summary>
    /// The clients that may write when writes are on, each with the level it's named at
    /// (<c>&lt;id&gt;:full</c>; a bare id is <c>capture</c>). A client must also be on LifeOS's list
    /// and act as an AI-agent Person (§5.1); every other client stays read-only.
    /// </summary>
    public IReadOnlyDictionary<string, string> WriteClients { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool WritesOn => Writes != McpWrites.Off;

    /// <summary>Whether writes are on and <paramref name="clientId"/> is named in <c>Mcp:WriteClients</c>.</summary>
    public bool NamesWriter(string clientId) => WriteLevel(clientId) is not null;

    /// <summary>
    /// The level <paramref name="clientId"/> writes at, by the settings alone: the level it's named
    /// at, held to <see cref="Writes"/>; null when writes are off or it isn't named.
    /// </summary>
    public string? WriteLevel(string clientId)
        => WritesOn && WriteClients.TryGetValue(clientId.Trim(), out var named)
            ? McpWrites.Lower(named, Writes)
            : null;

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
            WriteClients = ParseWriteClients(configuration[WriteClientsKey]),
        };
    }

    private static string ParseWrites(string? text)
    {
        var writes = text?.Trim();
        if (string.IsNullOrEmpty(writes))
        {
            return McpWrites.Off;
        }

        return McpWrites.Levels.FirstOrDefault(l => string.Equals(l, writes, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{WritesKey} must be off, capture or full, got '{writes}'.");
    }

    /// <summary>
    /// <c>Mcp:WriteClients</c>: client ids, each bare (<c>capture</c>) or with its level after a
    /// colon. A client named twice, or at a level that isn't one, fails startup.
    /// </summary>
    private static Dictionary<string, string> ParseWriteClients(string? text)
    {
        var clients = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Split(text))
        {
            var colon = entry.LastIndexOf(':');
            var clientId = (colon < 0 ? entry : entry[..colon]).Trim();
            var named = colon < 0 ? McpWrites.Capture : entry[(colon + 1)..].Trim();
            var level = McpWrites.Levels
                .Where(l => l != McpWrites.Off)
                .FirstOrDefault(l => string.Equals(l, named, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"{WriteClientsKey} names {clientId} at '{named}'. A client writes at capture or full: <id>:full, or the id alone for capture.");
            if (clientId.Length == 0 || !clients.TryAdd(clientId, level))
            {
                throw new InvalidOperationException(clientId.Length == 0
                    ? $"{WriteClientsKey} has an entry with no client id: '{entry}'."
                    : $"{WriteClientsKey} names {clientId} twice.");
            }
        }

        return clients;
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

/// <summary>
/// The write levels (agent-access plan §5.1), lowest first: <c>Mcp:Writes</c>'s ceiling, and each
/// client's own level in <c>Mcp:WriteClients</c>. A level includes every tool of the levels below it.
/// </summary>
public static class McpWrites
{
    public const string Off = "off";

    /// <summary>
    /// <c>capture</c>, <c>add_journal_entry</c>, <c>dismiss</c> and <c>link_reflection</c>: they
    /// add, clear a raw capture from the Inbox and link a review to its note, and change nothing
    /// else (§5.2, notes plans N7 and N8).
    /// </summary>
    public const string Capture = "capture";

    /// <summary>The <c>capture</c> tools, and the tools that change items (§5.2, §5.6).</summary>
    public const string Full = "full";

    /// <summary>Every level, lowest first.</summary>
    public static readonly IReadOnlyList<string> Levels = [Off, Capture, Full];

    /// <summary>Whether <paramref name="level"/> includes what <paramref name="needed"/> allows.</summary>
    public static bool Reaches(string level, string needed) => Rank(level) >= Rank(needed);

    /// <summary>The lower of two levels.</summary>
    public static string Lower(string a, string b) => Rank(a) <= Rank(b) ? a : b;

    private static int Rank(string level)
    {
        for (var i = 0; i < Levels.Count; i++)
        {
            if (Levels[i] == level)
            {
                return i;
            }
        }

        throw new ArgumentException($"'{level}' isn't a write level.", nameof(level));
    }
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

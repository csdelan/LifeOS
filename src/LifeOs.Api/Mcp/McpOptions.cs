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

    /// <summary>The optional areas, besides the core that's always on (§4.3).</summary>
    public static readonly IReadOnlyList<string> AllDomains = [McpDomains.Health, McpDomains.Food, McpDomains.Rewards, McpDomains.Home];

    /// <summary>Off by default: <c>/mcp</c> and its metadata answer 404. The kill switch.</summary>
    public bool Enabled { get; init; }

    /// <summary>The optional areas agents can read.</summary>
    public IReadOnlySet<string> Domains { get; init; } = new HashSet<string>(AllDomains, StringComparer.OrdinalIgnoreCase);

    /// <summary>Client ids refused at once, even with a token that hasn't expired.</summary>
    public IReadOnlySet<string> BlockedClients { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public bool Allows(string domain) => Domains.Contains(domain);

    /// <summary>The optional areas switched off, which every tool leaves out.</summary>
    public IReadOnlySet<string> Excluded
        => AllDomains.Where(d => !Allows(d)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether agents may see items of <paramref name="type"/>: a Reward belongs to the rewards
    /// area and a Space to the home area, so each goes when its area is off.
    /// </summary>
    public bool AllowsType(string type)
        => type switch
        {
            Domain.SubjectTypes.Reward => Allows(McpDomains.Rewards),
            Domain.SubjectTypes.Space => Allows(McpDomains.Home),
            _ => true,
        };

    public bool IsBlocked(string clientId) => BlockedClients.Contains(clientId.Trim());

    /// <summary>
    /// Reads the settings. <c>Mcp:Domains</c> unset means every area, <c>none</c> means the core
    /// only; <c>Mcp:Writes</c> must be <c>off</c> until Phase 2 builds writes.
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

        var writes = configuration[WritesKey]?.Trim();
        if (!string.IsNullOrEmpty(writes) && !string.Equals(writes, "off", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{WritesKey} is '{writes}', but agents can't write yet (agent-access plan Phase 2). Leave it off.");
        }

        return new McpOptions
        {
            Enabled = on,
            Domains = ParseDomains(configuration[DomainsKey]),
            BlockedClients = Split(configuration[BlockedClientsKey]).ToHashSet(StringComparer.Ordinal),
        };
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

/// <summary>The optional areas' names, as <c>Mcp:Domains</c> spells them.</summary>
public static class McpDomains
{
    /// <summary>Oura: sleep, readiness, steps, workouts and tags (Chris accepted the risk, §2 decision 3).</summary>
    public const string Health = "health";
    public const string Food = "food";
    public const string Rewards = "rewards";
    public const string Home = "home";
}

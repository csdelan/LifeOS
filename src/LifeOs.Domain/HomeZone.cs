using System.Text.RegularExpressions;

namespace LifeOs.Domain;

/// <summary>
/// The one time zone that decides which local day an earn counts on (xp-domains plan
/// §3.3). Life-domain progression judges whole days, so the server and every device must
/// agree on where a day starts; the viewer's zone, which REWARD-5's History uses, would let
/// two devices disagree. Configured as <c>Rewards:HomeTimeZone</c>, an IANA name.
/// <para>
/// The solution runs with invariant globalization, so .NET can't resolve IANA names on
/// Windows: the date math happens in Postgres (<c>AT TIME ZONE</c>), and only the name's
/// shape is checked here. Postgres refuses a well-formed name it doesn't know on first use.
/// </para>
/// </summary>
public static partial class HomeZone
{
    /// <summary>The configuration key for the zone.</summary>
    public const string ConfigurationKey = "Rewards:HomeTimeZone";

    /// <summary>The zone when none is configured: Pacific time.</summary>
    public const string Default = "America/Los_Angeles";

    /// <summary>
    /// Whether <paramref name="zone"/> has the shape of an IANA zone name ("UTC",
    /// "America/Los_Angeles", "America/Argentina/Buenos_Aires", "Etc/GMT+8"). A Windows
    /// zone id such as "Pacific Standard Time" doesn't.
    /// </summary>
    public static bool IsWellFormed(string? zone)
        => zone is not null && IanaShape().IsMatch(zone);

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+\-]*(/[A-Za-z0-9_+\-]+){0,2}$")]
    private static partial Regex IanaShape();
}

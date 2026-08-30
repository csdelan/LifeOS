using System.Text;

namespace LifeOs.Domain;

/// <summary>
/// Builds subject URNs of the form <c>urn:bsk:&lt;type&gt;:&lt;slug&gt;-&lt;shortid&gt;</c>.
/// The slug keeps the URN human-readable; the short id makes it unique by
/// construction, so distinct subjects never collide even when their titles slug
/// to the same value. Reuse of an existing subject is handled by resolving on
/// type+title, not by reconstructing its URN.
/// </summary>
public static class Urns
{
    public static string Build(string type, string title)
    {
        var slug = Slugify(title);
        var shortId = Guid.NewGuid().ToString("N")[..6];
        var tail = slug.Length == 0 ? shortId : $"{slug}-{shortId}";
        return $"urn:bsk:{type.ToLowerInvariant()}:{tail}";
    }

    public static bool IsUrn(string value) => value.StartsWith("urn:bsk:", StringComparison.Ordinal);

    // Lowercase, alphanumeric runs joined by single dashes, no leading/trailing dash.
    private static string Slugify(string title)
    {
        var builder = new StringBuilder(title.Length);
        var pendingSeparator = false;

        foreach (var ch in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }
                builder.Append(ch);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }
}

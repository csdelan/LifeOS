using System.Text;

namespace LifeOs.Domain;

/// <summary>
/// D19: journal and review text lives in SuperJournal, and <c>bsk</c> keeps a pointer event for
/// every saved version. A pointer is a <c>journal</c> event whose payload holds
/// <c>{"superjournal": {journal_id, entry_id, version, subject_type, subject_key, excerpt}}</c>
/// (plus <c>reconciled: true</c> when the reconcile step wrote it), its <c>source_id</c> is who
/// saved it (<c>api</c>, or <c>mcp:&lt;client_id&gt;</c> for an agent), and its
/// <c>external_id</c> is <see cref="ExternalId"/>. Migration 0049 keeps one pointer per entry
/// version, whichever door writes it.
/// </summary>
public static class JournalPointers
{
    /// <summary>The payload key that holds the pointer, and that marks a <c>journal</c> event as one.</summary>
    public const string PayloadKey = "superjournal";

    /// <summary>
    /// The longest excerpt a pointer keeps: what <c>recent_changes</c> shows of any event's text.
    /// Readers that need more fetch the entry from SuperJournal.
    /// </summary>
    public const int ExcerptLength = 500;

    /// <summary>The pointer's <c>external_id</c>: the SuperJournal entry id and the version, <c>&lt;entry_id&gt;:&lt;version&gt;</c>.</summary>
    public static string ExternalId(string entryId, int version) => $"{entryId}:{version}";

    /// <summary>
    /// A plain-text excerpt of <paramref name="contentText"/>: whitespace runs become one space,
    /// and text longer than <see cref="ExcerptLength"/> is cut at a word where it can be, with
    /// "…" at the end.
    /// </summary>
    public static string Excerpt(string? contentText)
    {
        if (string.IsNullOrWhiteSpace(contentText))
        {
            return "";
        }

        var flat = new StringBuilder(Math.Min(contentText.Length, ExcerptLength + 1));
        var space = false;
        foreach (var c in contentText)
        {
            if (char.IsWhiteSpace(c))
            {
                space = flat.Length > 0;
                continue;
            }

            if (space)
            {
                flat.Append(' ');
                space = false;
            }

            flat.Append(c);
            if (flat.Length > ExcerptLength)
            {
                break;
            }
        }

        if (flat.Length <= ExcerptLength)
        {
            return flat.ToString();
        }

        // Room for the ellipsis; cut at the last space if it doesn't lose much.
        var cut = ExcerptLength - 1;
        var lastSpace = flat.ToString(0, cut + 1).LastIndexOf(' ');
        if (lastSpace >= cut - 40)
        {
            cut = lastSpace;
        }

        // Don't split a surrogate pair.
        if (cut > 0 && char.IsHighSurrogate(flat[cut - 1]))
        {
            cut--;
        }

        return flat.ToString(0, cut).TrimEnd() + "…";
    }
}

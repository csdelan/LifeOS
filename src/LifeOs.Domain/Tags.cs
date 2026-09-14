namespace LifeOs.Domain;

/// <summary>
/// Tag normalization (GEN-1). Tags are a flat, case-insensitive classification
/// space, so they are stored lowercased and trimmed — "Trading" and " trading "
/// are one tag. The store enforces the same shape (a CHECK in migration 0013); this
/// is the code mirror the write path normalizes through before it gets there.
/// </summary>
public static class Tags
{
    /// <summary>The normalized form of a single tag, or <c>null</c> when it is blank.</summary>
    public static string? Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        return tag.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a set of tags: trims/lowercases each, drops blanks, and de-dupes
    /// while preserving first-seen order.
    /// </summary>
    public static IReadOnlyList<string> NormalizeAll(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var tag in tags)
        {
            var normalized = Normalize(tag);
            if (normalized is not null && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }
}

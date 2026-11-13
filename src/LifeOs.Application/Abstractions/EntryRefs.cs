namespace LifeOs.Application.Abstractions;

/// <summary>
/// How an entry that isn't a subject is named (a food entry, a fast, a space rating or photo, a
/// reading, a lab visit): its id, or the start of it, at least <see cref="MinPrefix"/> hex digits
/// (code review CR20). One resolver, so each refuses in the same words.
/// </summary>
public static class EntryRefs
{
    /// <summary>The fewest hex digits that name an entry by the start of its id.</summary>
    public const int MinPrefix = 4;

    /// <summary>
    /// The live entry <paramref name="reference"/> names. Refuses, in words with
    /// <paramref name="noun"/> in them ("photo", "lab visit"), a reference that is neither an id
    /// nor the start of one, an id no live entry has (<paramref name="gone"/> says why it may
    /// not), and a start that no id or more than one id begins with. A start that several ids
    /// share lists up to five of them, said by <paramref name="describe"/> when it's given.
    /// </summary>
    public static async Task<T> ResolveAsync<T>(
        string? reference,
        string noun,
        string nouns,
        Func<Guid, CancellationToken, Task<T?>> byId,
        Func<string, CancellationToken, Task<IReadOnlyList<T>>> byPrefix,
        CancellationToken cancellationToken,
        Func<T, string>? describe = null,
        string gone = "deleted",
        string? tooShort = null)
        where T : class
    {
        var wanted = reference?.Trim() ?? "";
        if (Guid.TryParse(wanted, out var id))
        {
            return await byId(id, cancellationToken)
                   ?? throw new InvalidOperationException($"No {noun} has the id {id} (it may have been {gone}).");
        }

        if (wanted.Length < MinPrefix || !wanted.All(ch => Uri.IsHexDigit(ch) || ch == '-'))
        {
            throw new ArgumentException(
                tooShort ?? $"'{reference}' isn't a {noun}'s id: give at least its first {MinPrefix} hex digits.");
        }

        var matches = await byPrefix(wanted, cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"No {noun}'s id starts with '{wanted}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"'{wanted}' starts {matches.Count} {nouns}' ids: " + (describe is null
                    ? "give more of the id."
                    : string.Join("; ", matches.Take(5).Select(describe)) + ". Give more of the id."))
        };
    }
}

using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Renames a subject — the one write path that changes the <c>title</c> column after
/// creation (BROWSE-2 / the pilot's editable title). The URN is deliberately left
/// untouched: it is an immutable handle whose embedded slug is a birth-time
/// convenience, and edges/tags reference the subject by id, so a rename never breaks
/// a link. Renaming a reuse-by-title subject (e.g. a Problem) onto a title that
/// already exists surfaces <see cref="DuplicateSubjectException"/>.
/// </summary>
public sealed class RenameService(SubjectService subjects, ISubjectRepository repository)
{
    /// <summary>
    /// Renames the subject named by <paramref name="reference"/> (urn, short id, or
    /// title) to <paramref name="newTitle"/>. The new title is normalized the same way
    /// creation normalizes (trimmed, internal whitespace collapsed); when it matches
    /// the current title nothing is written and the result reports <c>Changed = false</c>.
    /// </summary>
    public async Task<RenameResult> RenameAsync(
        string reference, string newTitle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new ArgumentException("A new title is required.", nameof(newTitle));
        }

        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        var cleanTitle = NormalizeTitle(newTitle);

        // A no-op rename writes nothing — so it never trips the reuse-by-title index
        // against the subject's own row, and repeated saves stay cheap.
        if (string.Equals(cleanTitle, subject.Title, StringComparison.Ordinal))
        {
            return new RenameResult(subject, subject.Title, Changed: false);
        }

        var updated = await repository.RenameAsync(subject.Id, cleanTitle, cancellationToken);
        if (!updated)
        {
            // The subject resolved a moment ago; a missing row now is a real fault.
            throw new SubjectNotFoundException(reference);
        }

        return new RenameResult(subject with { Title = cleanTitle }, subject.Title, Changed: true);
    }

    // Trim and collapse internal whitespace runs to a single space (as SubjectService does),
    // so titles that differ only by spacing don't diverge from their created form.
    private static string NormalizeTitle(string title)
        => string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// The outcome of a rename: the subject carrying its (new) title, the title it had
/// before, and whether anything actually changed.
/// </summary>
public sealed record RenameResult(SubjectRef Subject, string PreviousTitle, bool Changed);

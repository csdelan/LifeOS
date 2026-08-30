using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Resolve-or-create for subjects. A caller supplies a type and either an
/// existing subject's URN or a title; an existing subject is reused, otherwise a
/// new one is created with a fresh URN. Shared by <c>bsk ideas</c> (Problem) and
/// the M3 subject commands.
/// </summary>
public sealed class SubjectService(ISubjectRepository subjects)
{
    /// <summary>
    /// Resolves an <em>existing</em> subject named by a URN, a short id, or a fuzzy
    /// title. This is the shared resolver every other M3/M4 command uses to name a
    /// subject; unlike <see cref="ResolveOrCreateAsync"/> it never creates. A URN or
    /// short id must match exactly; a title resolves when it uniquely identifies one
    /// subject (an exact case-insensitive title wins over broader substring matches).
    /// Ambiguity raises <see cref="AmbiguousSubjectException"/> rather than guessing.
    /// </summary>
    public async Task<SubjectRef> ResolveAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A subject reference is required.", nameof(reference));
        }

        reference = reference.Trim();

        if (Urns.IsUrn(reference))
        {
            return await subjects.FindByUrnAsync(reference, cancellationToken)
                ?? throw new SubjectNotFoundException(reference);
        }

        // A short-id-looking token is tried as a short id first; if nothing carries
        // it, fall through to treating it as a title fragment (hex-only words like
        // "cafe" are legitimate titles too).
        if (Urns.IsShortId(reference))
        {
            var byShortId = await subjects.FindByShortIdAsync(reference, cancellationToken);
            if (byShortId.Count == 1)
            {
                return byShortId[0];
            }

            if (byShortId.Count > 1)
            {
                throw new AmbiguousSubjectException(reference, byShortId);
            }
        }

        var fragment = NormalizeTitle(reference);
        var matches = await subjects.FindByTitleContainsAsync(fragment, cancellationToken);
        if (matches.Count == 0)
        {
            throw new SubjectNotFoundException(reference);
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        // Several titles contain the fragment, but if exactly one equals it (case-
        // insensitively) that is the unambiguous intent; otherwise report candidates.
        var exact = matches
            .Where(m => string.Equals(m.Title, fragment, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return exact.Count == 1
            ? exact[0]
            : throw new AmbiguousSubjectException(reference, matches);
    }

    /// <summary>
    /// Creates a new subject of the given type with a freshly minted URN and the
    /// supplied attributes. Always creates — for reuse-by-title types the store's
    /// uniqueness guarantee surfaces a <see cref="DuplicateSubjectException"/>.
    /// </summary>
    public async Task<SubjectRef> CreateAsync(
        string type, string title, string attributesJson = "{}", Guid? originEventId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A title is required.", nameof(title));
        }

        var cleanTitle = NormalizeTitle(title);
        var urn = Urns.Build(type, cleanTitle);
        var id = await subjects.CreateAsync(
            new NewSubject(urn, type, cleanTitle, attributesJson, originEventId), cancellationToken);
        return new SubjectRef(id, urn, type, cleanTitle);
    }

    public async Task<ResolvedSubject> ResolveOrCreateAsync(
        string type, string urnOrTitle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urnOrTitle))
        {
            throw new ArgumentException("A URN or title is required.", nameof(urnOrTitle));
        }

        if (Urns.IsUrn(urnOrTitle))
        {
            var byUrn = await subjects.FindByUrnAsync(urnOrTitle, cancellationToken)
                ?? throw new InvalidOperationException($"No subject found with URN '{urnOrTitle}'.");
            return new ResolvedSubject(byUrn, Created: false);
        }

        // Normalize whitespace so titles that differ only by spacing resolve to the
        // same subject rather than creating a divergent one.
        var title = NormalizeTitle(urnOrTitle);

        var existing = await subjects.FindByTypeAndTitleAsync(type, title, cancellationToken);
        if (existing is not null)
        {
            return new ResolvedSubject(existing, Created: false);
        }

        var urn = Urns.Build(type, title);
        try
        {
            var id = await subjects.CreateAsync(new NewSubject(urn, type, title), cancellationToken);
            return new ResolvedSubject(new SubjectRef(id, urn, type, title), Created: true);
        }
        catch (DuplicateSubjectException)
        {
            // Lost a race to create the same subject: return the canonical one that
            // now exists rather than surfacing a duplicate or an error.
            var raced = await subjects.FindByTypeAndTitleAsync(type, title, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return new ResolvedSubject(raced, Created: false);
        }
    }

    // Trim and collapse internal whitespace runs to a single space.
    private static string NormalizeTitle(string title)
        => string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>A subject that was resolved (reused) or newly created.</summary>
public sealed record ResolvedSubject(SubjectRef Subject, bool Created);

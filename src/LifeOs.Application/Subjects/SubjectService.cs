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

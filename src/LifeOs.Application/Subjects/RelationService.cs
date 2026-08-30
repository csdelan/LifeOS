using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Wires subjects together (<c>bsk link</c>). Both endpoints are named through the
/// shared resolver, the leaf-only rule is enforced before anything is written, and
/// the edge is recorded as a declared relation. Other M3 commands (promote, decide)
/// reuse <see cref="LinkResolvedAsync"/> to record their edges between already-known
/// subjects without re-resolving.
/// </summary>
public sealed class RelationService(SubjectService subjects, IRelationRepository relations)
{
    /// <summary>Resolves both endpoints by reference, then records the edge.</summary>
    public async Task<RelationResult> LinkAsync(
        string fromReference, string relation, string toReference,
        CancellationToken cancellationToken = default)
    {
        var from = await subjects.ResolveAsync(fromReference, cancellationToken);
        var to = await subjects.ResolveAsync(toReference, cancellationToken);

        var id = await LinkResolvedAsync(from, relation, to, cancellationToken);
        return new RelationResult(id, from, relation, to);
    }

    /// <summary>
    /// Records an edge between two already-resolved subjects, enforcing the graph
    /// rules first. Provenance is <c>declared</c> — a person asserting a link.
    /// </summary>
    public async Task<Guid> LinkResolvedAsync(
        SubjectRef from, string relation, SubjectRef to,
        CancellationToken cancellationToken = default)
    {
        // subject → subject only. concerns/evidences/violates are event → subject
        // edges and go through capture / `bsk log`, not through link.
        if (!SubjectRelations.All.Contains(relation))
        {
            throw new ArgumentException(
                $"'{relation}' is not a subject-to-subject relation. Expected one of: " +
                $"{string.Join(", ", SubjectRelations.All)}.",
                nameof(relation));
        }

        var rejection = RelationRules.Rejection(relation, to.Type);
        if (rejection is not null)
        {
            throw new InvalidOperationException(rejection);
        }

        return await relations.CreateAsync(from.Id, relation, to.Id, Provenances.Declared, cancellationToken);
    }
}

/// <summary>The outcome of a link: the edge id and its resolved endpoints.</summary>
public sealed record RelationResult(Guid Id, SubjectRef From, string Relation, SubjectRef To);

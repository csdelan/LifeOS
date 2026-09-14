using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Parent-first creation (GEN-7): create a child subject and relate it to an existing
/// parent in one atomic step. The relation is inferred from the canonical parent →
/// child map (<see cref="ParentChildRelations"/>) when the pair is unambiguous, or
/// named explicitly for the rare ambiguous case. The graph rules (subject → subject
/// only, and the leaf rule) are checked before anything is written, and the create +
/// edge are one transaction so a failed link never leaves an orphan child.
/// </summary>
public sealed class ChildCreationService(SubjectService subjects)
{
    public async Task<ChildCreationResult> CreateChildAsync(
        string childType, string title, string parentReference,
        string? relationOverride = null, string attributesJson = "{}",
        CancellationToken cancellationToken = default)
    {
        var parent = await subjects.ResolveAsync(parentReference, cancellationToken);

        var relation = relationOverride?.Trim() switch
        {
            null or "" => ParentChildRelations.Infer(childType, parent.Type)
                ?? throw new ArgumentException(
                    $"No canonical relation for creating a {childType} under a {parent.Type}. " +
                    "Specify the relation explicitly."),
            var explicitRelation => explicitRelation
        };

        // subject → subject only; concerns/evidences/violates are event → subject.
        if (!SubjectRelations.All.Contains(relation))
        {
            throw new ArgumentException(
                $"'{relation}' is not a subject-to-subject relation. Expected one of: " +
                $"{string.Join(", ", SubjectRelations.All)}.");
        }

        // The leaf rule and any other graph shape rule, before any write.
        var rejection = RelationRules.Rejection(relation, parent.Type);
        if (rejection is not null)
        {
            throw new InvalidOperationException(rejection);
        }

        var (child, edgeId) = await subjects.CreateWithParentEdgeAsync(
            childType, title, attributesJson, relation, parent.Id, cancellationToken);

        return new ChildCreationResult(child, relation, parent, edgeId);
    }
}

/// <summary>The outcome of parent-first creation: the new child, the edge, and its parent.</summary>
public sealed record ChildCreationResult(SubjectRef Child, string Relation, SubjectRef Parent, Guid EdgeId);

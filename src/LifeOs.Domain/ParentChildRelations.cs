namespace LifeOs.Domain;

/// <summary>
/// The canonical parent → child relation map for parent-first creation (GEN-7).
/// When a child is created from an existing parent, the edge is the child
/// <c>serves</c> / <c>results_in</c> the parent (child is the edge's <c>from</c>,
/// parent the <c>to</c>). The dominant Pilot hierarchy is Value → Goal → Project →
/// Task, with Task also allowed directly under a Goal:
/// <list type="bullet">
///   <item>Goal → Value: <c>serves</c></item>
///   <item>Project → Goal: <c>results_in</c></item>
///   <item>Task → Project: <c>serves</c></item>
///   <item>Task → Goal: <c>serves</c></item>
/// </list>
/// These pairs are unambiguous, so the relation is inferred silently. Any other pair
/// returns <c>null</c> — the caller must name the relation explicitly or the pair is
/// not a valid parent-first combination. The leaf rule (nothing <c>serves</c> a Task)
/// falls out for free: Task never appears as a parent here, so a Task's child-type
/// chooser is empty.
/// </summary>
public static class ParentChildRelations
{
    // Keyed by (childType, parentType).
    private static readonly Dictionary<(string Child, string Parent), string> Map = new()
    {
        [(SubjectTypes.Goal, SubjectTypes.Value)] = SubjectRelations.Serves,
        [(SubjectTypes.Project, SubjectTypes.Goal)] = SubjectRelations.ResultsIn,
        [(SubjectTypes.Task, SubjectTypes.Project)] = SubjectRelations.Serves,
        [(SubjectTypes.Task, SubjectTypes.Goal)] = SubjectRelations.Serves,
    };

    /// <summary>
    /// The relation for creating a <paramref name="childType"/> under a
    /// <paramref name="parentType"/>, or <c>null</c> when the pair has no canonical
    /// relation (the caller must specify one, or the combination is invalid).
    /// </summary>
    public static string? Infer(string childType, string parentType)
        => Map.TryGetValue((childType, parentType), out var relation) ? relation : null;

    /// <summary>
    /// The child types that can be created beneath <paramref name="parentType"/> —
    /// what the contextual child-type chooser should offer (empty for a Task).
    /// </summary>
    public static IReadOnlyList<string> ChildTypesFor(string parentType)
        => Map.Keys.Where(k => k.Parent == parentType).Select(k => k.Child).Distinct().ToList();
}

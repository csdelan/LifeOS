namespace LifeOs.Domain;

/// <summary>
/// The subject-graph shape rules that hold regardless of who is writing. Stage 1
/// has one: a Task is a leaf — it may <c>serves</c> something (a Commitment) or
/// stand alone, but nothing may <c>serves</c> a Task. This keeps the alignment
/// graph from growing chains of tasks serving tasks, where "does this work serve
/// anything I care about?" stops being answerable.
/// </summary>
public static class RelationRules
{
    /// <summary>
    /// Returns a human-readable reason the relation is illegal, or <c>null</c> when
    /// it is allowed. <paramref name="toType"/> is the target subject's type.
    /// </summary>
    public static string? Rejection(string relation, string toType)
    {
        if (relation == SubjectRelations.Serves && toType == SubjectTypes.Task)
        {
            return "Nothing may 'serves' a Task: a Task is a leaf. "
                   + "A Task may serve a Commitment or stand alone.";
        }

        return null;
    }

    public static bool IsAllowed(string relation, string toType) => Rejection(relation, toType) is null;
}

namespace LifeOs.Domain;

/// <summary>
/// The subject-graph shape rules that hold regardless of who is writing. A Task is a leaf: it
/// may <c>serves</c> something (a Commitment) or stand alone, but nothing may <c>serves</c> a
/// Task. This keeps the alignment graph from growing chains of tasks serving tasks, where "does
/// this work serve anything I care about?" stops being answerable. And no <see cref="Acyclic"/>
/// link may close a loop (focus plan G2): a Goal under its own sub-goal has no root, so it drops
/// off the Map, and what it serves can't be answered.
/// </summary>
public static class RelationRules
{
    /// <summary>The relations a loop may not close, followed together: the alignment graph's.</summary>
    public static readonly IReadOnlyList<string> Acyclic = [SubjectRelations.Serves, SubjectRelations.ResultsIn];

    /// <summary>
    /// Why <paramref name="fromTitle"/> can't <paramref name="relation"/> <paramref name="toTitle"/>:
    /// <paramref name="path"/> already runs up from the second to the first (their titles, the
    /// second's first), so the link would close a loop. A path of one is the item itself.
    /// </summary>
    public static string LoopRefusal(string fromTitle, string relation, string toTitle, IReadOnlyList<string> path)
    {
        var verb = relation == SubjectRelations.ResultsIn ? "result in" : "serve";
        return path.Count <= 1
            ? $"'{fromTitle}' can't {verb} itself."
            : $"'{fromTitle}' can't {verb} '{toTitle}': '{toTitle}' already sits under '{fromTitle}' "
              + $"({string.Join(" → ", path)}), so the link would close a loop. Unlink one of those first.";
    }

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

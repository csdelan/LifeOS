namespace LifeOs.Domain;

/// <summary>
/// The Primary focus (TODAY-1, D6, focus plan G1): a Goal Chris chooses in a review and keeps
/// until he changes it. D6 has two levels, a weekly focus set in the weekly review and a monthly
/// one set in the monthly review; only the weekly one is built, and the daily review sets it too
/// (REVIEW-1's planning reassesses the focus). Each choice is a <c>note</c> event,
/// <c>{focus: {level, goal_id, review_id}}</c>, and the newest per level wins
/// (<c>bsk.v_focus</c>, migration 0074).
/// </summary>
public static class PrimaryFocus
{
    /// <summary>The payload key a focus event carries.</summary>
    public const string PayloadKey = "focus";

    /// <summary>D6's weekly focus, the one level built so far.</summary>
    public const string Weekly = "weekly";

    /// <summary>What a focus Goal must be: Active, and not archived.</summary>
    public const string ActiveStatus = "Active";

    /// <summary>
    /// Why the subject can't be the focus, or null when it can: it must be a Goal, Active, and not
    /// archived. A New Goal is still being developed (GEN-8); a Completed or Abandoned one is over.
    /// </summary>
    public static string? WhyNot(string title, string type, string? status, bool archived)
    {
        if (!string.Equals(type, SubjectTypes.Goal, StringComparison.Ordinal))
        {
            return $"'{title}' is a {type}. The Primary focus is a Goal.";
        }

        if (archived)
        {
            return $"'{title}' is archived. Restore it first, or choose another Goal.";
        }

        return string.Equals(status?.Trim(), ActiveStatus, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"'{title}' is {StatusOrNew(status)}. The Primary focus is an Active Goal: make it Active first, or choose another.";
    }

    /// <summary>A Goal's status as it reads: New when none was ever set.</summary>
    public static string StatusOrNew(string? status) => status?.Trim() is { Length: > 0 } s ? s : "New";
}

/// <summary>
/// The focus as last chosen for <paramref name="Level"/> (<c>bsk.v_focus</c>). <paramref name="GoalId"/>
/// is null when Chris chose No focus. The Goal's title, status and whether it's archived are as
/// they are now, so a Goal completed since reads as <see cref="Lapsed"/>. <paramref name="ReviewId"/>
/// and <paramref name="ReviewTitle"/> name the review it was chosen in, when it was.
/// </summary>
public sealed record ChosenFocus(
    string Level,
    Guid? GoalId,
    string? GoalUrn,
    string? GoalTitle,
    string? GoalStatus,
    bool GoalArchived,
    Guid? ReviewId,
    string? ReviewTitle,
    DateTimeOffset ChosenAt,
    Guid EventId)
{
    /// <summary>A Goal was chosen, and it's still Active and not archived: it is the focus.</summary>
    public bool Holds => GoalId is not null && GoalTitle is not null
        && PrimaryFocus.WhyNot(GoalTitle, SubjectTypes.Goal, GoalStatus, GoalArchived) is null;

    /// <summary>A Goal was chosen, and it has since been completed, abandoned, archived or moved out of Active.</summary>
    public bool Lapsed => GoalId is not null && !Holds;

    /// <summary>What the chosen Goal reads as now, when it has lapsed: its status, or Archived.</summary>
    public string? LapsedAs => !Lapsed ? null : GoalArchived ? "Archived" : PrimaryFocus.StatusOrNew(GoalStatus);
}

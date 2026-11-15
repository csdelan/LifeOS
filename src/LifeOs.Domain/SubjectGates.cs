namespace LifeOs.Domain;

/// <summary>
/// The two gates a Goal and a Habit keep (GEN-8, GEN-3; code review CR125): a Goal can't be
/// Active without a target date, and a Habit serves at least one Goal or Identity Statement. They
/// were the screens' until Chris chose the kernel for them (fifth review, choice D), so the
/// services every door shares refuse them, and <c>bsk</c> and agents can't skip them.
/// <para>
/// Each gate refuses only the change that would break it. An item already past one (an Active Goal
/// made before the gate, with no target date) is left alone: editing its other fields is fine, and
/// only taking away what it has (the target date it now has, its last parent) is refused.
/// </para>
/// <para>
/// The web's forms say the same before anything is written, so focus can move to the field; the
/// cases in <c>tests/rules/gates.json</c> hold both to these answers.
/// </para>
/// </summary>
public static class SubjectGates
{
    /// <summary>A Goal's target date, the attribute its activation needs.</summary>
    public const string TargetDateKey = "target_date";

    /// <summary>The status a Goal needs its target date for.</summary>
    public const string ActiveStatus = "Active";

    /// <summary>The refusal for an Active Goal without a target date.</summary>
    public const string GoalNeedsTargetDate = "A Goal can't be Active without a target date.";

    /// <summary>The refusal for a Habit that serves no Goal or Identity Statement.</summary>
    public const string HabitNeedsParent = "A Habit needs at least one Goal or Identity Statement it serves.";

    /// <summary>The types a Habit's parent can be: a Goal, or an Identity Statement (a Value).</summary>
    public static readonly IReadOnlyList<string> HabitParentTypes = [SubjectTypes.Goal, SubjectTypes.Value];

    /// <summary>
    /// Why a Goal moving from <paramref name="statusBefore"/> (none for a new Goal) to
    /// <paramref name="statusAfter"/>, with or without a target date before and after, is refused,
    /// or null when it isn't: it's refused when it leaves the Goal Active without a target date and
    /// the Goal wasn't already so.
    /// </summary>
    public static string? GoalRefusal(string? statusBefore, bool targetBefore, string? statusAfter, bool targetAfter)
    {
        var brokenBefore = IsActive(statusBefore) && !targetBefore;
        var brokenAfter = IsActive(statusAfter) && !targetAfter;
        return brokenAfter && !brokenBefore ? GoalNeedsTargetDate : null;
    }

    /// <summary>
    /// Why a Habit going from <paramref name="parentsBefore"/> parents (null for a new Habit) to
    /// <paramref name="parentsAfter"/> is refused, or null when it isn't: a new Habit needs one, and
    /// a Habit that has one keeps one.
    /// </summary>
    public static string? HabitRefusal(int? parentsBefore, int parentsAfter)
        => parentsAfter < 1 && (parentsBefore is null || parentsBefore >= 1) ? HabitNeedsParent : null;

    /// <summary>Whether a Habit's edge of <paramref name="relation"/> to a <paramref name="toType"/> is one of its parents.</summary>
    public static bool IsHabitParent(string relation, string toType)
        => relation == SubjectRelations.Serves && HabitParentTypes.Contains(toType);

    /// <summary>Whether <paramref name="value"/>, a target date's attribute, holds one: anything but blank.</summary>
    public static bool HasTargetDate(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsActive(string? status)
        => string.Equals(status?.Trim(), ActiveStatus, StringComparison.OrdinalIgnoreCase);
}

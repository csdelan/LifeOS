using LifeOs.Domain;

namespace LifeOs.Contracts;

/// <summary>
/// The Primary focus as last chosen (TODAY-1, D6, focus plan G1): what <c>POST /api/focus/primary</c>
/// answers and <c>bsk focus --json</c> prints. <see cref="GoalId"/> is null when Chris chose No focus,
/// or when no focus was ever chosen (<see cref="ChosenAt"/> null too). <see cref="Holds"/> is whether
/// that Goal is still Active and unarchived, so the focus; when it isn't, <see cref="LapsedAs"/>
/// says what it is now (Completed, Abandoned, Archived, ...) and Focus suggests one instead.
/// </summary>
public sealed class FocusChoiceRow
{
    /// <summary>D6's level: <c>weekly</c>.</summary>
    public string Level { get; set; } = PrimaryFocus.Weekly;

    public Guid? GoalId { get; set; }
    public string? GoalUrn { get; set; }
    public string? GoalTitle { get; set; }

    /// <summary>Whether the chosen Goal is still Active and unarchived.</summary>
    public bool Holds { get; set; }

    /// <summary>What the chosen Goal reads as now when it no longer holds: its status, or Archived.</summary>
    public string? LapsedAs { get; set; }

    /// <summary>The review it was chosen in, when it was.</summary>
    public Guid? ReviewId { get; set; }

    public string? ReviewTitle { get; set; }

    /// <summary>When it was chosen; null when no focus ever was.</summary>
    public DateTimeOffset? ChosenAt { get; set; }

    /// <summary>Whether this write wrote anything: choosing the focus already held doesn't.</summary>
    public bool Written { get; set; }

    public static FocusChoiceRow From(ChosenFocus? focus, bool written = false)
        => focus is null
            ? new FocusChoiceRow { Written = written }
            : new FocusChoiceRow
            {
                Level = focus.Level,
                GoalId = focus.GoalId,
                GoalUrn = focus.GoalUrn,
                GoalTitle = focus.GoalTitle,
                Holds = focus.Holds,
                LapsedAs = focus.LapsedAs,
                ReviewId = focus.ReviewId,
                ReviewTitle = focus.ReviewTitle,
                ChosenAt = focus.ChosenAt,
                Written = written,
            };
}

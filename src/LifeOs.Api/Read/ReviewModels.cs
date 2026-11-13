using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// A review as the Reviews page reads it (REVIEW-1, REVIEW-2, reviews plan §3). Days are home-zone
/// <c>yyyy-MM-dd</c>. <see cref="Date"/> is the day a daily review is for, or the Sunday naming a
/// weekly one; <see cref="WeekStart"/> to <see cref="WeekEnd"/> is the Monday-to-Sunday week it
/// belongs to (a weekly review's is the week it looks back on), and <see cref="PlanStart"/> to
/// <see cref="PlanEnd"/> the days it plans. <see cref="Status"/> is what it reads as today: Open,
/// Completed or Missed. <see cref="Missed"/> stays true after a late completion
/// (<see cref="CompletedLate"/>).
/// </summary>
public sealed class ReviewRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary><c>daily</c> or <c>weekly</c>.</summary>
    public string Kind { get; set; } = "";

    public DateOnly Date { get; set; }
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
    public DateOnly PlanStart { get; set; }
    public DateOnly PlanEnd { get; set; }

    /// <summary>The day it's due, at the end of which an Open review is missed.</summary>
    public DateOnly DueOn { get; set; }

    /// <summary>Open, Completed or Missed, as of today.</summary>
    public string Status { get; set; } = ReviewStatuses.Open;

    public bool Missed { get; set; }
    public bool CompletedLate { get; set; }

    /// <summary>When the current Completed was set; null unless it's Completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Whether this review keeps the Primary focus (on unless turned off).</summary>
    public bool KeepFocus { get; set; }

    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The vault note that answers it (reviews plan §4.1); null until an agent links one.</summary>
    public ReviewReflectionRow? Reflection { get; set; }

    public static ReviewRow From(Review review, DateOnly today)
    {
        var weekEnd = ReviewPeriods.WeekSunday(review.Period);
        var (planStart, planEnd) = review.Plans;
        return new ReviewRow
        {
            Id = review.Id,
            Urn = review.Urn,
            Title = review.Title,
            Kind = review.Kind,
            Date = review.Period,
            WeekStart = weekEnd.AddDays(-6),
            WeekEnd = weekEnd,
            PlanStart = planStart,
            PlanEnd = planEnd,
            DueOn = review.DueOn,
            Status = review.StatusAsOf(today),
            Missed = review.MissedAsOf(today),
            CompletedLate = review.CompletedLate,
            CompletedAt = review.CompletedAt,
            KeepFocus = review.KeepFocus,
            Archived = review.Archived,
            CreatedAt = review.CreatedAt,
            Reflection = review.Reflection is { } r
                ? new ReviewReflectionRow { Path = r.Path, Vault = r.Vault, Summary = r.Summary, LinkedAt = r.LinkedAt }
                : null,
        };
    }
}

/// <summary>
/// A review's reflection: the note's <see cref="Path"/> in the <see cref="Vault"/> (opened with an
/// <c>obsidian://</c> link), the summary an agent wrote, and when this link was recorded.
/// </summary>
public sealed class ReviewReflectionRow
{
    public string Path { get; set; } = "";
    public string Vault { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset LinkedAt { get; set; }
}

/// <summary>
/// One section of a review's recap (REVIEW-1, REVIEW-2): what was recorded over the days it looks
/// back on. <see cref="Id"/> is <c>status</c>, <c>completed</c>, <c>overdue</c>, <c>habits</c>,
/// <c>appointments</c>, <c>journal</c> or <c>inbox</c>.
/// </summary>
public sealed class RecapSectionRow
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public IReadOnlyList<RecapItemRow> Items { get; set; } = [];
}

/// <summary>
/// One thing in a recap section. <see cref="At"/> is an ISO instant, or a <c>yyyy-MM-dd</c> day for
/// things that happen on a day (a habit day, an appointment, a due date).
/// </summary>
public sealed class RecapItemRow
{
    public string Kind { get; set; } = "";
    public string At { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public Guid? SubjectId { get; set; }
}

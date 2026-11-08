using System.Globalization;

namespace LifeOs.Domain;

/// <summary>
/// The kinds of Review (REVIEW-0, REVIEW-1, REVIEW-2, D3): a daily review for each day and a
/// weekly review for each Monday-to-Sunday week, named by its Sunday. Monthly (REVIEW-3) comes
/// later.
/// </summary>
public static class ReviewKinds
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";

    public static readonly IReadOnlyList<string> All = [Daily, Weekly];

    /// <summary>The kind's own spelling of <paramref name="kind"/>, or null when it isn't one.</summary>
    public static string? Canonical(string? kind)
        => All.FirstOrDefault(k => string.Equals(k, kind?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A Review subject's statuses (D3, D8, reviews plan §2). A review is Open until Chris completes
/// it. Missed is what an Open review reads as once its day is over, and it can also be set by
/// hand ("I'm not doing this one"). Nothing moves a review's status on its own: missed is worked
/// out on every read from the day and when it was first completed.
/// </summary>
public static class ReviewStatuses
{
    public const string Open = "Open";
    public const string Completed = "Completed";
    public const string Missed = "Missed";
}

/// <summary>
/// A Review as <c>bsk.v_review</c> holds it (migration 0052). <paramref name="Period"/> is the day
/// a daily review is for, or the Sunday that names a weekly one. <paramref name="Status"/> is the
/// stored status (null when none was ever set, which reads as Open). <paramref name="CompletedAt"/>
/// is when the current Completed was set (null unless Completed now);
/// <paramref name="FirstCompletedAt"/> and <paramref name="FirstCompletedOn"/> are the first time
/// it was ever completed and that instant's home-zone day, which decide whether it was missed.
/// </summary>
public sealed record Review(
    Guid Id,
    string Urn,
    string Title,
    string Kind,
    DateOnly Period,
    string? Status,
    bool KeepFocus,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FirstCompletedAt,
    DateOnly? FirstCompletedOn,
    bool Archived,
    DateTimeOffset CreatedAt)
{
    /// <summary>The day it's due: the end of its own day (D8). A weekly review is due on its Sunday.</summary>
    public DateOnly DueOn => Period;

    /// <summary>The days it looks back on: its day, or the Monday-to-Sunday week ending on its Sunday.</summary>
    public (DateOnly From, DateOnly To) LooksBack => ReviewPeriods.LooksBack(Kind, Period);

    /// <summary>The days it plans: the next day, or the Monday-to-Sunday week after its Sunday.</summary>
    public (DateOnly From, DateOnly To) Plans => ReviewPeriods.Plans(Kind, Period);

    /// <summary>Whether it's Completed now.</summary>
    public bool IsCompleted => string.Equals(Status, ReviewStatuses.Completed, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether it was missed as of <paramref name="today"/> (home-zone day): first completed after
    /// its day, or never completed and either set Missed by hand or past its day. Completing it
    /// late doesn't undo this (REVIEW-2).
    /// </summary>
    public bool MissedAsOf(DateOnly today)
        => FirstCompletedOn is { } first
            ? first > DueOn
            : string.Equals(Status, ReviewStatuses.Missed, StringComparison.OrdinalIgnoreCase) || today > DueOn;

    /// <summary>What it reads as on <paramref name="today"/>: Completed, Missed or Open.</summary>
    public string StatusAsOf(DateOnly today)
        => IsCompleted ? ReviewStatuses.Completed
            : string.Equals(Status, ReviewStatuses.Missed, StringComparison.OrdinalIgnoreCase) ? ReviewStatuses.Missed
            : FirstCompletedOn is null && today > DueOn ? ReviewStatuses.Missed
            : ReviewStatuses.Open;

    /// <summary>Completed, but first completed after its day: backfilled (REVIEW-2).</summary>
    public bool CompletedLate => IsCompleted && FirstCompletedOn is { } first && first > DueOn;
}

/// <summary>
/// The period rules for reviews (REVIEW-1, REVIEW-2, D8). Weeks run Monday to Sunday; a weekly
/// review is named by its Sunday, looks back on the week ending that day and plans the week after
/// (Chris, 2026-10-23: the review on Sun Oct 25 looks back on Mon Oct 19 – Sun Oct 25 and plans
/// Mon Oct 26 – Sun Nov 1).
/// </summary>
public static class ReviewPeriods
{
    /// <summary>The attribute holding a review's kind (daily or weekly).</summary>
    public const string KindKey = "kind";

    /// <summary>The attribute holding a review's period: its day, or its Sunday (yyyy-MM-dd).</summary>
    public const string PeriodKey = "period";

    /// <summary>The attribute holding whether the review keeps the Primary focus (true unless set false).</summary>
    public const string KeepFocusKey = "keep_focus";

    /// <summary>The attributes only the review service writes.</summary>
    public static readonly IReadOnlyList<string> ReservedKeys = [KindKey, PeriodKey];

    /// <summary>The Sunday that ends the Monday-to-Sunday week holding <paramref name="day"/>.</summary>
    public static DateOnly WeekSunday(DateOnly day)
        => day.AddDays(((int)DayOfWeek.Sunday - (int)day.DayOfWeek + 7) % 7);

    /// <summary>The period a <paramref name="kind"/> review for <paramref name="day"/> belongs to.</summary>
    public static DateOnly PeriodFor(string kind, DateOnly day)
        => kind == ReviewKinds.Weekly ? WeekSunday(day) : day;

    /// <summary>The days a review looks back on.</summary>
    public static (DateOnly From, DateOnly To) LooksBack(string kind, DateOnly period)
        => kind == ReviewKinds.Weekly ? (period.AddDays(-6), period) : (period, period);

    /// <summary>The days a review plans.</summary>
    public static (DateOnly From, DateOnly To) Plans(string kind, DateOnly period)
        => kind == ReviewKinds.Weekly ? (period.AddDays(1), period.AddDays(7)) : (period.AddDays(1), period.AddDays(1));

    /// <summary>A review's title: "Daily review, Sun Nov 8" or "Weekly review, Sun Nov 8".</summary>
    public static string Title(string kind, DateOnly period)
        => (kind == ReviewKinds.Weekly ? "Weekly review, " : "Daily review, ")
            + Day(period);

    /// <summary>
    /// Why a <paramref name="kind"/> review for <paramref name="period"/> can't be opened on
    /// <paramref name="today"/>, or null when it can. Neither kind opens ahead of its day, so a
    /// week has only daily reviews until its Sunday (REVIEW-2). A past one opens late, to
    /// backfill it.
    /// </summary>
    public static string? WhyNotOpen(string kind, DateOnly period, DateOnly today)
        => period <= today ? null
            : kind == ReviewKinds.Weekly
                ? $"The weekly review for {Day(period)} opens that Sunday. Until then, write in the daily reviews."
                : $"The daily review for {Day(period)} opens that day.";

    /// <summary>A day as reviews name it: "Sun Nov 8".</summary>
    public static string Day(DateOnly day) => day.ToString("ddd MMM d", CultureInfo.InvariantCulture);
}

import { addDays, endOfWeekSunday, formatTimestamp, localDateFromTimestamp } from "@/lib/dates";
import { habitStage } from "@/lib/establish";
import type {
  HabitOccurrenceRow,
  HabitRow,
  ReviewDoc,
  ReviewKind,
  ReviewReflection,
  ReviewStatus,
} from "@/lib/production-ui-types";

/**
 * The rules of daily and weekly reviews on the web (REVIEW-0, REVIEW-1, REVIEW-2, REVIEW-5,
 * reviews plan §1), the twin of `ReviewPeriods` and `Review` in LifeOs.Domain. Days are
 * `yyyy-MM-dd`. Weeks run Monday to Sunday, and a weekly review is named by its Sunday.
 */

export interface ReviewPeriod {
  kind: ReviewKind;
  /** What names the review: the day, or the Sunday ending its week. */
  date: string;
  /** The days it looks back on. */
  start: string;
  end: string;
  /** The days it plans. */
  planStart: string;
  planEnd: string;
}

/** The review of a kind holding a day: a daily review is the day's, a weekly one its week's. */
export function reviewPeriod(kind: ReviewKind, day: string): ReviewPeriod {
  if (kind === "daily") {
    const next = addDays(day, 1);
    return { kind, date: day, start: day, end: day, planStart: next, planEnd: next };
  }
  const sunday = endOfWeekSunday(day);
  return {
    kind,
    date: sunday,
    start: addDays(sunday, -6),
    end: sunday,
    planStart: addDays(sunday, 1),
    planEnd: addDays(sunday, 7),
  };
}

/** The day naming the review before (-1) or after (1): the day, or the Sunday a week away. */
export function stepReview(period: ReviewPeriod, by: 1 | -1): string {
  return addDays(period.date, period.kind === "daily" ? by : 7 * by);
}

/** "Sun, Nov 8": how a review names its day. */
export function reviewDay(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
  });
}

/** "Mon, Nov 2 – Sun, Nov 8", or the one day. */
export function reviewSpan(start: string, end: string): string {
  return start === end ? reviewDay(start) : `${reviewDay(start)} – ${reviewDay(end)}`;
}

/**
 * Why the review can't be opened yet, in words, or null once its day has come. Neither kind
 * opens ahead of its day, so a week has only daily reviews until its Sunday (REVIEW-2).
 */
export function whyNotOpen(period: ReviewPeriod, today: string): string | null {
  if (period.date <= today) return null;
  return period.kind === "weekly"
    ? `The weekly review for ${reviewDay(period.date)} opens that Sunday. Until then, write in the daily reviews.`
    : `The daily review for ${reviewDay(period.date)} opens that day.`;
}

/**
 * What a review reads as on `today`, from its due day and the day it was first completed (the
 * twin of `Review.StatusAsOf`): Completed; Missed once its day is over and it was never
 * completed; otherwise Open. A late completion stays missed.
 */
export function reviewStateAsOf(
  review: { dueOn: string; completedAt?: string | null; firstCompletedOn?: string | null },
  today: string,
): { status: ReviewStatus; missed: boolean; completedLate: boolean } {
  const completed = !!review.completedAt;
  const first = review.firstCompletedOn ?? null;
  const missed = first !== null ? first > review.dueOn : today > review.dueOn;
  const status: ReviewStatus = completed
    ? "Completed"
    : first === null && today > review.dueOn
      ? "Missed"
      : "Open";
  return { status, missed, completedLate: completed && first !== null && first > review.dueOn };
}

/** The line under a review's title saying where it stands. */
export function reviewStanding(doc: ReviewDoc, today: string): string {
  if (doc.status === "Completed") {
    const when = doc.completedAt ? formatTimestamp(doc.completedAt) : null;
    return doc.completedLate
      ? `Completed late${when ? `, on ${when}` : ""}. It still counts as missed. Still editable.`
      : `Completed${when ? ` ${when}` : ""}. Still editable.`;
  }
  if (doc.status === "Missed") {
    return `Missed: its day ended on ${reviewDay(doc.dueOn)}. You can still write it and complete it late.`;
  }
  if (doc.dueOn < today) return "Open again. It was completed on time, so it isn't missed.";
  return doc.dueOn === today
    ? "Due by the end of today. Complete it when it feels done."
    : `Due by the end of ${reviewDay(doc.dueOn)}.`;
}

/** A day a review looks back on, with a row for each habit it can check off. */
export interface ReviewHabitDay {
  day: string;
  rows: HabitOccurrenceRow[];
}

/**
 * The habit days a review can check off (REVIEW-5): each day it looks back on up to today, with
 * a row for every habit tracked that day, recorded or not. A habit counts while Building or
 * Established, from its start date (or the day it was made) to its end date. A day already
 * recorded always shows. An unrecorded day stays unrecorded, never a failure.
 */
export function reviewHabitDays(
  habits: HabitRow[],
  occurrences: HabitOccurrenceRow[],
  start: string,
  end: string,
  today: string,
): ReviewHabitDay[] {
  const last = end < today ? end : today;
  const days: ReviewHabitDay[] = [];
  for (let day = start; day <= last; day = addDays(day, 1)) {
    const recorded = occurrences.filter((o) => o.occurrenceDate === day);
    const rows: HabitOccurrenceRow[] = habits
      .filter((h) => tracksOn(h, day))
      .map(
        (h) =>
          recorded.find((o) => o.habitId === h.id) ?? {
            habitId: h.id,
            habitUrn: h.urn,
            habitName: h.name,
            occurrenceDate: day,
            state: "unrecorded" as const,
            allowsPartial: h.allowsPartial,
          },
      );
    for (const o of recorded) {
      if (!rows.some((r) => r.habitId === o.habitId)) rows.push(o);
    }
    rows.sort((a, b) => a.habitName.localeCompare(b.habitName));
    days.push({ day, rows });
  }
  return days;
}

function tracksOn(habit: HabitRow, day: string): boolean {
  if (habit.archived) return false;
  const stage = habitStage(habit);
  if (stage !== "building" && stage !== "established") return false;
  const from = habit.startDate || localDateFromTimestamp(habit.createdAt);
  if (from > day) return false;
  return !habit.endDate || habit.endDate >= day;
}

/**
 * The `obsidian://` link that opens a reflection in Obsidian on a machine that has its vault
 * (Notes Spec §4: from LifeOS, the note's path opened this way).
 */
export function obsidianUrl(reflection: Pick<ReviewReflection, "path" | "vault">): string {
  return `obsidian://open?vault=${encodeURIComponent(reflection.vault)}&file=${encodeURIComponent(reflection.path)}`;
}

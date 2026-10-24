import { addDays, startOfWeekMonday } from "@/lib/dates";
import { addMonthsClamped, isOccurrenceDate, parseRecurrence, recurrenceLabel } from "@/lib/recurrence";
import { normalizeAmounts } from "@/lib/rewards";
import { defaultStatus } from "@/lib/production-ui-types";
import type {
  RecurrenceSpec,
  SubjectListItem,
  TaskRepeat,
  TaskSeriesCounts,
  TaskSeriesHistory,
  TaskSeriesRow,
} from "@/lib/production-ui-types";

/**
 * Repeating Tasks (TASKS-3, D16), the web's copy of the kernel's `TaskSeries`: how a pattern
 * dates its repetitions, and the plain readings the pages show.
 */

/** How a repetition is dated (`RepeatKind`): on calendar dates, once each week, or after the last one closed. */
export type RepeatKind = "calendar" | "each-week" | "after-completion";

/** The kind a Task's pattern is; null for none, or a cue (which has no dates). */
export function repeatKind(spec: RecurrenceSpec | null | undefined): RepeatKind | null {
  if (!spec || spec.freq === "trigger") return null;
  if (spec.freq === "each") return "each-week";
  if (spec.freq === "interval" && spec.from === "completion") return "after-completion";
  return "calendar";
}

/** Why a Task can't repeat on `spec` (`TaskSeries.CheckTaskPattern`), or null when it can. */
export function taskPatternProblem(spec: RecurrenceSpec | null | undefined): string | null {
  return spec && repeatKind(spec) === null
    ? "A repeating Task needs dates: on days of the week or month, every N, once each week, or after the last completion. A cue is for Habits."
    : null;
}

/** The Monday of the week holding `iso` (weeks run Monday to Sunday). */
export function weekOf(iso: string): string {
  return startOfWeekMonday(iso);
}

/**
 * A repetition's dates for the date it stands for: once each week is scheduled on the Monday
 * and due the Sunday; every other kind is due on the date itself.
 */
export function datesFor(kind: RepeatKind, occurrence: string): { due: string; scheduled: string | null } {
  return kind === "each-week" ? { due: addDays(occurrence, 6), scheduled: occurrence } : { due: occurrence, scheduled: null };
}

/** When an after-completion repetition is due: `closedOn` plus the every-N, a month clamping to its last day. */
export function afterCompletion(spec: RecurrenceSpec, closedOn: string): string {
  const every = Math.max(1, spec.every ?? 1);
  if (spec.unit === "weeks") return addDays(closedOn, 7 * every);
  if (spec.unit === "months") return addMonthsClamped(closedOn, every);
  return addDays(closedOn, every);
}

/** The first date on or after `from` that a calendar pattern lands on, counted from `start`; null when none is near. */
export function nextOccurrence(spec: RecurrenceSpec, start: string, from: string): string | null {
  let day = from < start ? start : from;
  // Six years covers a monthly day 31 and an every-N-months repeat many times over.
  for (let guard = 0; guard < 2200; guard += 1) {
    if (isOccurrenceDate(spec, day, start)) return day;
    day = addDays(day, 1);
  }
  return null;
}

/** A stored pattern's plain reading ("Every Wednesday"), or null when there's none to read. */
export function repeatLabel(raw?: string | null): string | null {
  const spec = parseRecurrence(raw);
  return spec ? recurrenceLabel(spec) : null;
}

/**
 * How a repeating Task has gone, as its repetitions closed: "3 of 4 done" counts the done
 * against the done and missed (a cancelled one didn't apply). "None closed yet" before any.
 */
export function seriesTally(counts: Pick<TaskSeriesCounts, "completedCount" | "missedCount">): string {
  const done = counts.completedCount;
  const owed = done + counts.missedCount;
  return owed === 0 ? "None closed yet" : `${done} of ${owed} done`;
}

/** One repetition of a repeating Task, as the lists carry it (not the series itself). */
export function isRepetition(item: Pick<SubjectListItem, "isSeries" | "seriesId">): boolean {
  return !!item.seriesId && !item.isSeries;
}

/**
 * The subjects the Map draws (TASKS-3): a repeating Task is one node, the series, which holds
 * its pattern, price and links; its repetitions, one a week for a weekly chore, are left out.
 * Give it a list read with `includeSeries`.
 */
export function mapSubjects(subjects: SubjectListItem[]): SubjectListItem[] {
  return subjects.filter((s) => !isRepetition(s));
}

/**
 * What a subject's status pill says. A repeating Task's own status is only Not started or
 * Cancelled (D16), so the series reads "Repeating", or "Stopped" once Cancelled or archived.
 */
export function shownStatus(item: SubjectListItem): string | null {
  if (item.isSeries) return item.status === "Cancelled" || item.archived ? "Stopped" : "Repeating";
  return item.status || defaultStatus(item.type) || null;
}

/** How one repeating Task went over a review's days. */
export interface SeriesPeriod {
  seriesId: string;
  title: string;
  /** The latest due date among its repetitions in the period. */
  lastDue: string;
  completedCount: number;
  missedCount: number;
  /** Open, and due before the review's as-of day. */
  lateCount: number;
  /** Open, and not due yet. */
  aheadCount: number;
  /** Cancelled: skipped on purpose, so not counted against it. */
  skippedCount: number;
}

/**
 * Each repeating Task's repetitions due from `start` to `end`, counted for a review ("Take out
 * trash: 3 of 4 done"). A repetition belongs to the period its due date falls in, so a late
 * one done the next week still counts for its own week, and a hidden one is left out. Only
 * the repeating Tasks with a repetition due in the period are listed, by title.
 */
export function seriesInPeriod(
  histories: TaskSeriesHistory[],
  start: string,
  end: string,
  asOf: string,
): SeriesPeriod[] {
  const out: SeriesPeriod[] = [];
  for (const { series, repetitions } of histories) {
    const due = repetitions.filter((r) => !r.archived && !!r.due && r.due >= start && r.due <= end);
    if (due.length === 0) continue;
    const row: SeriesPeriod = {
      seriesId: series.id,
      title: series.title,
      lastDue: due.reduce((latest, r) => (r.due! > latest ? r.due! : latest), ""),
      completedCount: 0,
      missedCount: 0,
      lateCount: 0,
      aheadCount: 0,
      skippedCount: 0,
    };
    for (const r of due) {
      const status = (r.status ?? "").toLowerCase();
      if (status === "completed") row.completedCount += 1;
      else if (status === "missed") row.missedCount += 1;
      else if (status === "cancelled") row.skippedCount += 1;
      else if (r.due! < asOf) row.lateCount += 1;
      else row.aheadCount += 1;
    }
    out.push(row);
  }
  return out.sort((a, b) => a.title.localeCompare(b.title));
}

/** A period's reading: "3 of 4 done · 1 missed", then what's late, still to do or skipped. */
export function periodReading(row: SeriesPeriod): string {
  const parts = [seriesTally(row)];
  if (row.missedCount) parts.push(`${row.missedCount} missed`);
  if (row.lateCount) parts.push(`${row.lateCount} late`);
  if (row.aheadCount) parts.push(`${row.aheadCount} still to do`);
  if (row.skippedCount) parts.push(`${row.skippedCount} skipped`);
  return parts.join(" · ");
}

/** The API types int64 as `number | string`; the counts are numbers here. */
function counted<T extends TaskSeriesCounts>(raw: T): T {
  return {
    ...raw,
    openCount: Number(raw.openCount) || 0,
    completedCount: Number(raw.completedCount) || 0,
    missedCount: Number(raw.missedCount) || 0,
    cancelledCount: Number(raw.cancelledCount) || 0,
  };
}

/** A detail's `repeating`, its counts as numbers. */
export function normalizeRepeat(raw: TaskRepeat | null | undefined): TaskRepeat | null {
  return raw ? counted(raw) : null;
}

/** A series row, its counts and price as numbers. */
export function normalizeSeriesRow(raw: TaskSeriesRow): TaskSeriesRow {
  return { ...counted(raw), bounty: raw.bounty ? normalizeAmounts(raw.bounty) : null };
}

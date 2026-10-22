/**
 * A day's activity XP as the Activity page shows it (ACTIVITY-5, activity plan §3.8, A9): each
 * part — steps, exercise, sleep — either paid (read from the ledger, multiplier included) or
 * projected from the day so far by `lib/activity-score.ts`, and labelled as projected, never as
 * earned. A projection is base Health XP: the multiplier is applied when the award is written.
 *
 * Steps and exercise pay once Oura's day is over, at 4 a.m. the next morning; sleep pays once
 * the day has a score. Both are paid at the next sync after that. Missing data pays nothing,
 * and days before activity XP started never pay.
 */
import {
  ACTIVITY_RULES,
  activityPays,
  EXERCISE_MAX,
  exerciseSeconds,
  exerciseXp,
  SLEEP_MAX,
  sleepXp,
  STEPS_MAX,
  stepsXp,
  type ExerciseSpan,
} from "@/lib/activity-score";
import { formatDayFull } from "@/lib/dates";
import type { ActivityAwardPaid, ActivityDayRow } from "@/lib/production-ui-types";

/** Oura's day ends at 4 a.m. the next morning. */
export const DAY_START_HOUR = 4;

export type XpPart = "steps" | "exercise" | "sleep";

/** The parts, each with the most base Health XP it pays a day. */
export const XP_PARTS: readonly { part: XpPart; label: string; max: number }[] = [
  { part: "steps", label: "Steps", max: STEPS_MAX },
  { part: "exercise", label: "Exercise", max: EXERCISE_MAX },
  { part: "sleep", label: "Sleep", max: SLEEP_MAX },
];

/** One part of a day's activity XP. */
export interface PartXp {
  part: XpPart;
  label: string;
  /** `paid`: on the ledger. `projected`: on track for, not paid yet. `none`: nothing to pay. */
  state: "paid" | "projected" | "none";
  /** Paid: what it paid, after the multiplier. Projected: the base it's on track for. None: 0. */
  xp: number;
  /** The base before the multiplier (the same as `xp` unless a multiplier applied). */
  base: number;
  multiplier: number | null;
  /** In words, for a part not paid: when it pays, or why it doesn't. */
  why: string | null;
}

export interface DayXp {
  day: string;
  /** False before activity XP started: nothing on the day ever pays. */
  pays: boolean;
  parts: PartXp[];
  /** Health XP paid, after the multiplier. */
  paid: number;
  /** Base Health XP projected for the parts not paid yet. */
  projected: number;
  total: number;
}

/** "2026-10-22": the local date an instant falls on. */
function localDay(at: Date): string {
  return `${at.getFullYear()}-${String(at.getMonth() + 1).padStart(2, "0")}-${String(at.getDate()).padStart(2, "0")}`;
}

/** The Oura day it is at `now`: the local date 4 hours earlier. */
export function ouraDayAt(now: Date): string {
  return localDay(new Date(now.getTime() - DAY_START_HOUR * 3_600_000));
}

/** Whether Oura's `day` is over at `now`, so its steps and exercise can pay. */
export function ouraDayOver(day: string, now: Date): boolean {
  return day < ouraDayAt(now);
}

function paidPart(part: XpPart, label: string, paid: ActivityAwardPaid): PartXp {
  return { part, label, state: "paid", xp: paid.paid, base: paid.base, multiplier: paid.multiplier, why: null };
}

function projectedPart(part: XpPart, label: string, base: number, why: string): PartXp {
  return { part, label, state: "projected", xp: base, base, multiplier: null, why };
}

function nonePart(part: XpPart, label: string, why: string): PartXp {
  return { part, label, state: "none", xp: 0, base: 0, multiplier: null, why };
}

/**
 * A day's activity XP: each part paid, projected or nothing, with the totals. `workouts` are
 * the day's (others are ignored); `now` decides whether Oura's day is over.
 */
export function dayXp(
  row: Pick<ActivityDayRow, "day" | "steps" | "sleepScore" | "paid">,
  workouts: readonly (ExerciseSpan & { day: string })[],
  now: Date = new Date(),
): DayXp {
  const { day } = row;
  const pays = activityPays(day);
  const label = (part: XpPart) => XP_PARTS.find((p) => p.part === part)!.label;
  if (!pays) {
    const why = `Activity XP starts on ${formatDayFull(ACTIVITY_RULES.xpFrom)}`;
    return { day, pays, parts: XP_PARTS.map((p) => nonePart(p.part, p.label, why)), paid: 0, projected: 0, total: 0 };
  }

  const over = ouraDayOver(day, now);
  const waits = over ? "Pays at the next sync" : "Pays when the day ends, at 4 a.m.";

  const steps = row.paid.steps
    ? paidPart("steps", label("steps"), row.paid.steps)
    : row.steps == null
      ? nonePart("steps", label("steps"), "No steps from Oura")
      : stepsXp(row.steps) > 0
        ? projectedPart("steps", label("steps"), stepsXp(row.steps), waits)
        : nonePart("steps", label("steps"), "No steps yet");

  const seconds = exerciseSeconds(workouts.filter((w) => w.day === day));
  const firstTier = ACTIVITY_RULES.exerciseTiers[0].fromMinutes;
  const exercise = row.paid.exercise
    ? paidPart("exercise", label("exercise"), row.paid.exercise)
    : seconds === 0
      ? nonePart("exercise", label("exercise"), "No exercise recorded")
      : exerciseXp(seconds) > 0
        ? projectedPart("exercise", label("exercise"), exerciseXp(seconds), waits)
        : nonePart("exercise", label("exercise"), `Pays from ${firstTier} minutes`);

  const floor = ACTIVITY_RULES.sleepCurve[0].at;
  const sleep = row.paid.sleep
    ? paidPart("sleep", label("sleep"), row.paid.sleep)
    : row.sleepScore == null
      ? nonePart("sleep", label("sleep"), "No sleep score")
      : sleepXp(row.sleepScore) > 0
        ? projectedPart("sleep", label("sleep"), sleepXp(row.sleepScore), "Pays at the next sync")
        : nonePart("sleep", label("sleep"), `Pays above a sleep score of ${floor}`);

  const parts = [steps, exercise, sleep];
  const paid = parts.filter((p) => p.state === "paid").reduce((sum, p) => sum + p.xp, 0);
  const projected = parts.filter((p) => p.state === "projected").reduce((sum, p) => sum + p.xp, 0);
  return { day, pays, parts, paid, projected, total: paid + projected };
}

/** "×1.3": a paid part's multiplier. */
function times(multiplier: number): string {
  return `×${multiplier.toFixed(1)}`;
}

/** A part's number with its standing: "20", "26 (20 ×1.3)", "15 projected", "—". */
export function partXpText(p: PartXp): string {
  if (p.state === "paid") return p.multiplier ? `${p.xp} (${p.base} ${times(p.multiplier)})` : String(p.xp);
  if (p.state === "projected") return `${p.xp} projected`;
  return "—";
}

/** A part in words for a screen reader or a readout: "Steps: 20 Health XP earned", "Sleep: 15 projected, pays at the next sync". */
export function partXpWords(p: PartXp): string {
  if (p.state === "paid") {
    return `${p.label}: ${p.xp} Health XP earned${p.multiplier ? ` (${p.base} base ${times(p.multiplier)})` : ""}`;
  }
  if (p.state === "projected") return `${p.label}: ${p.xp} Health XP projected, ${p.why!.toLowerCase()}`;
  return `${p.label}: nothing to pay (${p.why!.toLowerCase()})`;
}

/** The day's total in words: "40 Health XP: 15 earned, 25 projected". */
export function dayXpWords(xp: DayXp): string {
  if (!xp.pays) return xp.parts[0].why ?? "";
  if (xp.total === 0) return "No Health XP yet";
  if (xp.projected === 0) return `${xp.paid} Health XP earned`;
  if (xp.paid === 0) return `${xp.projected} Health XP projected`;
  return `${xp.total} Health XP: ${xp.paid} earned, ${xp.projected} projected`;
}

/** One part of a day's XP, for steps or sleep, which need no workouts. */
export function dayPartXp(
  row: Pick<ActivityDayRow, "day" | "steps" | "sleepScore" | "paid">,
  part: "steps" | "sleep",
  now: Date = new Date(),
): PartXp {
  return dayXp(row, [], now).parts.find((p) => p.part === part)!;
}

/** What the rows from `from` to `to` paid in total for one part (after the multiplier). */
export function paidBetween(
  rows: readonly Pick<ActivityDayRow, "day" | "paid">[],
  part: XpPart,
  from: string,
  to: string,
): number {
  return paidFor(
    rows.filter((r) => r.day >= from && r.day <= to),
    part,
  );
}

/** The days a range of rows paid in total for one part (after the multiplier). */
export function paidFor(rows: readonly Pick<ActivityDayRow, "paid">[], part: XpPart): number {
  return rows.reduce((sum, r) => sum + (r.paid[part]?.paid ?? 0), 0);
}

/**
 * A habit's lifecycle (REWARD-2): Building → Established (graduated) | Abandoned. When a
 * Building habit meets its establish target, LifeOS suggests establishing it and Chris
 * confirms; confirming is an ordinary status change, which pays and graduates it.
 */
import type { HabitEstablishment, HabitRow, HabitTargetRequest } from "@/lib/production-ui-types";
import { isTerminal } from "@/lib/production-ui-types";

export type HabitStage = "building" | "established" | "abandoned";

export function habitStage(habit: Pick<HabitRow, "status">): HabitStage {
  const status = (habit.status ?? "").trim().toLowerCase();
  if (status === "established") return "established";
  if (isTerminal(status)) return "abandoned";
  return "building";
}

export function isReadyToEstablish(habit: Pick<HabitRow, "status" | "establishment">): boolean {
  return habitStage(habit) === "building" && !!habit.establishment?.targetMet;
}

function percent(n: number): string {
  return `${Math.round(n * 100)}%`;
}

/** "91% over the last 60 days · target 80%", plus how long until the window has run. */
export function establishmentProgress(e: HabitEstablishment): string {
  const target = `target ${percent(e.minAdherence)}`;
  const over = `over the last ${e.windowDays} day${e.windowDays === 1 ? "" : "s"}`;
  const adherence =
    e.adherence === null || e.adherence === undefined
      ? `Nothing recorded ${over} yet`
      : `${percent(e.adherence)} ${over}`;
  const wait = e.windowDays - e.ageDays;
  const age = wait > 0 ? ` · ${wait} more day${wait === 1 ? "" : "s"} before it can graduate` : "";
  return `${adherence} · ${target}${age}`;
}

/**
 * What's typed into the establish-target fields — only the ones the editor touched. Chris
 * types a percentage ("80") and a number of days ("60").
 */
export interface TargetDraft {
  percent?: string;
  days?: string;
}

const MAX_WINDOW_DAYS = 3650;

/** The fields pre-filled from the target the habit has now: "80" and "60". */
export function targetFields(e: HabitEstablishment): { percent: string; days: string } {
  return { percent: String(Math.round(e.minAdherence * 10000) / 100), days: String(e.windowDays) };
}

/** The first reason the typed target would be refused (`HabitTargetService`), or null. */
export function targetProblem(draft: TargetDraft): string | null {
  if (draft.percent !== undefined) {
    const n = Number(draft.percent);
    if (!draft.percent.trim() || !(n > 0 && n <= 100)) {
      return "The establish target is a percentage above 0 and up to 100.";
    }
  }
  if (draft.days !== undefined) {
    const n = Number(draft.days);
    if (!draft.days.trim() || !Number.isInteger(n) || n < 1 || n > MAX_WINDOW_DAYS) {
      return `The window is a whole number of days from 1 to ${MAX_WINDOW_DAYS}.`;
    }
  }
  return null;
}

function changed(typed: string | undefined, now: string): boolean {
  return typed !== undefined && Number(typed) !== Number(now);
}

/**
 * The write the draft needs, or null when nothing changed: only the parts that differ from
 * the target the habit has now are sent, so the other part keeps what it is. The percentage
 * goes as a fraction (80 → 0.8), so 1 means 1%, never "the whole of the days".
 */
export function targetWrite(
  habit: string,
  e: HabitEstablishment | null | undefined,
  draft: TargetDraft,
): HabitTargetRequest | null {
  if (!e) return null;
  const now = targetFields(e);
  const req: HabitTargetRequest = { habit };
  if (changed(draft.percent, now.percent)) req.minAdherence = Number(draft.percent) / 100;
  if (changed(draft.days, now.days)) req.windowDays = Number(draft.days);
  return req.minAdherence !== undefined || req.windowDays !== undefined ? req : null;
}

/** True while the draft would write something (or would be refused for what's typed). */
export function targetDirty(e: HabitEstablishment | null | undefined, draft: TargetDraft): boolean {
  if (!e) return false;
  const now = targetFields(e);
  return changed(draft.percent, now.percent) || changed(draft.days, now.days);
}

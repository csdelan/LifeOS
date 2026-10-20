/**
 * A habit's lifecycle (REWARD-2): Building → Established (graduated) | Abandoned. When a
 * Building habit meets its establish target, LifeOS suggests establishing it and Chris
 * confirms; confirming is an ordinary status change, which pays and graduates it.
 */
import type { HabitEstablishment, HabitRow } from "@/lib/production-ui-types";
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

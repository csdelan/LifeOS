/**
 * The habit days the app offers come from the kernel's, never from a reckoning of its own (code
 * review, web data #1). The kernel lists a habit's day (`bsk_derived.habit_occurrence_source`) when
 * its pattern schedules it, up to today, or when something has been recorded on it, and a past day
 * it lists with nothing recorded reads as missed. So a day the occurrences read returns is listed,
 * and only a listed day can be missed: `AdherenceService` refuses a miss on any other, which would
 * give the habit a day it never had, counted against it for good. A tracked habit on a day the
 * kernel doesn't list (not one of its days, or a habit with no pattern) can still log a session
 * done anyway.
 */
import type { HabitOccurrenceRow, HabitRow } from "@/lib/production-ui-types";

/** A habit's day as the app offers it. */
export interface HabitDay extends HabitOccurrenceRow {
  /** The kernel lists the day, so it can be missed; false for a day the app only offers to log. */
  listed: boolean;
}

/** The kernel's row for `habit` on `day` among `occurrences`, or an unrecorded day to log on. */
export function habitDay(habit: HabitRow, day: string, occurrences: readonly HabitOccurrenceRow[]): HabitDay {
  const row = occurrences.find((o) => o.habitId === habit.id && o.occurrenceDate === day);
  if (row) return { ...row, listed: true };
  return {
    habitId: habit.id,
    habitUrn: habit.urn,
    habitName: habit.name,
    occurrenceDate: day,
    state: "unrecorded",
    allowsPartial: habit.allowsPartial,
    listed: false,
  };
}

/** What a habit's day says it stands at: its state, or that it isn't one of the habit's days. */
export function habitDayLabel(day: HabitDay, today: string): string {
  if (!day.listed) return day.occurrenceDate === today ? "Not due today" : "Not due";
  return day.state === "unrecorded" ? "Unrecorded" : day.state.replace("_", " ");
}

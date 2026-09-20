import type { HabitOccurrenceRow, HabitRow } from "@/lib/production-ui-types";

/**
 * Consecutive `followed` days, walking backward from today.
 * Today's unrecorded cell does not break the streak (the period is still open).
 * Partial and not-followed break it. Past unrecorded also breaks it (period closed).
 */
export function recomputeStreak(
  occurrences: HabitOccurrenceRow[],
  today: string,
): { currentStreak: number; lastState: string | null } {
  const mine = [...occurrences].sort((a, b) =>
    b.occurrenceDate.localeCompare(a.occurrenceDate),
  );
  const last = mine.find((o) => o.occurrenceDate <= today);
  let streak = 0;
  for (const row of mine) {
    if (row.occurrenceDate > today) continue;
    if (row.occurrenceDate === today && row.state === "unrecorded") continue;
    if (row.state === "followed") streak += 1;
    else break;
  }
  return { currentStreak: streak, lastState: last?.state ?? null };
}

export function applyStreakToHabit(
  habit: HabitRow,
  occurrences: HabitOccurrenceRow[],
  today: string,
): HabitRow {
  const { currentStreak, lastState } = recomputeStreak(
    occurrences.filter((o) => o.habitId === habit.id),
    today,
  );
  return { ...habit, currentStreak, lastState };
}

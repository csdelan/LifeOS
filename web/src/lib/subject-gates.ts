/**
 * The kernel's two gates (`SubjectGates` in C#, GEN-8 and GEN-3, code review CR125): a Goal can't
 * be Active without a target date, and a Habit serves at least one Goal or Identity Statement.
 * The kernel refuses them from every door; the forms say them first, before anything is written,
 * so focus can move to the field. Each refuses only the change that breaks it, so an item made
 * before the gate is left alone. `tests/rules/gates.json` holds this copy to the kernel's answers.
 */

export const GOAL_NEEDS_TARGET_DATE = "A Goal can't be Active without a target date.";

export const HABIT_NEEDS_PARENT = "A Habit needs at least one Goal or Identity Statement it serves.";

const isActive = (status: string | null | undefined) => status?.trim().toLowerCase() === "active";

/**
 * Why a Goal going from `statusBefore` (none for a new Goal) to `statusAfter`, with or without a
 * target date before and after, is refused, or null: it's refused when it leaves the Goal Active
 * without a target date and the Goal wasn't already so.
 */
export function goalRefusal(
  statusBefore: string | null,
  targetBefore: boolean,
  statusAfter: string | null,
  targetAfter: boolean,
): string | null {
  const brokenBefore = isActive(statusBefore) && !targetBefore;
  const brokenAfter = isActive(statusAfter) && !targetAfter;
  return brokenAfter && !brokenBefore ? GOAL_NEEDS_TARGET_DATE : null;
}

/**
 * Why a Habit going from `parentsBefore` parents (null for a new Habit) to `parentsAfter` is
 * refused, or null: a new Habit needs one, and a Habit that has one keeps one.
 */
export function habitRefusal(parentsBefore: number | null, parentsAfter: number): string | null {
  return parentsAfter < 1 && (parentsBefore === null || parentsBefore >= 1) ? HABIT_NEEDS_PARENT : null;
}

/**
 * The Primary focus in words (TODAY-1, D6, focus plan G1), as the review's card and Focus both
 * say it: whether Chris chose it or it's the date rule's suggestion, and whether the Goal he chose
 * has ended. TODAY-1 asks that what he chose and what's suggested read differently.
 */
import type { DashboardRead } from "@/lib/reads";

type FocusGoalRead = DashboardRead["primaryFocus"];

export interface FocusWords {
  /** Where it came from: chosen, or suggested; null when there's no Active Goal at all. */
  source: string | null;
  /** The Goal Chris chose and why it no longer leads, once it has ended. */
  lapsed: string | null;
}

export const SUGGESTED = "Suggested: the Active Goal with the nearest target date.";

export function focusWords(focus: FocusGoalRead): FocusWords {
  const lapsed = focus.lapsedTitle
    ? `Your focus, ${focus.lapsedTitle}, is ${(focus.lapsedAs ?? "no longer Active").toLowerCase()}.`
    : null;
  if (focus.chosen) {
    // A review's title is "Weekly review, Sun Nov 22"; chosen with bsk, there's no review.
    const source = focus.chosenIn
      ? `Chosen in your ${focus.chosenIn.charAt(0).toLowerCase()}${focus.chosenIn.slice(1)}.`
      : "Chosen by you.";
    return { source, lapsed };
  }
  return { source: focus.subjectId ? SUGGESTED : null, lapsed };
}

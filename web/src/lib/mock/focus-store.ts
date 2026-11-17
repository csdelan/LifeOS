/**
 * The mock's half of the Primary focus (TODAY-1, D6, focus plan G1): the Goal last chosen, as
 * `FocusService` keeps it (`bsk.v_focus`), and Focus's pick over it (`FocusReader.PrimaryAsync`).
 * The choice lives in the main store's `focusChoice`, so a reset re-seeds it. Refusals are the
 * kernel's in words, but the .NET tests hold the rules.
 */
import { nowIso } from "@/lib/dates";
import type { FocusChoiceRead } from "@/lib/features/focus/types";
import type { SubjectListItem } from "@/lib/production-ui-types";
import { byRef, emit, getState, saveReviewBody } from "@/lib/mock/store";

/** The newest choice: a Goal, or none (`goalId` null), and the review it was chosen in. */
export interface MockFocusChoice {
  goalId: string | null;
  reviewId: string | null;
  chosenAt: string;
}

/** The choice as Focus reads it, with its review's title. */
export interface FocusChoiceInput {
  goalId: string | null;
  chosenAt: string;
  chosenIn: string | null;
}

const statusOrNew = (status: string | null | undefined) => status?.trim() || "New";

/** Why an item can't be the focus (`PrimaryFocus.WhyNot`), or null when it can. */
export function whyNotFocus(item: SubjectListItem): string | null {
  if (item.type !== "Goal") return `'${item.title}' is a ${item.type}. The Primary focus is a Goal.`;
  if (item.archived) return `'${item.title}' is archived. Restore it first, or choose another Goal.`;
  if (statusOrNew(item.status) !== "Active") {
    return `'${item.title}' is ${statusOrNew(item.status)}. The Primary focus is an Active Goal: make it Active first, or choose another.`;
  }
  return null;
}

/** How a chosen Goal that no longer leads reads: Archived, or its status. */
export function lapsedAs(goal: SubjectListItem): string {
  return goal.archived ? "Archived" : statusOrNew(goal.status);
}

function read(choice: MockFocusChoice | null, written: boolean): FocusChoiceRead {
  const goal = choice?.goalId ? byRef(choice.goalId) : undefined;
  const review = choice?.reviewId ? getState().reviews.find((r) => r.id === choice.reviewId) : undefined;
  const holds = goal !== undefined && whyNotFocus(goal) === null;
  return {
    level: "weekly",
    goalId: goal?.id ?? null,
    goalUrn: goal?.urn ?? null,
    goalTitle: goal?.title ?? null,
    holds,
    lapsedAs: goal && !holds ? lapsedAs(goal) : null,
    reviewId: review?.id ?? null,
    reviewTitle: review?.title ?? null,
    chosenAt: choice?.chosenAt ?? null,
    written,
  };
}

/** Focus's input: the newest choice, with the title of the review it was made in. */
export function mockFocusChoice(): FocusChoiceInput | null {
  const choice = getState().focusChoice;
  if (!choice) return null;
  const review = choice.reviewId ? getState().reviews.find((r) => r.id === choice.reviewId) : undefined;
  return { goalId: choice.goalId, chosenAt: choice.chosenAt, chosenIn: review?.title ?? null };
}

/**
 * `POST /api/focus/primary`: an Active Goal becomes the focus, or `null` chooses none. Choosing
 * what's already chosen writes nothing; chosen in a review, that review's Keep focus turns off.
 */
export function mockSetPrimaryFocus(goalRef: string | null, reviewId?: string): FocusChoiceRead {
  const state = getState();
  const review = reviewId ? state.reviews.find((r) => r.id === reviewId) : undefined;
  if (reviewId && !review) throw new Error(`No review matches '${reviewId}'.`);
  const goal = goalRef ? byRef(goalRef) : undefined;
  if (goalRef && !goal) throw new Error(`No subject matches '${goalRef}'.`);
  const why = goal ? whyNotFocus(goal) : null;
  if (why) throw new Error(why);

  const current = state.focusChoice;
  const goalId = goal?.id ?? null;
  if ((current?.goalId ?? null) === goalId && (current !== null || goalId === null)) {
    return read(current, false);
  }

  state.focusChoice = { goalId, reviewId: review?.id ?? null, chosenAt: nowIso() };
  if (review) saveReviewBody(review.id, { keepFocus: false });
  emit();
  return read(state.focusChoice, true);
}

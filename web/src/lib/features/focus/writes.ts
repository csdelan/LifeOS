/**
 * The Primary focus's writes: what the write seam offers, how the live app sends each to the
 * API, and what each does besides itself (`lib/write-effects.ts`).
 */
import { fits } from "@/lib/live/fits";
import { api, unwrap } from "@/lib/live/http";
import { qk } from "@/lib/query-keys";
import type { Effect } from "@/lib/write-effects";
import type { FocusChoiceRead } from "@/lib/features/focus/types";

export interface FocusWrites {
  /**
   * Make an Active Goal the Primary focus, or choose No focus with `null` (TODAY-1, D6,
   * `POST /api/focus/primary`). `reviewId` is the review it's chosen in, whose Keep focus the
   * kernel turns off. Anything but an Active, unarchived Goal is refused in words.
   */
  setPrimaryFocus(goalId: string | null, reviewId?: string): Promise<FocusChoiceRead>;
}

export const liveFocusWrites: FocusWrites = {
  async setPrimaryFocus(goalId, reviewId) {
    return fits<FocusChoiceRead>()(await unwrap(
      api().POST("/api/focus/primary", { body: { goalId, reviewId: reviewId ?? null } }),
    ));
  },
};

/**
 * The focus leads Focus, with its projects; chosen in a review, that review's Keep focus turns
 * off, so the reviews refresh too. The Goal's and the review's histories gain the choice.
 */
export const focusEffects: { [K in keyof FocusWrites]: Effect<K> } = {
  setPrimaryFocus: {
    says: (answer, [goalId]) =>
      !answer.written
        ? "Focus kept"
        : goalId
          ? `Focus: ${answer.goalTitle ?? "the Goal you chose"}`
          : "Focus cleared: Focus suggests a Goal",
    refreshes: ([goalId, reviewId]) => [
      qk.dashboard,
      qk.reviews,
      ...(goalId ? [qk.history(goalId)] : []),
      ...(reviewId ? [qk.history(reviewId)] : []),
    ],
  },
};

import { useSyncExternalStore } from "react";

import { todayIso } from "@/lib/dates";

/** How often an open page checks whether the home day has turned while it's in view. */
export const HOME_DAY_CHECK_MS = 30_000;

/**
 * Calls `onCheck` whenever the home day may have turned: every half minute, and the moment the
 * page comes back into view or focus, since a hidden tab's timers can sleep for hours. The
 * caller compares the day itself.
 */
export function subscribeHomeDay(onCheck: () => void): () => void {
  const onVisible = () => {
    if (document.visibilityState === "visible") onCheck();
  };
  const timer = window.setInterval(onCheck, HOME_DAY_CHECK_MS);
  document.addEventListener("visibilitychange", onVisible);
  window.addEventListener("focus", onCheck);
  window.addEventListener("pageshow", onCheck);
  return () => {
    window.clearInterval(timer);
    document.removeEventListener("visibilitychange", onVisible);
    window.removeEventListener("focus", onCheck);
    window.removeEventListener("pageshow", onCheck);
  };
}

/**
 * Today in the home zone, which re-renders the component when the day turns (code review CR58).
 * A page left open overnight otherwise keeps the day it loaded, and a morning check-off lands on
 * yesterday, where the append-only log and the ledger keep it.
 */
export function useHomeToday(): string {
  return useSyncExternalStore(subscribeHomeDay, todayIso);
}

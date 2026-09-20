import type { AdherenceState } from "@/lib/production-ui-types";

/** Surface colors for habit cells and one-click buttons. Done is the only solid green. */
export const HABIT_STATE_SURFACE: Record<AdherenceState, string> = {
  unrecorded:
    "border border-dashed border-muted-foreground/40 bg-transparent text-muted-foreground",
  followed: "border border-success/40 bg-success text-success-foreground",
  partial:
    "border border-yellow-500/60 bg-yellow-400/35 text-yellow-950 dark:border-yellow-400/50 dark:bg-yellow-400/25 dark:text-yellow-100",
  not_followed: "border border-destructive/45 bg-destructive/15 text-destructive",
};

/** Light row tint so Today's habits (and similar lists) read the same states. */
export const HABIT_STATE_ROW: Record<AdherenceState, string> = {
  unrecorded: "",
  followed: "border border-success/40 bg-success/15",
  partial:
    "border border-yellow-500/60 bg-yellow-400/30 dark:border-yellow-400/50 dark:bg-yellow-400/20",
  not_followed: "border border-destructive/45 bg-destructive/15",
};

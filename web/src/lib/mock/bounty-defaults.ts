import { linesFor } from "@/lib/bounty";
import type { BountyTypeDefaults } from "@/lib/production-ui-types";

/** The mock's `GET /api/bounty/defaults`. */
export const BOUNTY_DEFAULTS: BountyTypeDefaults[] = (["Goal", "Habit", "Project", "Task"] as const).map(
  (type) => ({ type, lines: linesFor(type, {}) }),
);

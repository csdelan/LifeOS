import { addDays, weekdayIndex } from "@/lib/dates";
import type { RecurrenceSpec } from "@/lib/production-ui-types";

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"] as const;

export function parseRecurrence(raw?: string | null): RecurrenceSpec {
  if (!raw || raw === "daily") return { kind: "daily" };
  if (raw === "weekdays") return { kind: "weekly", weekdays: [1, 2, 3, 4, 5] };
  if (raw === "weekly") return { kind: "weekly", weekdays: [0] };
  try {
    const parsed = JSON.parse(raw) as RecurrenceSpec;
    if (parsed && (parsed.kind === "daily" || parsed.kind === "weekly" || parsed.kind === "interval")) {
      return parsed;
    }
  } catch {
    /* plain string */
  }
  return { kind: "daily" };
}

export function serializeRecurrence(spec: RecurrenceSpec): string {
  return JSON.stringify(spec);
}

export function recurrenceLabel(spec: RecurrenceSpec): string {
  if (spec.kind === "daily") return "Daily";
  if (spec.kind === "interval") {
    const n = Math.max(1, spec.intervalDays ?? 1);
    return n === 1 ? "Every day" : `Every ${n} days`;
  }
  const days = spec.weekdays?.length ? spec.weekdays : [0, 1, 2, 3, 4, 5, 6];
  if (days.length === 7) return "Weekly (every day)";
  if (days.length === 5 && days.every((d, i) => d === i + 1)) return "Weekdays";
  return `Weekly (${days.map((d) => WEEKDAYS[d]).join(", ")})`;
}

export function isOccurrenceDate(
  spec: RecurrenceSpec,
  iso: string,
  start?: string | null,
  end?: string | null,
): boolean {
  if (start && iso < start) return false;
  if (end && iso > end) return false;
  if (spec.kind === "daily") return true;
  if (spec.kind === "weekly") {
    const days = spec.weekdays?.length ? spec.weekdays : [0, 1, 2, 3, 4, 5, 6];
    return days.includes(weekdayIndex(iso));
  }
  const interval = Math.max(1, spec.intervalDays ?? 1);
  if (!start) return true;
  let cursor = start;
  if (cursor > iso) return false;
  let guard = 0;
  while (cursor < iso && guard < 4000) {
    cursor = addDays(cursor, interval);
    guard += 1;
  }
  return cursor === iso;
}

export { WEEKDAYS };

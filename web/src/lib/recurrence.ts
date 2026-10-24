import { addDays, daysInMonth, weekdayIndex } from "@/lib/dates";
import type { IntervalUnit, RecurrenceSpec, WeekdayToken } from "@/lib/production-ui-types";

/** Short weekday labels, Sunday first (index = JS `getDay()`). */
const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"] as const;
const WEEKDAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"] as const;
/** The kernel's weekday tokens, Sunday first (D8). */
export const WEEKDAY_TOKENS: readonly WeekdayToken[] = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];
export const INTERVAL_UNITS: readonly IntervalUnit[] = ["days", "weeks", "months"];

export const DAILY: RecurrenceSpec = { freq: "daily" };

/**
 * Reads a stored repeat pattern into the kernel's shape, or `null` when there is none or it
 * can't be read. It takes the kernel object as text (what the API returns), that text
 * quoted once more (a pattern the kernel stored as a string before migration 0041), the web's
 * old `{kind, weekdays, intervalDays}` shape, and the mock's words `daily`, `weekdays` and
 * `weekly` — the same shapes `bsk.recurrence_from_text` repairs.
 */
export function parseRecurrence(raw?: string | null): RecurrenceSpec | null {
  const text = raw?.trim();
  if (!text) return null;
  if (text === "daily") return DAILY;
  if (text === "weekdays") return { freq: "weekly", on: ["mon", "tue", "wed", "thu", "fri"] };
  if (text === "weekly") return { freq: "weekly", on: ["sun"] };
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return null;
  }
  if (typeof parsed === "string") return parseRecurrence(parsed);
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return null;
  const o = parsed as Record<string, unknown>;
  if (typeof o.freq === "string") return normalize(o);
  if (typeof o.kind === "string") return fromLegacy(o);
  return null;
}

function normalize(o: Record<string, unknown>): RecurrenceSpec | null {
  switch (o.freq) {
    case "daily":
      return DAILY;
    case "weekly": {
      const on = weekdayTokens(o.on);
      return on.length ? { freq: "weekly", on } : null;
    }
    case "interval": {
      const every = wholeNumber(o.every);
      const unit = INTERVAL_UNITS.find((u) => u === o.unit);
      return every && every >= 1 && unit ? { freq: "interval", every, unit } : null;
    }
    case "monthly": {
      const day = wholeNumber(o.day);
      if (day && day >= 1 && day <= 31) return { freq: "monthly", day };
      const on = Array.isArray(o.on) ? o.on : [o.on];
      return on.includes("last") ? { freq: "monthly", on: ["last"] } : null;
    }
    case "trigger":
      return typeof o.cue === "string" && o.cue.trim() ? { freq: "trigger", cue: o.cue.trim() } : null;
    default:
      return null;
  }
}

function fromLegacy(o: Record<string, unknown>): RecurrenceSpec | null {
  if (o.kind === "daily") return DAILY;
  if (o.kind === "weekly") {
    const days = Array.isArray(o.weekdays)
      ? [...new Set(o.weekdays.filter((d): d is number => Number.isInteger(d) && d >= 0 && d <= 6))]
      : [];
    // The old editor read an empty list as every day.
    return days.length ? { freq: "weekly", on: WEEKDAY_TOKENS.filter((_, i) => days.includes(i)) } : DAILY;
  }
  if (o.kind === "interval") {
    const n = wholeNumber(o.intervalDays);
    return { freq: "interval", every: Math.max(1, n ?? 1), unit: "days" };
  }
  return null;
}

function weekdayTokens(value: unknown): WeekdayToken[] {
  const list = Array.isArray(value) ? value : [value];
  const wanted = new Set(list.filter((x): x is string => typeof x === "string").map((x) => x.trim().toLowerCase()));
  return WEEKDAY_TOKENS.filter((d) => wanted.has(d));
}

function wholeNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isInteger(value)) return value;
  if (typeof value === "string" && /^\d+$/.test(value.trim())) return Number(value.trim());
  return null;
}

/** The pattern as the kernel stores it, keys in a stable order. */
export function serializeRecurrence(spec: RecurrenceSpec): string {
  const out: Record<string, unknown> = { freq: spec.freq };
  if (spec.freq === "weekly") out.on = WEEKDAY_TOKENS.filter((d) => spec.on?.includes(d));
  if (spec.freq === "interval") {
    out.every = spec.every;
    out.unit = spec.unit;
  }
  if (spec.freq === "monthly") {
    if (spec.day) out.day = spec.day;
    else out.on = "last";
  }
  if (spec.freq === "trigger") out.cue = spec.cue;
  return JSON.stringify(out);
}

/** True when two stored patterns (as text) mean the same thing; two absent ones are equal. */
export function sameRecurrence(a?: string | null, b?: string | null): boolean {
  const x = parseRecurrence(a);
  const y = parseRecurrence(b);
  if (!x || !y) return !x && !y;
  return serializeRecurrence(x) === serializeRecurrence(y);
}

function ordinal(n: number): string {
  const rem100 = n % 100;
  if (rem100 >= 11 && rem100 <= 13) return `${n}th`;
  return `${n}${({ 1: "st", 2: "nd", 3: "rd" } as Record<number, string>)[n % 10] ?? "th"}`;
}

/** A short plain reading: "Daily", "Every Wednesday", "Every 30 days", "Monthly on the 1st". */
export function recurrenceLabel(spec: RecurrenceSpec): string {
  switch (spec.freq) {
    case "daily":
      return "Daily";
    case "weekly": {
      const days = WEEKDAY_TOKENS.map((d, i) => (spec.on?.includes(d) ? i : -1)).filter((i) => i >= 0);
      if (days.length === 7) return "Every day";
      if (days.length === 5 && days.every((d, i) => d === i + 1)) return "Weekdays";
      if (days.length === 2 && days[0] === 0 && days[1] === 6) return "Weekends";
      if (days.length === 1) return `Every ${WEEKDAY_NAMES[days[0]]}`;
      return `Every ${days.map((d) => WEEKDAYS[d]).join(", ")}`;
    }
    case "interval": {
      const n = Math.max(1, spec.every ?? 1);
      const unit = spec.unit ?? "days";
      if (n === 1) return unit === "days" ? "Every day" : `Every ${unit.slice(0, -1)}`;
      return `Every ${n} ${unit}`;
    }
    case "monthly":
      return spec.day ? `Monthly on the ${ordinal(spec.day)}` : "Monthly on the last day";
    case "trigger":
      return spec.cue ? `On a cue: ${spec.cue}` : "On a cue";
  }
}

/** Months added the way Postgres adds them: the day clamps to the month's last day. */
function addMonthsClamped(iso: string, months: number): string {
  const [y, m, d] = iso.split("-").map(Number);
  const first = new Date(y, m - 1 + months, 1);
  const yy = first.getFullYear();
  const mm = String(first.getMonth() + 1).padStart(2, "0");
  const last = daysInMonth(`${yy}-${mm}-01`);
  return `${yy}-${mm}-${String(Math.min(d, last)).padStart(2, "0")}`;
}

/**
 * Whether `iso` is one of the pattern's dates, the way `bsk.recurrence_occurrences` expands
 * it: an interval counts from `start`; a monthly day skips months without it; a cue has no
 * dates.
 */
export function isOccurrenceDate(
  spec: RecurrenceSpec,
  iso: string,
  start?: string | null,
  end?: string | null,
): boolean {
  if (start && iso < start) return false;
  if (end && iso > end) return false;
  switch (spec.freq) {
    case "daily":
      return true;
    case "weekly":
      return spec.on?.includes(WEEKDAY_TOKENS[weekdayIndex(iso)]) ?? false;
    case "monthly": {
      const dom = Number(iso.slice(8, 10));
      return spec.day ? dom === spec.day : dom === daysInMonth(iso);
    }
    case "trigger":
      return false;
    case "interval": {
      if (!start) return true;
      const every = Math.max(1, spec.every ?? 1);
      const unit = spec.unit ?? "days";
      let cursor = start;
      for (let guard = 0; cursor < iso && guard < 4000; guard += 1) {
        cursor =
          unit === "days"
            ? addDays(cursor, every)
            : unit === "weeks"
              ? addDays(cursor, every * 7)
              : addMonthsClamped(cursor, every);
      }
      return cursor === iso;
    }
  }
}

export { WEEKDAYS };

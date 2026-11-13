/** Date helpers matching kernel conventions: `YYYY-MM-DD` and ISO timestamps. */

import { homeTimeZone } from "@/lib/public-config";

const homeFormats = new Map<string, Intl.DateTimeFormat | null>();

function homeFormat(timeZone: string): Intl.DateTimeFormat | null {
  if (!homeFormats.has(timeZone)) {
    try {
      homeFormats.set(
        timeZone,
        new Intl.DateTimeFormat("en-US", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }),
      );
    } catch {
      // A zone this browser doesn't know: its own clock's day, as before the home zone.
      homeFormats.set(timeZone, null);
    }
  }
  return homeFormats.get(timeZone) ?? null;
}

/**
 * The day `instant` falls on in the home zone (code review CR22): the day the kernel files a
 * check-off or a capture under, whatever zone the browser is in. `timeZone` defaults to the API's.
 */
export function homeDayAt(instant: Date, timeZone: string = homeTimeZone()): string {
  const format = homeFormat(timeZone);
  if (!format) {
    const y = instant.getFullYear();
    const m = String(instant.getMonth() + 1).padStart(2, "0");
    const d = String(instant.getDate()).padStart(2, "0");
    return `${y}-${m}-${d}`;
  }
  const part = (type: Intl.DateTimeFormatPartTypes) =>
    format.formatToParts(instant).find((p) => p.type === type)?.value ?? "";
  return `${part("year")}-${part("month")}-${part("day")}`;
}

/** Today in the home zone, `YYYY-MM-DD`. */
export function todayIso(): string {
  return homeDayAt(new Date());
}

export function addDays(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split("-").map(Number);
  const dt = new Date(y, m - 1, d);
  dt.setDate(dt.getDate() + days);
  const yy = dt.getFullYear();
  const mm = String(dt.getMonth() + 1).padStart(2, "0");
  const dd = String(dt.getDate()).padStart(2, "0");
  return `${yy}-${mm}-${dd}`;
}

export function nowIso(): string {
  return new Date().toISOString();
}

export function compareIsoDate(a: string, b: string): number {
  return a.localeCompare(b);
}

export function formatDay(iso: string | null | undefined): string {
  if (!iso) return "";
  const [y, m, d] = iso.split("-").map(Number);
  const dt = new Date(y, m - 1, d);
  return dt.toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
  });
}

export function formatDayFull(iso: string | null | undefined): string {
  if (!iso) return "";
  const [y, m, d] = iso.split("-").map(Number);
  const dt = new Date(y, m - 1, d);
  return dt.toLocaleDateString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

export function formatTimestamp(iso: string): string {
  const dt = new Date(iso);
  return dt.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

/**
 * Whether an item due on `due` is overdue on `today` (both `yyyy-MM-dd`): its due date has passed
 * and it isn't finished. `closed` is whether it is (`isTerminal` of its status): a Completed,
 * Missed or Cancelled item's due date is just a day. The one rule for Browse's Overdue, the Tasks
 * page's groups, Focus and every date chip (code review CR74).
 */
export function isOverdue(due: string | null | undefined, closed: boolean, today: string): boolean {
  return !closed && !!due && due < today;
}

export function relativeDayLabel(iso: string, today = todayIso()): string {
  if (iso === today) return "Today";
  if (iso === addDays(today, -1)) return "Yesterday";
  if (iso === addDays(today, 1)) return "Tomorrow";
  if (iso < today) {
    const days = Math.round(
      (Date.parse(`${today}T00:00:00`) - Date.parse(`${iso}T00:00:00`)) /
        86_400_000,
    );
    return days === 1 ? "1 day overdue" : `${days} days overdue`;
  }
  return formatDay(iso);
}

export function localDateFromTimestamp(iso: string): string {
  const dt = new Date(iso);
  const y = dt.getFullYear();
  const m = String(dt.getMonth() + 1).padStart(2, "0");
  const day = String(dt.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

export function weekdayIndex(iso: string): number {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(y, m - 1, d).getDay();
}

/** The Sunday on or before the day (the mock home walk-throughs' Sundays). */
export function startOfWeekSunday(iso: string): string {
  return addDays(iso, -weekdayIndex(iso));
}

/** The Monday starting the Monday-to-Sunday week holding the day: the app's week. */
export function startOfWeekMonday(iso: string): string {
  return addDays(iso, -((weekdayIndex(iso) + 6) % 7));
}

/**
 * The Sunday ending the Monday-to-Sunday week holding the day: the day itself on a Sunday.
 * A weekly review is named by this Sunday (REVIEW-2).
 */
export function endOfWeekSunday(iso: string): string {
  return addDays(startOfWeekMonday(iso), 6);
}

export function startOfMonth(iso: string): string {
  const [y, m] = iso.split("-").map(Number);
  return `${y}-${String(m).padStart(2, "0")}-01`;
}

export function addMonths(iso: string, months: number): string {
  const [y, m, d] = iso.split("-").map(Number);
  const dt = new Date(y, m - 1 + months, d);
  const yy = dt.getFullYear();
  const mm = String(dt.getMonth() + 1).padStart(2, "0");
  const dd = String(dt.getDate()).padStart(2, "0");
  return `${yy}-${mm}-${dd}`;
}

export function daysInMonth(iso: string): number {
  const [y, m] = iso.split("-").map(Number);
  return new Date(y, m, 0).getDate();
}

export function slugify(title: string): string {
  const slug = title
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 40);
  return slug || "item";
}

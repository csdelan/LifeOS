import type { AppointmentRow, MaterializeResult } from "@/lib/production-ui-types";
import { formatDay } from "@/lib/dates";

/** Whether an appointment fills the day (the kernel stores the flag as text). */
export function isAllDay(row: Pick<AppointmentRow, "allDay">): boolean {
  const flag = (row.allDay ?? "").trim().toLowerCase();
  return flag === "true" || flag === "t";
}

/** "07:00" as "7:00 AM"; anything else as it came. */
export function formatTimeOfDay(hhmm: string | null | undefined): string {
  const match = /^(\d{1,2}):(\d{2})/.exec((hhmm ?? "").trim());
  if (!match) return hhmm?.trim() ?? "";
  const hours = Number(match[1]);
  const suffix = hours < 12 ? "AM" : "PM";
  const twelve = hours % 12 === 0 ? 12 : hours % 12;
  return `${twelve}:${match[2]} ${suffix}`;
}

/** When an appointment is, at a glance: "All day", "9:15 AM – 10:00 AM", "9:15 AM", or "". */
export function appointmentWhen(row: Pick<AppointmentRow, "allDay" | "startTime" | "endTime">): string {
  if (isAllDay(row)) return "All day";
  const start = formatTimeOfDay(row.startTime);
  const end = formatTimeOfDay(row.endTime);
  if (start && end) return `${start} – ${end}`;
  return start || end;
}

/** What Materialize did, in words for its toast. */
export function materializedMessage(result: MaterializeResult, through: string): string {
  const n = result.created.length;
  if (n === 0) return `Every occurrence through ${formatDay(through)} was already made`;
  return `Made ${n} occurrence${n === 1 ? "" : "s"} through ${formatDay(through)}`;
}

/** A day's appointments in calendar order: all-day ones first, then by start time, then title. */
export function sortAppointments(rows: AppointmentRow[]): AppointmentRow[] {
  return [...rows].sort((a, b) => {
    const allDay = Number(isAllDay(b)) - Number(isAllDay(a));
    if (allDay !== 0) return allDay;
    const startA = a.startTime ?? "99:99";
    const startB = b.startTime ?? "99:99";
    if (startA !== startB) return startA < startB ? -1 : 1;
    return a.title.localeCompare(b.title);
  });
}

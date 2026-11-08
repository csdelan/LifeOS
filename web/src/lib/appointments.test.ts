import { describe, expect, it } from "vitest";
import { appointmentWhen, formatTimeOfDay, sortAppointments } from "@/lib/appointments";
import type { AppointmentRow } from "@/lib/production-ui-types";

function row(title: string, extra: Partial<AppointmentRow> = {}): AppointmentRow {
  return { id: title, urn: title, title, status: "Scheduled", archived: false, createdAt: "", ...extra };
}

describe("appointment times", () => {
  it("reads a time of day on a 12-hour clock", () => {
    expect(formatTimeOfDay("07:00")).toBe("7:00 AM");
    expect(formatTimeOfDay("12:30")).toBe("12:30 PM");
    expect(formatTimeOfDay("00:15")).toBe("12:15 AM");
    expect(formatTimeOfDay("18:45:00")).toBe("6:45 PM");
    expect(formatTimeOfDay(null)).toBe("");
  });

  it("says when an appointment is at a glance", () => {
    expect(appointmentWhen(row("a", { allDay: "true", startTime: "09:00" }))).toBe("All day");
    expect(appointmentWhen(row("b", { startTime: "09:15", endTime: "10:00" }))).toBe("9:15 AM – 10:00 AM");
    expect(appointmentWhen(row("c", { startTime: "09:15" }))).toBe("9:15 AM");
    expect(appointmentWhen(row("d"))).toBe("");
  });

  it("puts all-day appointments first, then the rest by start time", () => {
    const sorted = sortAppointments([
      row("Dinner", { startTime: "18:30" }),
      row("Untimed"),
      row("Market", { allDay: "t" }),
      row("Dentist", { startTime: "09:15" }),
    ]);
    expect(sorted.map((r) => r.title)).toEqual(["Market", "Dentist", "Dinner", "Untimed"]);
  });
});

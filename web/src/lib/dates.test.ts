import { afterEach, describe, expect, it, vi } from "vitest";
import { homeDayAt, isOverdue, todayIso } from "@/lib/dates";

// 06:30 UTC on 15 February 2003: 10:30 pm on the 14th in Pacific time. UTC+14 (Kiritimati)
// and UTC−11 (Pago Pago) are a day apart, as in the kernel's HomeCalendarTests.
const instant = new Date("2003-02-15T06:30:00Z");

describe("the home zone's day (code review CR22)", () => {
  afterEach(() => {
    delete window.__LIFEOS_PUBLIC_CONFIG__;
    vi.useRealTimers();
  });

  it.each([
    ["America/Los_Angeles", "2003-02-14"],
    ["UTC", "2003-02-15"],
    ["Pacific/Kiritimati", "2003-02-15"],
    ["Pacific/Pago_Pago", "2003-02-14"],
  ])("reads an instant on %s's date", (zone, expected) => {
    expect(homeDayAt(instant, zone)).toBe(expected);
  });

  it("counts today in the API's home zone, whatever zone the browser is in", () => {
    vi.useFakeTimers();
    vi.setSystemTime(instant);

    window.__LIFEOS_PUBLIC_CONFIG__ = { homeTimeZone: "Pacific/Kiritimati" };
    expect(todayIso()).toBe("2003-02-15");
    window.__LIFEOS_PUBLIC_CONFIG__ = { homeTimeZone: "Pacific/Pago_Pago" };
    expect(todayIso()).toBe("2003-02-14");
  });

  it("falls back to the browser's own day without a home zone, or with one it doesn't know", () => {
    const local = `${instant.getFullYear()}-${String(instant.getMonth() + 1).padStart(2, "0")}-${String(instant.getDate()).padStart(2, "0")}`;

    expect(homeDayAt(instant, "Mars/Olympus")).toBe(local);
    vi.useFakeTimers();
    vi.setSystemTime(instant);
    expect(todayIso()).toBe(local);
  });
});

describe("overdue (code review CR74)", () => {
  it("is a due date passed on an item not finished", () => {
    expect(isOverdue("2026-10-25", false, "2026-10-27")).toBe(true);
    expect(isOverdue("2026-10-25", true, "2026-10-27")).toBe(false);
    expect(isOverdue("2026-10-27", false, "2026-10-27")).toBe(false);
    expect(isOverdue("2026-10-28", false, "2026-10-27")).toBe(false);
    expect(isOverdue(null, false, "2026-10-27")).toBe(false);
  });
});

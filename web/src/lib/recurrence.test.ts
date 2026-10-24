import { describe, expect, it } from "vitest";
import {
  isOccurrenceDate,
  parseRecurrence,
  recurrenceLabel,
  sameRecurrence,
  serializeRecurrence,
} from "@/lib/recurrence";

describe("parseRecurrence", () => {
  it("reads the kernel's object as text", () => {
    expect(parseRecurrence('{"on": ["wed"], "freq": "weekly"}')).toEqual({ freq: "weekly", on: ["wed"] });
    expect(parseRecurrence('{"freq":"interval","every":30,"unit":"days"}')).toEqual({
      freq: "interval",
      every: 30,
      unit: "days",
    });
    expect(parseRecurrence('{"freq":"monthly","on":"last"}')).toEqual({ freq: "monthly", on: ["last"] });
    expect(parseRecurrence('{"freq":"monthly","day":1}')).toEqual({ freq: "monthly", day: 1 });
    expect(parseRecurrence('{"freq":"trigger","cue":"after lunch"}')).toEqual({ freq: "trigger", cue: "after lunch" });
  });

  it("puts weekdays in Sunday-first order and drops unknown ones", () => {
    expect(parseRecurrence('{"freq":"weekly","on":["fri","MON","funday"]}')).toEqual({
      freq: "weekly",
      on: ["mon", "fri"],
    });
  });

  it("reads a pattern stored as a string before migration 0041", () => {
    expect(parseRecurrence(JSON.stringify('{"freq":"daily"}'))).toEqual({ freq: "daily" });
  });

  it("reads the web's old shape and the mock's words, as migration 0041 repairs them", () => {
    expect(parseRecurrence('{"kind":"weekly","weekdays":[3]}')).toEqual({ freq: "weekly", on: ["wed"] });
    expect(parseRecurrence('{"kind":"weekly","weekdays":[]}')).toEqual({ freq: "daily" });
    expect(parseRecurrence('{"kind":"interval","intervalDays":3}')).toEqual({ freq: "interval", every: 3, unit: "days" });
    expect(parseRecurrence("weekdays")).toEqual({ freq: "weekly", on: ["mon", "tue", "wed", "thu", "fri"] });
    expect(parseRecurrence("weekly")).toEqual({ freq: "weekly", on: ["sun"] });
  });

  it("gives null for nothing or anything it can't read", () => {
    expect(parseRecurrence(undefined)).toBeNull();
    expect(parseRecurrence("")).toBeNull();
    expect(parseRecurrence("fortnightly")).toBeNull();
    expect(parseRecurrence('{"freq":"weekly","on":[]}')).toBeNull();
    expect(parseRecurrence('{"freq":"interval","every":0,"unit":"days"}')).toBeNull();
  });
});

describe("serializeRecurrence", () => {
  it("writes the kernel's shape", () => {
    expect(serializeRecurrence({ freq: "weekly", on: ["fri", "mon"] })).toBe('{"freq":"weekly","on":["mon","fri"]}');
    expect(serializeRecurrence({ freq: "monthly", on: ["last"] })).toBe('{"freq":"monthly","on":"last"}');
    expect(serializeRecurrence({ freq: "interval", every: 30, unit: "days" })).toBe(
      '{"freq":"interval","every":30,"unit":"days"}',
    );
  });

  it("compares patterns by meaning", () => {
    expect(sameRecurrence('{"on": ["mon", "fri"], "freq": "weekly"}', '{"freq":"weekly","on":["fri","mon"]}')).toBe(true);
    expect(sameRecurrence(undefined, "")).toBe(true);
    expect(sameRecurrence('{"freq":"daily"}', "")).toBe(false);
  });
});

describe("recurrenceLabel", () => {
  it("reads plainly", () => {
    expect(recurrenceLabel({ freq: "daily" })).toBe("Daily");
    expect(recurrenceLabel({ freq: "weekly", on: ["wed"] })).toBe("Every Wednesday");
    expect(recurrenceLabel({ freq: "weekly", on: ["mon", "wed", "fri"] })).toBe("Every Mon, Wed, Fri");
    expect(recurrenceLabel({ freq: "weekly", on: ["mon", "tue", "wed", "thu", "fri"] })).toBe("Weekdays");
    expect(recurrenceLabel({ freq: "weekly", on: ["sun", "sat"] })).toBe("Weekends");
    expect(recurrenceLabel({ freq: "interval", every: 30, unit: "days" })).toBe("Every 30 days");
    expect(recurrenceLabel({ freq: "interval", every: 1, unit: "weeks" })).toBe("Every week");
    expect(recurrenceLabel({ freq: "monthly", day: 1 })).toBe("Monthly on the 1st");
    expect(recurrenceLabel({ freq: "monthly", day: 22 })).toBe("Monthly on the 22nd");
    expect(recurrenceLabel({ freq: "monthly", day: 13 })).toBe("Monthly on the 13th");
    expect(recurrenceLabel({ freq: "monthly", on: ["last"] })).toBe("Monthly on the last day");
  });
});

describe("isOccurrenceDate", () => {
  it("matches the expander's weekly, monthly and cue dates", () => {
    // 2026-10-28 is a Wednesday.
    expect(isOccurrenceDate({ freq: "weekly", on: ["wed"] }, "2026-10-28")).toBe(true);
    expect(isOccurrenceDate({ freq: "weekly", on: ["wed"] }, "2026-10-29")).toBe(false);
    expect(isOccurrenceDate({ freq: "monthly", day: 31 }, "2026-11-30")).toBe(false);
    expect(isOccurrenceDate({ freq: "monthly", on: ["last"] }, "2026-11-30")).toBe(true);
    expect(isOccurrenceDate({ freq: "trigger", cue: "after lunch" }, "2026-11-30")).toBe(false);
  });

  it("counts an interval from the start date, months clamped the way Postgres adds them", () => {
    const every3 = { freq: "interval", every: 3, unit: "days" } as const;
    expect(isOccurrenceDate(every3, "2026-10-07", "2026-10-01")).toBe(true);
    expect(isOccurrenceDate(every3, "2026-10-08", "2026-10-01")).toBe(false);
    expect(isOccurrenceDate({ freq: "interval", every: 2, unit: "weeks" }, "2026-10-15", "2026-10-01")).toBe(true);
    const monthly = { freq: "interval", every: 1, unit: "months" } as const;
    expect(isOccurrenceDate(monthly, "2026-02-28", "2026-01-31")).toBe(true);
    expect(isOccurrenceDate(monthly, "2026-03-28", "2026-01-31")).toBe(true);
    expect(isOccurrenceDate(monthly, "2026-03-31", "2026-01-31")).toBe(false);
  });

  it("keeps to the start and end dates", () => {
    expect(isOccurrenceDate({ freq: "daily" }, "2026-09-30", "2026-10-01")).toBe(false);
    expect(isOccurrenceDate({ freq: "daily" }, "2026-10-02", "2026-10-01", "2026-10-01")).toBe(false);
  });
});

import { describe, expect, it } from "vitest";
import { formatDay } from "@/lib/dates";
import { changeText, fieldLabel } from "@/lib/history";

describe("an item's history in words (agent-access plan §5.6 step 2.4)", () => {
  it("names a field by its label on the item's form", () => {
    expect(fieldLabel("Task", "due")).toBe("Due date");
    expect(fieldLabel("Task", "estimated_duration")).toBe("Estimated duration");
    expect(fieldLabel("Task", "title")).toBe("Title");
    expect(fieldLabel("Person", "title")).toBe("Name");
    expect(fieldLabel("Goal", "target_date")).toBe("Target date");
  });

  it("names what LifeOS keeps besides the form, and any other key in words", () => {
    expect(fieldLabel("Goal", "cover")).toBe("Cover");
    expect(fieldLabel("Habit", "bounty")).toBe("Bounty");
    expect(fieldLabel(undefined, "date_identified")).toBe("Date identified");
  });

  it("says a change from and to, a value set, a value cleared, and a pattern only as changed", () => {
    expect(changeText({ field: "due", from: "2026-11-19", to: "2026-11-21", plain: true })).toBe(
      `${formatDay("2026-11-19")} → ${formatDay("2026-11-21")}`,
    );
    expect(changeText({ field: "priority", from: null, to: "High", plain: true })).toBe("set to High");
    expect(changeText({ field: "priority", from: "High", to: null, plain: true })).toBe("cleared (was High)");
    expect(changeText({ field: "recurrence", from: null, to: null, plain: false })).toBe("changed");
  });
});

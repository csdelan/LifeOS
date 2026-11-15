import { describe, expect, it } from "vitest";
import { emptyFormValues, groupFields, groupTitle, isDetailDirty, visibleFields } from "@/lib/subject-forms";
import type { SubjectType } from "@/lib/production-ui-types";

describe("read-only → edit dirty logic", () => {
  it("is never dirty while read-only", () => {
    expect(isDetailDirty(false, true)).toBe(false);
    expect(isDetailDirty(false, false)).toBe(false);
  });

  it("is dirty only after Edit when the form has changes", () => {
    expect(isDetailDirty(true, false)).toBe(false);
    expect(isDetailDirty(true, true)).toBe(true);
  });
});

describe("the Overview's groups", () => {
  function groupsOf(type: SubjectType, attrs: Record<string, string> = {}) {
    const values = { ...emptyFormValues(type, "edit"), attrs: { ...emptyFormValues(type, "edit").attrs, ...attrs } };
    const groups = groupFields(visibleFields(type, "edit", values));
    return Object.fromEntries(Object.entries(groups).map(([g, fields]) => [g, fields.map((f) => f.key)]));
  }

  it("keeps a repeating Task's pattern and its start together, apart from its other fields", () => {
    const groups = groupsOf("Task", { recurrence: JSON.stringify({ freq: "weekly", on: ["wed"] }) });
    expect(groups.repeat).toEqual(["recurrence", "start"]);
    // Its dates are its repetitions', so it has none of its own.
    expect(groups.when).toBeUndefined();
    expect(groups.about).toEqual(["description"]);
  });

  it("opens the details with the status and ends them with the area", () => {
    expect(groupsOf("Task").details).toEqual(["status", "priority", "estimated_duration", "area"]);
    expect(groupsOf("Appointment").details).toEqual(["status", "location", "meeting_link", "attendees", "area"]);
    expect(groupsOf("Task").when).toEqual(["due", "scheduled"]);
  });

  it("gives a Habit its loop and its schedule", () => {
    const groups = groupsOf("Habit");
    expect(groups.loop).toEqual(["cue", "routine", "reward"]);
    expect(groups.repeat).toEqual(["recurrence", "start", "end"]);
    expect(groupTitle("repeat", "Habit")).toBe("Schedule");
    expect(groupTitle("repeat", "Task")).toBe("Repeats");
    expect(groupTitle("when", "Appointment")).toBe("When");
  });
});

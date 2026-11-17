import { describe, expect, it } from "vitest";
import {
  emptyFormValues,
  formParentId,
  groupFields,
  groupTitle,
  isDetailDirty,
  subGoalDateWarning,
  subjectFormSchema,
  visibleFields,
  type SubjectFormValues,
} from "@/lib/subject-forms";
import { GOAL_NEEDS_TARGET_DATE, HABIT_NEEDS_PARENT } from "@/lib/subject-gates";
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

// The kernel's gates, said by the form before anything is written (code review CR125); the
// rule's own cases are in tests/rules/gates.json.
describe("a Goal's and a Habit's gates on the form", () => {
  function issues(values: Partial<SubjectFormValues> & Pick<SubjectFormValues, "type" | "mode">) {
    const parsed = subjectFormSchema.safeParse({ ...emptyFormValues(values.type, values.mode), title: "It", ...values });
    return parsed.success ? [] : parsed.error.issues.map((i) => ({ path: i.path.join("."), message: i.message }));
  }
  const goal = (status: string, targetDate: string, shown?: SubjectFormValues["shown"]) =>
    issues({ type: "Goal", mode: shown ? "edit" : "create", status, parent: "value-1", attrs: { target_date: targetDate }, shown });

  it("keeps a Goal from becoming Active without a target date, on the Target date field", () => {
    expect(goal("Active", "")).toEqual([{ path: "attrs.target_date", message: GOAL_NEEDS_TARGET_DATE }]);
    expect(goal("Active", "", { status: "New", targetDate: false, parents: 1 })).toHaveLength(1);
    expect(goal("Active", "", { status: "Active", targetDate: true, parents: 1 })).toHaveLength(1);
    expect(goal("Active", "2027-06-30")).toEqual([]);
    expect(goal("New", "")).toEqual([]);
  });

  it("leaves an Active Goal made before the gate, with no target date, editable", () => {
    expect(goal("Active", "", { status: "Active", targetDate: false, parents: 1 })).toEqual([]);
  });

  it("keeps a Goal under an Identity Statement or a Goal (focus plan G2)", () => {
    const parented = (parent: string) => issues({ type: "Goal", mode: "create", status: "New", parent });
    expect(parented("")).toEqual([
      { path: "parent", message: "Connect this Goal to an Identity Statement, or put it under a Goal." },
    ]);
    expect(parented("goal-half")).toEqual([]);
  });

  it("keeps a Habit serving a Goal or Identity Statement, unless it never did", () => {
    const habit = (parents: string[], shown?: SubjectFormValues["shown"]) =>
      issues({ type: "Habit", mode: shown ? "edit" : "create", parents, shown });
    expect(habit([])).toEqual([{ path: "parents", message: HABIT_NEEDS_PARENT }]);
    expect(habit([], { status: "Building", targetDate: false, parents: 1 })).toHaveLength(1);
    expect(habit(["goal-1"])).toEqual([]);
    expect(habit([], { status: "Building", targetDate: false, parents: 0 })).toEqual([]);
  });
});

describe("a sub-goal's form (focus plan G2)", () => {
  it("shows a Goal's Identity Statement as its parent, or the Goal it's under when it serves none", () => {
    const value = { type: "Value" as const, subjectId: "v1" };
    const goal = { type: "Goal" as const, subjectId: "g1" };
    expect(formParentId("Goal", [goal, value])).toBe("v1");
    expect(formParentId("Goal", [goal])).toBe("g1");
    expect(formParentId("Goal", [])).toBeUndefined();
    // A Habit's Goals are its parents list, not its one parent.
    expect(formParentId("Habit", [goal])).toBeUndefined();
  });

  it("warns, without refusing, when a sub-goal's target date is after its parent Goal's", () => {
    const half = { type: "Goal" as const, title: "Run a half marathon", targetDate: "2027-05-01" };
    expect(subGoalDateWarning("2027-06-01", half)).toMatch(/^After Run a half marathon's target date, /);
    expect(subGoalDateWarning("2027-05-01", half)).toBeUndefined();
    expect(subGoalDateWarning("2027-04-01", half)).toBeUndefined();
    expect(subGoalDateWarning("", half)).toBeUndefined();
    expect(subGoalDateWarning("2027-06-01", { ...half, targetDate: null })).toBeUndefined();
    expect(subGoalDateWarning("2027-06-01", { ...half, type: "Value" })).toBeUndefined();
    expect(subGoalDateWarning("2027-06-01", undefined)).toBeUndefined();
  });
});

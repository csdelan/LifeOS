import { describe, expect, it, vi } from "vitest";
import type { SubjectType } from "@/lib/production-ui-types";
import { emptyFormValues, type SubjectFormValues } from "@/lib/subject-forms";
import { applyRelationshipWrites, relationshipWrites } from "./subject-save";

function form(type: SubjectType, patch: Partial<SubjectFormValues> = {}): SubjectFormValues {
  const base = emptyFormValues(type, "edit");
  return { ...base, ...patch, attrs: { ...base.attrs, ...patch.attrs } };
}

describe("relationshipWrites", () => {
  it("involves an edited Appointment's new attendees and lets go of the removed ones", () => {
    // Edit used not to involve attendees at all; create did.
    const before = form("Appointment", { attrs: { attendees: "ann, bo" } });
    const after = form("Appointment", { attrs: { attendees: "bo,cy" } });
    expect(relationshipWrites("Appointment", "appt", after, { values: before, edges: [] })).toEqual([
      { kind: "involve", subject: "appt", person: "cy", role: "attendee" },
      { kind: "involve", subject: "appt", person: "ann", role: "attendee", remove: true },
    ]);
  });

  it("moves a Goal to another identity statement, taking the old link away", () => {
    const before = form("Goal", { parent: "v1" });
    const after = form("Goal", { parent: "v2" });
    expect(
      relationshipWrites("Goal", "g", after, { values: before, edges: [{ subjectId: "v1", relation: "serves" }] }),
    ).toEqual([
      { kind: "link", from: "g", relation: "serves", to: "v2" },
      { kind: "link", from: "g", relation: "serves", to: "v1", remove: true },
    ]);
  });

  it("leaves a Goal's statement alone when the form didn't change it", () => {
    const values = form("Goal", { parent: "v1" });
    expect(
      relationshipWrites("Goal", "g", values, { values, edges: [{ subjectId: "v1", relation: "serves" }] }),
    ).toEqual([]);
  });

  it("links a Habit's ticked parents and unlinks an unticked one", () => {
    const before = form("Habit", { parent: "goal-a", parents: ["goal-a", "value-b"] });
    const after = form("Habit", { parent: "goal-a", parents: ["goal-a", "goal-c"] });
    const edges = [
      { subjectId: "goal-a", relation: "serves" as const },
      { subjectId: "value-b", relation: "serves" as const },
    ];
    expect(relationshipWrites("Habit", "h", after, { values: before, edges })).toEqual([
      { kind: "link", from: "h", relation: "serves", to: "goal-c" },
      { kind: "link", from: "h", relation: "serves", to: "value-b", remove: true },
    ]);
  });

  it("makes the same writes for a new item, less the parent its create already linked", () => {
    const habit = form("Habit", { parent: "goal-a", parents: ["goal-a", "goal-c"] });
    expect(relationshipWrites("Habit", "h", habit, null, "goal-a")).toEqual([
      { kind: "link", from: "h", relation: "serves", to: "goal-c" },
    ]);
    const appointment = form("Appointment", { attrs: { attendees: "ann" } });
    expect(relationshipWrites("Appointment", "appt", appointment, null)).toEqual([
      { kind: "involve", subject: "appt", person: "ann", role: "attendee" },
    ]);
    const commitment = form("Commitment", { person: "sam" });
    expect(relationshipWrites("Commitment", "c", commitment, null)).toEqual([
      { kind: "involve", subject: "c", person: "sam", role: "involves" },
    ]);
  });
});

describe("applyRelationshipWrites", () => {
  it("sends each write in order", async () => {
    const calls: string[] = [];
    const client = {
      link: vi.fn(async (from: string, relation: string, to: string, remove?: boolean) => {
        calls.push(`link ${from} ${relation} ${to}${remove ? " remove" : ""}`);
      }),
      involve: vi.fn(async (subject: string, person: string, role: string, remove?: boolean) => {
        calls.push(`involve ${subject} ${person} ${role}${remove ? " remove" : ""}`);
      }),
    };
    await applyRelationshipWrites(
      [
        { kind: "link", from: "g", relation: "serves", to: "v2" },
        { kind: "involve", subject: "a", person: "p", role: "attendee", remove: true },
      ],
      client,
    );
    expect(calls).toEqual(["link g serves v2", "involve a p attendee remove"]);
  });
});

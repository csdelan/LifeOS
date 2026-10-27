import { describe, expect, it } from "vitest";
import {
  RESULTING_WORK_TYPES,
  canStartDecisionFrom,
  decisionGroups,
  decisionLinkWrite,
  pendingLinkParent,
  pendingLinkStatus,
  pendingLinkTypes,
  pendingLinkView,
  pendingLinkWrite,
  type LinkedItem,
  type PendingLink,
} from "@/lib/decisions";
import type { RelationEdge } from "@/lib/production-ui-types";
import { addEdge, createSubject, earnStatus, getState, resetStore } from "@/lib/mock/store";

const decision: LinkedItem = { id: "dec-1", title: "Use SQLite for local storage", type: "Decision", status: "Open" };
const problem: LinkedItem = { id: "prob-1", title: "The laptop runs out of disk", type: "Problem", status: "Open" };

function edge(relation: RelationEdge["relation"], subjectId: string, type: RelationEdge["type"]): RelationEdge {
  return { relation, subjectId, type, urn: `urn:bsk:${subjectId}`, title: subjectId };
}

describe("decisionGroups", () => {
  it("splits results_in by direction and supersedes into Replaces and Replaced by", () => {
    const groups = decisionGroups({
      // Outgoing: this Decision → the other.
      parents: [
        edge("results_in", "task-1", "Task"),
        edge("results_in", "proj-1", "Project"),
        edge("supersedes", "dec-old", "Decision"),
        edge("serves", "cns-1", "Constraint"),
      ],
      // Incoming: the other → this Decision.
      children: [
        edge("results_in", "prob-1", "Problem"),
        edge("supersedes", "dec-new", "Decision"),
        edge("serves", "proj-2", "Project"),
      ],
    });
    expect(groups.context.map((e) => e.subjectId)).toEqual(["prob-1"]);
    expect(groups.resultingWork.map((e) => e.subjectId)).toEqual(["task-1", "proj-1"]);
    expect(groups.replaces.map((e) => e.subjectId)).toEqual(["dec-old"]);
    expect(groups.replacedBy.map((e) => e.subjectId)).toEqual(["dec-new"]);
    expect(groups.other.map((o) => [o.label, o.edge.subjectId])).toEqual([
      ["Serves", "cns-1"],
      ["Served by", "proj-2"],
    ]);
  });

  it("is empty for a Decision with no links", () => {
    expect(decisionGroups({ parents: [], children: [] })).toEqual({
      context: [],
      resultingWork: [],
      replaces: [],
      replacedBy: [],
      other: [],
    });
  });
});

describe("pending links", () => {
  const resulting: PendingLink = { kind: "resulting-work", decision };
  const context: PendingLink = { kind: "context", item: problem };
  const replacement: PendingLink = { kind: "replacement", decision };

  it("offers trackable work for resulting work, and only a Decision otherwise", () => {
    expect(pendingLinkTypes(resulting)).toEqual(RESULTING_WORK_TYPES);
    expect(RESULTING_WORK_TYPES.slice(0, 3)).toEqual(["Goal", "Project", "Task"]);
    expect(RESULTING_WORK_TYPES).not.toContain("Decision");
    expect(pendingLinkTypes(context)).toEqual(["Decision"]);
    expect(pendingLinkTypes(replacement)).toEqual(["Decision"]);
  });

  it("writes resulting work as the Decision results in the new item, after the create", () => {
    expect(pendingLinkParent(resulting)).toBeNull();
    expect(pendingLinkWrite(resulting, "task-9")).toEqual({ from: "dec-1", relation: "results_in", to: "task-9" });
    expect(pendingLinkStatus(resulting)).toBeNull();
  });

  it("writes context as the item results in the new Decision, after the create", () => {
    expect(pendingLinkParent(context)).toBeNull();
    expect(pendingLinkWrite(context, "dec-9")).toEqual({ from: "prob-1", relation: "results_in", to: "dec-9" });
    expect(pendingLinkStatus(context)).toBeNull();
  });

  it("makes a replacement supersede its original in the create, and cancels the original", () => {
    expect(pendingLinkParent(replacement)).toEqual({ parent: "dec-1", relation: "supersedes" });
    expect(pendingLinkWrite(replacement, "dec-9")).toBeNull();
    expect(pendingLinkStatus(replacement)).toEqual({ subject: "dec-1", status: "Cancelled" });
  });

  it("leaves an original that's already Cancelled as it is", () => {
    const cancelled: PendingLink = { kind: "replacement", decision: { ...decision, status: "Cancelled" } };
    expect(pendingLinkStatus(cancelled)).toBeNull();
    expect(pendingLinkView(cancelled, "Decision").note).toMatch(/stays Cancelled/);
  });

  it("says what each link is before Save", () => {
    expect(pendingLinkView(resulting, "Task")).toMatchObject({ heading: "Resulting work of", item: decision });
    expect(pendingLinkView(resulting, "Task").note).toMatch(/status stays as it is/);
    expect(pendingLinkView(context, "Decision")).toMatchObject({
      heading: "Context",
      item: problem,
      note: "Saved as what prompted this Decision.",
    });
    expect(pendingLinkView(replacement, "Decision")).toMatchObject({ heading: "Replaces", item: decision });
    expect(pendingLinkView(replacement, "Decision").note).toMatch(/marks it Cancelled/);
  });
});

describe("decisionLinkWrite", () => {
  it("adds an existing item to a Decision's groups in the right direction", () => {
    expect(decisionLinkWrite("context", "dec-1", "prob-1")).toEqual({
      from: "prob-1",
      relation: "results_in",
      to: "dec-1",
    });
    expect(decisionLinkWrite("resulting-work", "dec-1", "task-1")).toEqual({
      from: "dec-1",
      relation: "results_in",
      to: "task-1",
    });
    expect(decisionLinkWrite("replaces", "dec-1", "dec-0")).toEqual({
      from: "dec-1",
      relation: "supersedes",
      to: "dec-0",
    });
  });
});

describe("canStartDecisionFrom", () => {
  it("offers a Decision from work and thinking, not from a Decision, Area, person or reward", () => {
    for (const type of ["Problem", "Idea", "Goal", "Project", "Task", "Value", "Habit"] as const) {
      expect(canStartDecisionFrom(type)).toBe(true);
    }
    for (const type of ["Decision", "Area", "Person", "Reward"] as const) {
      expect(canStartDecisionFrom(type)).toBe(false);
    }
  });
});

describe("a Decision's status is moved by hand", () => {
  it("stays put when its context and resulting work finish or are cancelled", () => {
    resetStore();
    const work = createSubject({ type: "Task", title: "Scaffold the Vite app" });
    addEdge("dec-stack", "results_in", work.id);
    addEdge("prob-sleep", "results_in", "dec-stack");

    earnStatus(work.id, "Completed");
    earnStatus("goal-lifeos", "Completed");
    earnStatus("prob-sleep", "Resolved");
    expect(getState().details["dec-stack"]?.status).toBe("Implementing");

    earnStatus(work.id, "Cancelled");
    expect(getState().details["dec-stack"]?.status).toBe("Implementing");
  });
});

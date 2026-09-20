import { describe, expect, it } from "vitest";
import {
  defaultStatus,
  inferChildRelation,
  isTerminal,
} from "@/lib/production-ui-types";

describe("status folding + defaultStatus", () => {
  it("defaults to the first vocabulary entry, or empty when status-less", () => {
    expect(defaultStatus("Task")).toBe("Not started");
    expect(defaultStatus("Goal")).toBe("New");
    expect(defaultStatus("Value")).toBe("");
    expect(defaultStatus("Habit")).toBe("");
  });

  it("treats completed / cancelled / abandoned as terminal", () => {
    expect(isTerminal("Completed")).toBe(true);
    expect(isTerminal("cancelled")).toBe(true);
    expect(isTerminal("Abandoned")).toBe(true);
    expect(isTerminal("In progress")).toBe(false);
    expect(isTerminal(null)).toBe(false);
  });
});

describe("inferChildRelation", () => {
  it("infers the canonical parent→child relation", () => {
    expect(inferChildRelation("Goal", "Value")).toBe("serves");
    expect(inferChildRelation("Project", "Goal")).toBe("results_in");
    expect(inferChildRelation("Task", "Project")).toBe("serves");
    expect(inferChildRelation("Task", "Goal")).toBe("serves");
    expect(inferChildRelation("Habit", "Goal")).toBeUndefined();
  });
});

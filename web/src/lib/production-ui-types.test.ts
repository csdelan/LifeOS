import { describe, expect, it } from "vitest";
import {
  defaultStatus,
  inferChildRelation,
  isTerminal,
  shortIdOf,
} from "@/lib/production-ui-types";

describe("status folding + defaultStatus", () => {
  it("defaults to the first vocabulary entry, or empty when status-less", () => {
    expect(defaultStatus("Task")).toBe("Not started");
    expect(defaultStatus("Goal")).toBe("New");
    expect(defaultStatus("Value")).toBe("");
    expect(defaultStatus("Habit")).toBe("Building");
    expect(defaultStatus("Area")).toBe("");
  });

  it("treats completed / cancelled / abandoned as terminal", () => {
    expect(isTerminal("Completed")).toBe(true);
    expect(isTerminal("cancelled")).toBe(true);
    expect(isTerminal("Abandoned")).toBe(true);
    expect(isTerminal("Established")).toBe(true);
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

describe("shortIdOf", () => {
  it("takes the trailing hex token the kernel mints onto a URN", () => {
    expect(shortIdOf("urn:bsk:goal:get-fit-a1b2c3")).toBe("a1b2c3");
    expect(shortIdOf("urn:bsk:task:7f3a9c")).toBe("7f3a9c");
    // The mock mints base-36 ids.
    expect(shortIdOf("urn:bsk:goal:ship-lifeos-g10002")).toBe("g10002");
  });

  it("falls back to the whole tail when there is no short id", () => {
    expect(shortIdOf("urn:bsk:project:life-kernel")).toBe("life-kernel");
  });
});

import { describe, expect, it } from "vitest";
import {
  STATUS_BY_TYPE,
  defaultStatus,
  inferChildRelation,
  initialStatus,
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

  it("starts a new Habit Planned and every other type at its default", () => {
    expect(STATUS_BY_TYPE.Habit?.[0]).toBe("Planned");
    expect(initialStatus("Habit")).toBe("Planned");
    expect(initialStatus("Task")).toBe("Not started");
    expect(initialStatus("Value")).toBe("");
    expect(isTerminal("Planned")).toBe(false);
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

// Code review CR83: a hand-written type the generated ones already say is a copy to keep in step
// by hand. Each is aliased to the generated one (`ApiSchema`) once it needs no narrowing, so the
// count of copies only falls. When you alias one, lower COPIES; a new type the API already has
// is an alias from the start.
describe("hand-written copies of the API's types", () => {
  const COPIES = 75;
  const schema = Object.values(
    import.meta.glob<string>("/src/api/schema.d.ts", { query: "?raw", import: "default", eager: true }),
  )[0];
  const components = schema.slice(schema.indexOf("export interface components"));
  const generated = new Set([...components.matchAll(/^ {8}(\w+): /gm)].map((m) => m[1]));
  const sources = import.meta.glob<string>(["/src/**/*.ts", "!/src/**/*.test.ts", "!/src/api/**"], {
    query: "?raw",
    import: "default",
    eager: true,
  });

  it(`number ${COPIES}, and only fall`, () => {
    const copies = Object.entries(sources).flatMap(([path, text]) =>
      [...text.matchAll(/^export (?:interface (\w+)|type (\w+)\s*=\s*\{)/gm)]
        .map((m) => m[1] ?? m[2])
        .filter((name) => generated.has(name))
        .map((name) => `${path}: ${name}`),
    );
    expect(generated.size).toBeGreaterThan(100);
    expect(copies.length, copies.join("\n")).toBe(COPIES);
  });
});

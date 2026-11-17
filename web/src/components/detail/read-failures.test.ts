/**
 * Code review CR131 (fifth review R24, for the item detail): a read that fails in the detail says
 * so. The History, Journal, Relationships and Tags tabs read their data and showed a failure as
 * the empty answer ("No journal yet", "No parents — unaligned for now"), so Chris could take a
 * Goal for unaligned or a journal for empty. Every file of the detail that reads through a hook
 * from `@/lib/queries` handles the read failing (`ReadFailed`, or its own `isError`), unless it's
 * listed here with why.
 */
import { describe, expect, it } from "vitest";

const sources = import.meta.glob<string>(["./*.tsx", "!./*.test.tsx"], {
  query: "?raw",
  import: "default",
  eager: true,
});

/** Files that read and needn't say a failure, with why. */
const exempt: Record<string, string> = {};

/** The read hooks a file imports from `@/lib/queries` (the write seam isn't a read). */
function readHooks(source: string): string[] {
  const imports = [...source.matchAll(/import\s*\{([^}]*)\}\s*from\s*"@\/lib\/queries"/g)].flatMap((m) =>
    m[1].split(",").map((name) => name.replace(/^\s*type\s+/, "").trim()),
  );
  return imports.filter((name) => /^use[A-Z]/.test(name) && name !== "useWrites");
}

describe("the item detail's reads (code review CR131)", () => {
  it("finds the detail's files", () => {
    expect(Object.keys(sources).length).toBeGreaterThan(5);
  });

  it("say when a read failed, never show it as empty", () => {
    const silent = Object.entries(sources)
      .filter(([file, source]) => !(file in exempt) && readHooks(source).length > 0)
      .filter(([, source]) => !/ReadFailed|isError/.test(source))
      .map(([file, source]) => `${file} (${readHooks(source).join(", ")})`);
    expect(silent).toEqual([]);
  });
});

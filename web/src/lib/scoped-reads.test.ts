import { describe, expect, it } from "vitest";

/**
 * A screen reads what it shows (code review CR107). The shell's badge read the whole Inbox for its
 * number, three pickers read every subject to filter them in the browser, the Map read every
 * repetition of every repeating Task to drop them, and a habit's grid read its whole history for a
 * month. Then Browse and the Map read every subject of every type, Spaces, Metrics and a review a
 * day among them; now the server pages Browse and answers what the Map draws (code review CR120).
 * And the shell read every timer ever for the running one, a detail every habit for its own and
 * every journal entry and status change, and the pickers every Goal and Project (code review CR121).
 * These find a read like that coming back, in the app's code (the mock answers them all).
 * `ScopedReadsTests` is the server's twin.
 */
const sources = import.meta.glob<string>(
  ["/src/**/*.{ts,tsx}", "!/src/**/*.test.{ts,tsx}", "!/src/lib/mock/**", "!/src/api/**"],
  { query: "?raw", import: "default", eager: true },
);

/** The text of each call to `name` in `text`, from its `(` to the matching `)`. */
function calls(text: string, name: RegExp): string[] {
  return [...text.matchAll(name)].map((m) => {
    const open = m.index + m[0].length - 1;
    let depth = 0;
    for (let i = open; i < text.length; i++) {
      if (text[i] === "(") depth++;
      else if (text[i] === ")" && --depth === 0) return text.slice(open + 1, i);
    }
    return text.slice(open + 1);
  });
}

/** The files with a call to `name` whose arguments `broad` says read too much. */
function readers(name: RegExp, broad: (args: string) => boolean): string[] {
  return Object.entries(sources)
    .filter(([, text]) => calls(text, name).some(broad))
    .map(([path]) => path)
    .sort();
}

/** The first argument of a call, up to its first comma at the top level. */
function firstArgument(args: string): string {
  let depth = 0;
  for (let i = 0; i < args.length; i++) {
    const ch = args[i];
    if (ch === "(" || ch === "{" || ch === "[") depth++;
    else if (ch === ")" || ch === "}" || ch === "]") depth--;
    else if (ch === "," && depth === 0) return args.slice(0, i);
  }
  return args;
}

/** Each place that reads every live habit, and why. */
const EVERY_HABIT: Record<string, string> = {
  "/src/pages/habits-page.tsx": "The Habits page lists every habit.",
  "/src/components/reviews/review-looking-back.tsx": "A review looks back on every habit's week.",
};

describe("scoped reads (code review CR107, CR120, CR121)", () => {
  it("reads the app's code", () => {
    expect(Object.keys(sources)).toEqual(expect.arrayContaining(["/src/lib/queries.ts", "/src/pages/map-page.tsx"]));
  });

  it("names the type of every subject list it reads, with no place listed that reads them all", () => {
    const everything = readers(/(?<!function )\buseSubjects\(/g, (args) => {
      const type = firstArgument(args).trim();
      return type === "" || /\bundefined\b/.test(type);
    });
    expect(everything).toEqual([]);
  });

  it("lists subjects only through the hooks, which name a type", () => {
    expect(readers(/\.listSubjects\(/g, () => true)).toEqual(["/src/lib/queries.ts"]);
  });

  it("reads Browse a page at a time, and what the Map draws once", () => {
    expect(readers(/(?<!function )\buseBrowse\(/g, () => true)).toEqual(["/src/pages/browse-page.tsx"]);
    expect(readers(/(?<!function )\buseAlignment\(/g, () => true)).toEqual(["/src/lib/queries.ts", "/src/pages/map-page.tsx"]);
  });

  it("reads the whole Inbox only on the Inbox page: the shell's badge reads its count", () => {
    expect(readers(/(?<!function )\buseInbox\(/g, () => true)).toEqual(["/src/pages/inbox-page.tsx"]);
  });

  it("reads a habit's days for a day or a range, never its whole history", () => {
    expect(readers(/\.occurrences\(/g, (args) => !/\b(on|from)\b/.test(args))).toEqual([]);
  });

  it("reads every habit only where a page lists them all, and a detail its own", () => {
    expect(readers(/(?<!function )\buseHabits\(/g, () => true)).toEqual(Object.keys(EVERY_HABIT).sort());
  });

  it("reads the timers of the Tasks shown, the running one, or one Task's, never every timer", () => {
    expect(readers(/\.timers\(/g, (args) => !/\b(active|status)\b/.test(args))).toEqual([]);
    expect(readers(/(?<!function )\buseTimers\(/g, () => true)).toEqual(["/src/pages/tasks-page.tsx"]);
  });

  it("reads a picker's open parents, not every Goal and Project", () => {
    expect(readers(/(?<!function )\buseSubjectsOfTypes\(/g, (args) => !/status:\s*"open"/.test(args))).toEqual([]);
  });

  it("reads an item's journal, status history and a series' repetitions a page at a time", () => {
    // A read, not its cache key (`qk.journal(id)`), which names no limit.
    for (const read of [/(?<!\bqk)\.journal\(/g, /(?<!\bqk)\.history\(/g, /(?<!\bqk)\.taskSeriesHistory\(/g]) {
      expect(readers(read, (args) => !args.includes(","))).toEqual([]);
    }
    for (const hook of [/(?<!function )\buseJournal\(/g, /(?<!function )\buseHistory\(/g, /(?<!function )\buseTaskSeriesHistory\(/g]) {
      expect(readers(hook, (args) => !args.includes(","))).toEqual([]);
    }
  });
});

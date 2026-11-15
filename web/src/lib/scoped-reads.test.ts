import { describe, expect, it } from "vitest";

/**
 * A screen reads what it shows (code review CR107). The shell's badge read the whole Inbox for its
 * number, three pickers read every subject to filter them in the browser, the Map read every
 * repetition of every repeating Task to drop them, and a habit's grid read its whole history for a
 * month. Then Browse and the Map read every subject of every type, Spaces, Metrics and a review a
 * day among them; now the server pages Browse and answers what the Map draws (code review CR120).
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

describe("scoped reads (code review CR107, CR120)", () => {
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
});

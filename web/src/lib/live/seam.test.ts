import { describe, expect, it } from "vitest";

/**
 * The live seam reads what the API sends only through `fits` (code review CR82). `unwrap` hands
 * back an `Unchecked<G>` that only `fits` opens, so tsc checks the API's generated type against
 * the app's. These look for the ways round it that tsc can't see: a call that skips `unwrap`, a
 * cast of what it answered, a normalizer that takes the answer as `unknown`, and HTTP errors made
 * by hand rather than as an `ApiRequestError` (which the query retry and the toasts read).
 */
const sources = import.meta.glob<string>(["/src/**/*.{ts,tsx}", "!/src/**/*.test.{ts,tsx}", "!/src/api/**"], {
  query: "?raw",
  import: "default",
  eager: true,
});

const SEAM = ["/src/lib/live/http.ts", "/src/lib/live/fits.ts"];

/** The text of the call whose `(` is at `open`, up to its matching `)`. */
function callAt(text: string, open: number): { args: string; end: number } {
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === "(") depth++;
    else if (text[i] === ")" && --depth === 0) return { args: text.slice(open + 1, i), end: i + 1 };
  }
  return { args: text.slice(open + 1), end: text.length };
}

function lineOf(text: string, index: number): number {
  return text.slice(0, index).split("\n").length;
}

/** Each `file:line` in the app's code where `find` says the seam is gone round. */
function offenders(find: (path: string, text: string) => number[]): string[] {
  const found = Object.entries(sources)
    .filter(([path]) => !SEAM.includes(path))
    .flatMap(([path, text]) => find(path, text).map((at) => `${path}:${lineOf(text, at)}`));
  return [...new Set(found)];
}

const VERB = /\bapi\(\)\.(GET|POST|PUT|PATCH|DELETE)\(/g;
const UNWRAP = /\b(unwrap|unwrapOrNull|unwrapBlob)\(/g;

describe("the live seam (code review CR82)", () => {
  it("reads the app's code", () => {
    expect(Object.keys(sources)).toEqual(
      expect.arrayContaining(["/src/lib/live/reads.ts", "/src/lib/agents.ts", "/src/lib/me.ts"]),
    );
  });

  it("sends every API call through unwrap", () => {
    const skipped = offenders((_, text) =>
      [...text.matchAll(VERB)]
        .map((m) => m.index)
        .filter((at) => !/\b(unwrap|unwrapOrNull|unwrapBlob)\(\s*$/.test(text.slice(Math.max(0, at - 40), at))),
    );
    expect(skipped).toEqual([]);
  });

  it("casts nothing unwrap answers: fits reads it as the app's type", () => {
    const cast = offenders((_, text) =>
      [...text.matchAll(UNWRAP)].flatMap((m) => {
        const { end } = callAt(text, m.index + m[0].length - 1);
        const after = text.slice(end).match(/^\)*\s*as\s/);
        // `const data = await unwrap(...)`, then `data as ...` further on.
        const named = /\bconst\s+(\w+)\s*=\s*\(?\s*await\s*$/.exec(text.slice(Math.max(0, m.index - 60), m.index));
        const later = named ? [...text.slice(end).matchAll(new RegExp(`\\b${named[1]}\\)?\\s+as\\s`, "g"))] : [];
        return [...(after ? [end] : []), ...later.map((l) => end + l.index)];
      }),
    );
    expect(cast).toEqual([]);
  });

  it("has no normalizer that takes the API's answer as unknown", () => {
    const loose = offenders((path, text) =>
      path.startsWith("/src/lib/mock/")
        ? []
        : [...text.matchAll(/\bfunction\s+normalize\w*\s*\(\s*\w+\s*:\s*unknown\b/g)].map((m) => m.index),
    );
    expect(loose).toEqual([]);
  });

  it("leaves the response's status to unwrap, which throws an ApiRequestError", () => {
    const byHand = offenders((path, text) => [
      ...[...text.matchAll(/\bresponse\.(ok|status)\b/g)].map((m) => m.index),
      ...(path.startsWith("/src/lib/live/") ? [...text.matchAll(/\bthrow new Error\(`API /g)].map((m) => m.index) : []),
    ]);
    expect(byHand).toEqual([]);
  });
});

/**
 * How the Playwright specs are written (code review CR104), read from their source: one wait for
 * every check, set in playwright.config.ts, with a longer one only by a name that says why; one
 * accessibility scan, `expectAccessible` in e2e/support.ts; and a build to run against, not the
 * dev server.
 */
import { describe, expect, it } from "vitest";

const specs = import.meta.glob<string>("/e2e/**/*.spec.ts", { query: "?raw", import: "default", eager: true });
const config = Object.values(
  import.meta.glob<string>("/playwright.config.ts", { query: "?raw", import: "default", eager: true }),
)[0];
const tsconfigs = import.meta.glob<string>(["/tsconfig.json", "/tsconfig.e2e.json"], {
  query: "?raw",
  import: "default",
  eager: true,
});

function where(pattern: RegExp): string[] {
  return Object.entries(specs).flatMap(([file, source]) =>
    source
      .split("\n")
      .map((line, i) => [line, i + 1] as const)
      .filter(([line]) => pattern.test(line))
      .map(([, n]) => `${file}:${n}`),
  );
}

describe("the Playwright specs (code review CR104)", () => {
  it("wait as long as playwright.config.ts says, or by a name that says why", () => {
    expect(config).toMatch(/expect: \{ timeout: \d/);
    // A timeout written into the call: `toBeVisible({ timeout: 10_000 })`, `toHaveCount(3, { timeout ...`.
    expect(where(/[(,]\s*\{\s*timeout:\s*\d/)).toEqual([]);
  });

  it("scan through expectAccessible, not an AxeBuilder of their own", () => {
    expect(where(/AxeBuilder/)).toEqual([]);
  });

  // Code review CR130: a spec that fixes the page's clock (`page.clock.install`, `beforeActivityXp`)
  // works out its days from that fixed time. The test runner's own clock isn't moved, so a day
  // worked out from it disagrees with the page's on some real dates (the 1st and 2nd of a month).
  // Inside `page.evaluate` the page's clock is the fixed one, so it's allowed there.
  it("work out days from the fixed clock, never the real one, in a spec that fixes the page's clock", () => {
    const realClock: string[] = [];
    for (const [file, source] of Object.entries(specs)) {
      if (!/clock\.install|beforeActivityXp\(/.test(source)) continue;
      let inPage = 0;
      source.split("\n").forEach((line, i) => {
        if (/page\.evaluate\(/.test(line)) inPage++;
        else if (inPage > 0 && /^\s*\}\);?\s*$/.test(line)) inPage--;
        else if (inPage === 0 && /new Date\(\)|Date\.now\(\)/.test(line)) realClock.push(`${file}:${i + 1}`);
      });
    }
    expect(realClock).toEqual([]);
  });

  // Code review CR133 (fifth review R34): Playwright strips a spec's types without checking them,
  // so the specs have a tsconfig of their own that `npm run typecheck` (tsc -b) builds.
  it("are type-checked by npm run typecheck", () => {
    expect(tsconfigs["/tsconfig.json"]).toMatch(/"path":\s*"\.\/tsconfig\.e2e\.json"/);
    expect(tsconfigs["/tsconfig.e2e.json"]).toMatch(/"include":\s*\["e2e"\]/);
  });

  it("run against a build of the app, as it ships", () => {
    expect(config).toMatch(/vite build[^"]*&& npx vite preview/);
  });
});

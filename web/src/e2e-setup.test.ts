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

  it("run against a build of the app, as it ships", () => {
    expect(config).toMatch(/vite build[^"]*&& npx vite preview/);
  });
});

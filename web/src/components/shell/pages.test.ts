import { describe, expect, it } from "vitest";
import { NAV_PAGES, PAGES } from "@/components/shell/pages";

/** Routes that aren't pages of their own, and why: nothing lists them, and no accessibility spec opens them. */
const NOT_PAGES: Record<string, string> = {
  "/activity/connected": "where Oura's sign-in returns to, which then opens Activity",
};

const router = Object.values(
  import.meta.glob<string>("/src/router.tsx", { query: "?raw", import: "default", eager: true }),
)[0];
const a11ySpecs = import.meta.glob<string>("/e2e/a11y/*.spec.ts", { query: "?raw", import: "default", eager: true });

/** The paths the router serves, as its route definitions name them. */
function routerPaths(): string[] {
  return [...router.matchAll(/^\s+path: "([^"]+)",$/gm)].map((m) => m[1]);
}

/** Whether an accessibility spec opens the page: a quoted path, on its own or with a search. */
function scanned(to: string): boolean {
  const quoted = [`"${to}"`, `"${to}?`, `\`${to}\``, `\`${to}?`];
  return Object.values(a11ySpecs).some((text) => quoted.some((q) => text.includes(q)));
}

describe("one list of pages (code review CR97)", () => {
  it("names every route the router serves, and no other", () => {
    const listed = [...PAGES.map((p) => p.to), ...Object.keys(NOT_PAGES)].sort();
    expect(routerPaths().sort()).toEqual(listed);
  });

  it("has every page opened by an accessibility spec", () => {
    expect(PAGES.map((p) => p.to).filter((to) => !scanned(to))).toEqual([]);
  });

  it("gives each page its own path and label", () => {
    expect(new Set(PAGES.map((p) => p.to)).size).toBe(PAGES.length);
    expect(new Set(PAGES.map((p) => p.label)).size).toBe(PAGES.length);
  });

  it("keeps the navigation to the pages marked for it, in the same order", () => {
    expect(NAV_PAGES.map((p) => p.to)).toEqual(PAGES.filter((p) => p.nav).map((p) => p.to));
    expect(NAV_PAGES.map((p) => p.to)).not.toContain("/gallery");
  });
});

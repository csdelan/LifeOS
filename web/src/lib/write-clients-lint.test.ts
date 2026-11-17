/**
 * Code review CR137: a write's success is said once, in its `writeEffects` entry, so no write
 * client imports `sonner` (CR55, CR83). The lint rule that says so named the two write clients
 * only, and a feature's `lib/features/<feature>/writes.ts` is a write client too. This reads
 * `.oxlintrc.json` and wants the rule to cover every write client there is.
 */
import { describe, expect, it } from "vitest";

const lint = Object.values(
  import.meta.glob<string>("/.oxlintrc.json", { query: "?raw", import: "default", eager: true }),
)[0];
const writeClients = Object.keys(
  import.meta.glob(["/src/lib/live/write-client.ts", "/src/lib/mock/write-client.ts", "/src/lib/features/*/writes.ts"]),
);

interface Override {
  files: string[];
  rules: Record<string, unknown>;
}

/** Whether `file` (from the web root, no leading slash) matches `glob`: `*` within a folder, `**` across them. */
function matches(glob: string, file: string): boolean {
  const pattern = glob
    .split("**")
    .map((part) => part.split("*").map((s) => s.replace(/[.+?^${}()|[\]\\]/g, "\\$&")).join("[^/]*"))
    .join(".*");
  return new RegExp(`^${pattern}$`).test(file);
}

describe("the write clients' lint rule (code review CR137)", () => {
  it("finds the config and the write clients", () => {
    expect(lint).toBeTruthy();
    expect(writeClients.length).toBeGreaterThan(2);
  });

  it("keeps sonner out of every write client", () => {
    const overrides = (JSON.parse(lint) as { overrides: Override[] }).overrides.filter((o) =>
      JSON.stringify(o.rules["no-restricted-imports"] ?? "").includes('"name":"sonner"'),
    );
    const uncovered = writeClients
      .map((file) => file.replace(/^\//, ""))
      .filter((file) => !overrides.some((o) => o.files.some((glob) => matches(glob, file))));
    expect(uncovered).toEqual([]);
  });
});

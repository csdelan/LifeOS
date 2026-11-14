/**
 * What the production bundle carries (code review CR84), read from the source, since neither
 * tsc nor the build says any of it:
 *
 * - the first page and each page's own chunk leave out what shows only on demand: the Map's
 *   graph, with its layout engine, and the shell's dialogs load when they're first wanted;
 * - code outside `lib/mock` is code the app uses: every module there is reached from `main.tsx`,
 *   and everything it exports, a test and the mock aside, is used by the app. What only the mock
 *   and the tests use goes under `lib/mock`, which the build keeps in a chunk of its own;
 * - every package the app depends on is one it uses.
 */
import { describe, expect, it } from "vitest";

const sources = import.meta.glob<string>(
  ["/src/**/*.{ts,tsx}", "!/src/**/*.test.{ts,tsx}", "!/src/**/*.d.ts", "!/src/api/**", "!/src/test/**"],
  { query: "?raw", import: "default", eager: true },
);
const styles = import.meta.glob<string>("/src/**/*.css", { query: "?raw", import: "default", eager: true });
const tooling = import.meta.glob<string>(
  ["/*.config.ts", "/tsconfig*.json", "/e2e/**/*.ts", "/src/**/*.test.{ts,tsx}", "/src/test/**"],
  { query: "?raw", import: "default", eager: true },
);
const manifest = Object.values(
  import.meta.glob<{
    dependencies: Record<string, string>;
    devDependencies: Record<string, string>;
    scripts: Record<string, string>;
  }>("/package.json", { import: "default", eager: true }),
)[0];

const MOCK = "/src/lib/mock/";
const ENTRY = "/src/main.tsx";

type Edge = {
  /** The module it reaches, or the package for one outside the app. */
  target: string;
  external: boolean;
  dynamic: boolean;
  /** Only types: nothing of it runs. */
  typeOnly: boolean;
  /** The names it takes, or "*" for all of them. */
  names: string[] | "*";
};

function withoutComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/[^\n]*/g, "$1");
}

function resolve(spec: string, from: string): string | null {
  let base: string;
  if (spec.startsWith("@/")) base = "/src/" + spec.slice(2);
  else if (spec.startsWith(".")) {
    const parts = from.split("/").slice(0, -1);
    for (const part of spec.split("/")) {
      if (part === "..") parts.pop();
      else if (part !== ".") parts.push(part);
    }
    base = parts.join("/");
  } else return null;
  for (const candidate of [base, `${base}.ts`, `${base}.tsx`, `${base}/index.ts`, `${base}/index.tsx`]) {
    if (candidate in sources) return candidate;
  }
  return base;
}

function packageOf(spec: string): string {
  const parts = spec.split("/");
  return spec.startsWith("@") ? parts.slice(0, 2).join("/") : parts[0];
}

function namesOf(clause: string): { names: string[] | "*"; typeOnly: boolean } {
  const whole = clause.trim();
  const typeOnly = whole.startsWith("type ");
  const body = typeOnly ? whole.slice(5) : whole;
  if (body.includes("*")) return { names: "*", typeOnly };
  const braced = /\{([^}]*)\}/.exec(body);
  const named = (braced?.[1] ?? "")
    .split(",")
    .map((n) => n.trim())
    .filter(Boolean);
  const values = named.filter((n) => !n.startsWith("type "));
  const names = values.map((n) => n.split(/\s+as\s+/)[0].trim());
  // A default import, before the braces: `import Foo, { bar } from`.
  const head = braced ? body.slice(0, braced.index).replace(",", "").trim() : body;
  if (head && !head.startsWith("{")) names.push("default");
  return { names, typeOnly: typeOnly || (named.length > 0 && values.length === 0 && !head) };
}

function edgesOf(file: string): Edge[] {
  const source = withoutComments(sources[file] ?? "");
  const edges: Edge[] = [];
  const add = (spec: string, dynamic: boolean, names: string[] | "*", typeOnly: boolean) => {
    const target = resolve(spec, file);
    edges.push({ target: target ?? packageOf(spec), external: target === null, dynamic, typeOnly, names });
  };
  for (const m of source.matchAll(/(?:^|[\n;])\s*(import|export)\s+([^"'`;]*?)\s+from\s+["']([^"']+)["']/g)) {
    const { names, typeOnly } = namesOf(m[2]);
    add(m[3], false, names, typeOnly);
  }
  for (const m of source.matchAll(/(?:^|[\n;])\s*import\s+["']([^"']+)["']/g)) add(m[1], false, [], false);
  for (const m of source.matchAll(/import\(\s*["']([^"']+)["']\s*\)/g)) add(m[1], true, "*", false);
  return edges;
}

const graph = new Map(Object.keys(sources).map((file) => [file, edgesOf(file)]));

/** What `from` reaches; `follow` says which edges to walk. The mock is never walked into. */
function reach(from: string[], follow: (edge: Edge) => boolean): Set<string> {
  const seen = new Set<string>();
  const queue = [...from];
  while (queue.length > 0) {
    const file = queue.pop()!;
    if (seen.has(file)) continue;
    seen.add(file);
    for (const edge of graph.get(file) ?? []) {
      // A stylesheet or an asset isn't a module here.
      if (!edge.external && edge.target in sources && follow(edge) && !edge.target.startsWith(MOCK)) {
        queue.push(edge.target);
      }
    }
  }
  return seen;
}

const loadsWith = (edge: Edge) => !edge.dynamic && !edge.typeOnly;
const pages = Object.keys(sources).filter((f) => f.startsWith("/src/pages/") && !f.endsWith("-page.helpers.ts"));
const appModules = reach([ENTRY], () => true);

describe("the bundle (code review CR84)", () => {
  it("leaves the graph and its layout engine out of the first page and every page's chunk", () => {
    const heavy = ["@xyflow/react", "elkjs"];
    const offenders = new Set<string>();
    for (const start of [ENTRY, ...pages]) {
      for (const file of reach([start], loadsWith)) {
        for (const edge of graph.get(file) ?? []) {
          if (edge.external && !edge.typeOnly && heavy.includes(edge.target)) offenders.add(`${start} → ${file}`);
        }
      }
    }
    expect([...offenders].sort()).toEqual([]);
  });

  it("loads the shell's dialogs when they're first opened, not with the first page", () => {
    const onDemand = [
      "/src/components/shell/command-palette.tsx",
      "/src/components/create/new-subject-dialog.tsx",
      "/src/components/create/quick-capture-dialog.tsx",
      "/src/components/create/voice-capture-dialog.tsx",
    ];
    for (const dialog of onDemand) expect(sources[dialog], `${dialog} is where the test thinks`).toBeDefined();
    const first = reach([ENTRY], loadsWith);
    expect(onDemand.filter((d) => first.has(d))).toEqual([]);
  });

  it("keeps the mock's stand-ins under lib/mock, where client.ts reaches them lazily", () => {
    // A stand-in picked at run time (`isLiveApi() ? live() : mock()`) ships with the app, used or not.
    const standIns = Object.entries(sources)
      .filter(([f]) => !f.startsWith(MOCK) && f !== "/src/lib/client.ts")
      .flatMap(([f, s]) =>
        [...withoutComments(s).matchAll(/^(?:export\s+)?(?:async\s+)?(?:function|const|let)\s+(mock[A-Z]\w*|MOCK_\w+)/gm)].map(
          (m) => `${f}: ${m[1]}`,
        ),
      );
    expect(standIns.sort()).toEqual([]);
  });

  it("keeps outside lib/mock only modules the app reaches", () => {
    const unreached = Object.keys(sources).filter((f) => !f.startsWith(MOCK) && !appModules.has(f));
    expect(unreached.sort()).toEqual([]);
  });

  // What the mock and the tests alone use, outside lib/mock: mostly the kernel's rules the mock
  // ports (the diet score, the Home Score's fold, a repeating Task's dates, the timer's fold),
  // which move under lib/mock as their features move into lib/features. When you move one, lower
  // MOCK_ONLY; an export nothing uses at all is deleted, not counted.
  const MOCK_ONLY = 45;

  it(`keeps outside lib/mock what the app uses, ${MOCK_ONLY} exports the mock and tests alone use aside`, () => {
    const used = new Map<string, Set<string> | "*">();
    for (const file of appModules) {
      for (const edge of graph.get(file) ?? []) {
        if (edge.external || edge.target === file) continue;
        const prior = used.get(edge.target) ?? new Set<string>();
        if (prior === "*" || edge.names === "*") used.set(edge.target, "*");
        else used.set(edge.target, new Set([...prior, ...edge.names]));
      }
    }
    const unused: string[] = [];
    for (const file of appModules) {
      if (file === ENTRY || file.startsWith("/src/components/ui/")) continue;
      const taken = used.get(file);
      if (taken === "*") continue;
      const own = withoutComments(sources[file]);
      for (const m of own.matchAll(/^export\s+(?:default\s+)?(?:async\s+)?(?:function\*?|const|let|class)\s+(\w+)/gm)) {
        const name = m[1];
        if (taken?.has(name)) continue;
        // Used in its own module, beside being exported for a test.
        const mentions = own.match(new RegExp(`\\b${name}\\b`, "g"))?.length ?? 0;
        if (mentions > 1) continue;
        unused.push(`${file}: ${name}`);
      }
    }
    const elsewhere = Object.entries({ ...sources, ...tooling })
      .filter(([f]) => !f.endsWith(".config.ts"))
      .map(([f, s]) => [f, withoutComments(s)] as const);
    const nowhere = unused.filter((entry) => {
      const [file, name] = entry.split(": ");
      return !elsewhere.some(([f, s]) => f !== file && new RegExp(`\\b${name}\\b`).test(s));
    });
    expect(nowhere.sort(), "used by nothing at all: delete it").toEqual([]);
    expect(unused.length, unused.sort().join("\n")).toBe(MOCK_ONLY);
  });

  it("depends only on packages it uses", () => {
    const imported = new Set<string>();
    for (const edges of graph.values()) for (const e of edges) if (e.external) imported.add(e.target);
    const css = Object.values(styles).join("\n");
    const config = Object.entries(tooling)
      .filter(([f]) => f.endsWith(".config.ts"))
      .map(([, s]) => s)
      .join("\n");
    const mentioned = (pkg: string, text: string) => new RegExp(`["'@]${pkg.replace("/", "\\/")}(["'/])`).test(text);
    // A peer another package needs, which the app never imports itself.
    const peers: Record<string, string> = { "@tiptap/pm": "@tiptap/core's and @tiptap/react's peer" };
    const unusedRuntime = Object.keys(manifest.dependencies).filter(
      (pkg) => !imported.has(pkg) && !mentioned(pkg, css) && !mentioned(pkg, config) && !(pkg in peers),
    );
    expect(unusedRuntime).toEqual([]);

    // A tool runs from a script under its command's name, or is imported by a test or config.
    const commands: Record<string, string> = { typescript: "tsc", "@playwright/test": "playwright" };
    const scripts = Object.values(manifest.scripts).join("\n");
    const everything = Object.values(tooling).join("\n");
    const unusedDev = Object.keys(manifest.devDependencies).filter(
      (pkg) =>
        !pkg.startsWith("@types/") &&
        !new RegExp(`\\b${commands[pkg] ?? pkg}\\b`).test(scripts) &&
        !mentioned(pkg, everything) &&
        !everything.includes(`"${pkg}"`),
    );
    expect(unusedDev).toEqual([]);
  });
});

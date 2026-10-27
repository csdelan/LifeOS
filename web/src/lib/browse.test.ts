import { describe, expect, it } from "vitest";
import {
  DEFAULT_BROWSE_FILTERS,
  NO_AREA,
  activeFilterCount,
  browseColumns,
  browseOptions,
  browseRows,
  browseTypeCounts,
  effectiveFilters,
  itemsOfType,
  openTarget,
  parseBrowseType,
  searchSubjects,
  sortBrowse,
  statusOf,
  type BrowseFilters,
} from "@/lib/browse";
import type { SubjectListItem, SubjectType } from "@/lib/production-ui-types";

const TODAY = "2026-10-27";

function item(
  id: string,
  type: SubjectType,
  title: string,
  extra: Partial<SubjectListItem> = {},
): SubjectListItem {
  return {
    id,
    urn: `urn:bsk:${type.toLowerCase()}:${id}`,
    type,
    title,
    status: null,
    archived: false,
    createdAt: "2026-10-01T12:00:00.000Z",
    ...extra,
  };
}

const dev = { area: "urn:bsk:area:dev", areaName: "Dev Career" };
const health = { area: "urn:bsk:area:health", areaName: "Health" };

const decisions = [
  item("d1", "Decision", "Use SQLite for local storage", { status: "Open", ...dev, tags: "lifeos, storage" }),
  item("d2", "Decision", "React + Vite for Production UI", { status: "Implementing", ...dev, tags: "lifeos" }),
  item("d3", "Decision", "Run in the mornings", { status: "Closed", ...health }),
  item("d4", "Decision", "Blazor for Production UI", { status: "Cancelled", ...dev, archived: true }),
];
const tasks = [
  item("t1", "Task", "Buy race shoes", { status: "Not started", due: "2026-10-25", ...health }),
  item("t2", "Task", "Call Mom", { status: "Completed", due: TODAY }),
  item("t3", "Task", "Plan the week", { due: "2026-11-02", createdAt: "2026-10-26T09:00:00.000Z" }),
  item("t4", "Task", "Read a chapter"),
];
const people = [item("p1", "Person", "Maya")];
const items = [...decisions, ...tasks, ...people];

const filters = (f: Partial<BrowseFilters>): BrowseFilters => ({ ...DEFAULT_BROWSE_FILTERS, ...f });
const titles = (rows: SubjectListItem[]) => rows.map((r) => r.title);

describe("Browse types (BROWSE-2)", () => {
  it("counts each type, archived items only when included, every creatable type even at zero", () => {
    const live = browseTypeCounts(items, false);
    expect(live.total).toBe(8);
    const count = (t: SubjectType, c = live) => c.types.find((x) => x.type === t)?.count;
    expect(count("Decision")).toBe(3);
    expect(count("Task")).toBe(4);
    expect(count("Goal")).toBe(0);
    // A type New can't make shows only when there is one.
    expect(live.types.some((t) => t.type === "Season")).toBe(false);
    expect(live.types.map((t) => t.type).slice(0, 4)).toEqual(["Value", "Goal", "Project", "Task"]);

    const withArchived = browseTypeCounts(items, true);
    expect(withArchived.total).toBe(9);
    expect(count("Decision", withArchived)).toBe(4);

    const seasons = browseTypeCounts([item("s1", "Season", "Q4")], false);
    expect(seasons.types.find((t) => t.type === "Season")?.count).toBe(1);
  });

  it("reads a type from a link, and nothing it doesn't know", () => {
    expect(parseBrowseType("Decision")).toBe("Decision");
    expect(parseBrowseType("all")).toBe("all");
    expect(parseBrowseType("decision")).toBeUndefined();
    expect(parseBrowseType(3)).toBeUndefined();
  });

  it("gives a status-less item no status, and an unset one its type's first", () => {
    expect(statusOf(people[0])).toBe("");
    expect(statusOf(tasks[2])).toBe("Not started");
  });
});

describe("Browse filters (BROWSE-2)", () => {
  it("offers the type's statuses in its own order with counts, then the Areas and tags its items hold", () => {
    const typed = itemsOfType(items, "Decision", false);
    const options = browseOptions(typed, "Decision");
    expect(options.statuses).toEqual([
      { value: "Open", label: "Open", count: 1 },
      { value: "Implementing", label: "Implementing", count: 1 },
      { value: "Cancelled", label: "Cancelled", count: 0 },
      { value: "Closed", label: "Closed", count: 1 },
    ]);
    expect(options.areas).toEqual([
      { value: dev.area, label: "Dev Career", count: 2 },
      { value: health.area, label: "Health", count: 1 },
    ]);
    expect(options.tags).toEqual([
      { value: "lifeos", label: "lifeos", count: 2 },
      { value: "storage", label: "storage", count: 1 },
    ]);
  });

  it("offers No area only beside real Areas, and no Area filter when none has one", () => {
    const taskAreas = browseOptions(itemsOfType(items, "Task", false), "Task").areas;
    expect(taskAreas.map((a) => a.value)).toEqual([health.area, NO_AREA]);
    expect(taskAreas.at(-1)).toEqual({ value: NO_AREA, label: "No area", count: 3 });
    expect(browseOptions(people, "Person").areas).toEqual([]);
  });

  it("ORs the values picked in one category and ANDs the categories", () => {
    const either = browseRows(items, "Decision", filters({ statuses: ["Open", "Closed"] }), TODAY);
    expect(titles(either)).toEqual(["Run in the mornings", "Use SQLite for local storage"]);

    const andArea = browseRows(
      items,
      "Decision",
      filters({ statuses: ["Open", "Closed"], areas: [dev.area] }),
      TODAY,
    );
    expect(titles(andArea)).toEqual(["Use SQLite for local storage"]);

    const anyTag = browseRows(items, "Decision", filters({ tags: ["storage", "lifeos"] }), TODAY);
    expect(titles(anyTag)).toEqual(["React + Vite for Production UI", "Use SQLite for local storage"]);
  });

  it("matches every word typed in the title or URN, in any order", () => {
    expect(titles(browseRows(items, "all", filters({ text: "storage sqlite" }), TODAY))).toEqual([
      "Use SQLite for local storage",
    ]);
    expect(titles(browseRows(items, "all", filters({ text: "urn:bsk:task:t4" }), TODAY))).toEqual([
      "Read a chapter",
    ]);
  });

  it("filters by due date and by when an item was entered", () => {
    const due = (d: BrowseFilters["due"]) => titles(browseRows(items, "Task", filters({ due: d }), TODAY));
    expect(due("overdue")).toEqual(["Buy race shoes"]);
    expect(due("today")).toEqual(["Call Mom"]);
    expect(due("upcoming")).toEqual(["Plan the week"]);
    expect(due("none")).toEqual(["Read a chapter"]);

    expect(titles(browseRows(items, "Task", filters({ entered: "7" }), TODAY))).toEqual(["Plan the week"]);
    expect(browseRows(items, "Task", filters({ entered: "30" }), TODAY)).toHaveLength(4);
  });

  it("includes archived items only when asked", () => {
    expect(browseRows(items, "Decision", filters({}), TODAY)).toHaveLength(3);
    const all = browseRows(items, "Decision", filters({ archived: true }), TODAY);
    expect(titles(all)).toContain("Blazor for Production UI");
  });

  it("ignores a picked value the type doesn't offer, rather than emptying the list", () => {
    // "Open" left picked from Decisions while looking at Tasks.
    const rows = browseRows(items, "Task", filters({ statuses: ["Open"] }), TODAY);
    expect(rows).toHaveLength(4);
    const typed = itemsOfType(items, "Task", false);
    expect(effectiveFilters(filters({ statuses: ["Open", "Completed"] }), browseOptions(typed, "Task")).statuses).toEqual([
      "Completed",
    ]);
  });

  it("counts what narrows the list, not the sort or Include archived", () => {
    expect(activeFilterCount(filters({ archived: true, sort: "due", dir: "desc" }))).toBe(0);
    expect(activeFilterCount(filters({ text: " x ", statuses: ["Open"], due: "today", entered: "7" }))).toBe(4);
  });
});

describe("Browse sorting (BROWSE-2)", () => {
  it("sorts titles naturally, either way", () => {
    const rows = [item("a", "Task", "Step 10"), item("b", "Task", "step 2"), item("c", "Task", "Step 1")];
    expect(titles(sortBrowse(rows, "title", "asc"))).toEqual(["Step 1", "step 2", "Step 10"]);
    expect(titles(sortBrowse(rows, "title", "desc"))).toEqual(["Step 10", "step 2", "Step 1"]);
  });

  it("keeps items without a due date or Area last whichever way the sort runs", () => {
    expect(titles(sortBrowse(tasks, "due", "asc"))).toEqual([
      "Buy race shoes",
      "Call Mom",
      "Plan the week",
      "Read a chapter",
    ]);
    expect(titles(sortBrowse(tasks, "due", "desc"))).toEqual([
      "Plan the week",
      "Call Mom",
      "Buy race shoes",
      "Read a chapter",
    ]);
    expect(titles(sortBrowse(tasks, "area", "desc")).at(-1)).not.toBe("Buy race shoes");
  });

  it("sorts statuses in the type's own order, status-less items last", () => {
    const mixed = [...decisions.slice(0, 3), people[0]];
    expect(titles(sortBrowse(mixed, "status", "asc"))).toEqual([
      "Use SQLite for local storage",
      "React + Vite for Production UI",
      "Run in the mornings",
      "Maya",
    ]);
    expect(titles(sortBrowse(mixed, "status", "desc"))).toEqual([
      "Run in the mornings",
      "React + Vite for Production UI",
      "Use SQLite for local storage",
      "Maya",
    ]);
  });

  it("sorts by when an item was entered", () => {
    expect(titles(sortBrowse(tasks, "entered", "desc"))[0]).toBe("Plan the week");
  });
});

describe("Browse columns (BROWSE-2: context-sensitive)", () => {
  it("shows a type's fields that apply: no Due for Decisions, none of Status for People", () => {
    expect(browseColumns("Decision", itemsOfType(items, "Decision", false))).toEqual([
      "status",
      "area",
      "tags",
      "entered",
    ]);
    expect(browseColumns("Task", itemsOfType(items, "Task", false))).toEqual([
      "status",
      "due",
      "area",
      "entered",
    ]);
    expect(browseColumns("Person", people)).toEqual(["entered"]);
  });

  it("adds Type across all types", () => {
    expect(browseColumns("all", items)[0]).toBe("type");
  });
});

describe("Search box: subjects", () => {
  // More than the 20 the search box used to look through.
  const many = Array.from({ length: 40 }, (_, i) =>
    item(`x${i}`, "Task", `Errand ${String(i).padStart(2, "0")}`),
  );

  it("finds an item past the first twenty", () => {
    const found = searchSubjects([...many, ...decisions], "blazor");
    expect(titles(found.matches)).toEqual(["Blazor for Production UI"]);
  });

  it("matches each word in the title, type, URN, Area or tags, in any order", () => {
    expect(titles(searchSubjects(items, "storage use").matches)).toEqual(["Use SQLite for local storage"]);
    expect(titles(searchSubjects(items, "decision health").matches)).toEqual(["Run in the mornings"]);
    expect(titles(searchSubjects(items, "lifeos").matches)).toHaveLength(2);
    expect(titles(searchSubjects(items, "urn:bsk:person").matches)).toEqual(["Maya"]);
    expect(titles(searchSubjects(items, "identity").matches)).toEqual([]);
  });

  it("puts titles that start with the search first, then a title word, then elsewhere, archived last", () => {
    const rows = [
      item("a", "Idea", "Shared shopping list"),
      item("b", "Task", "Plan a shop run"),
      item("c", "Task", "Shop for shoes", { archived: true }),
      item("d", "Task", "Shop for groceries"),
      item("e", "Task", "Workshop notes"),
      item("f", "Task", "Errands", { tags: "shop" }),
    ];
    expect(titles(searchSubjects(rows, "shop").matches)).toEqual([
      "Shop for groceries",
      "Plan a shop run",
      "Shared shopping list",
      "Workshop notes",
      "Errands",
      "Shop for shoes",
    ]);
  });

  it("returns the best few with how many matched, and nothing before anything is typed", () => {
    const found = searchSubjects(many, "errand", 10);
    expect(found.matches).toHaveLength(10);
    expect(found.total).toBe(40);
    expect(found.matches[0].title).toBe("Errand 00");
    expect(searchSubjects(many, "   ")).toEqual({ matches: [], total: 0 });
  });

  it("opens the alignment chain on the Map and everything else in Browse", () => {
    const goal = item("g", "Goal", "Ship it");
    expect(openTarget(goal)).toEqual({ to: "/map", search: { selected: "g" } });
    expect(openTarget({ ...goal, archived: true })).toEqual({
      to: "/map",
      search: { selected: "g", archived: true },
    });
    expect(openTarget(decisions[0])).toEqual({
      to: "/browse",
      search: { type: "Decision", selected: "d1" },
    });
  });
});

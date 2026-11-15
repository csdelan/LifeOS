/**
 * The mock's Browse (BROWSE-2): what `GET /api/browse` answers in the live app (`BrowseRules` in
 * `src/LifeOs.Api/Read/BrowseReader.cs`, code review CR120), worked out here over the mock's
 * subjects; `tests/rules/browse.json` holds the two to the same pages. The search box's match is
 * the mock's copy of `GET /api/subjects/search`.
 */

import {
  BROWSE_TYPE_ORDER,
  NO_AREA,
  effectiveFilters,
  matchesBrowse,
  statusOf,
  tagsOf,
  type BrowseColumn,
  type BrowseOptions,
  type BrowseSort,
  type BrowseType,
  type SortDir,
  type SubjectSearch,
  type TypeCount,
} from "@/lib/browse";
import type { BrowseQuery, BrowseRead } from "@/lib/features/browse/types";
import { STATUS_BY_TYPE, typeLabel, type SubjectListItem, type SubjectType } from "@/lib/production-ui-types";

/**
 * The items Browse and the search box show: those of a type Browse lists. The kernel's list
 * also holds types the web doesn't model yet, such as the Environment page's Spaces.
 */
export function browsableItems(items: SubjectListItem[]): SubjectListItem[] {
  return items.filter((i) => BROWSE_TYPE_ORDER.includes(i.type));
}

/** How many items each type Browse lists holds, archived ones only when they're included; only types that hold one. */
export function browseTypeCounts(items: SubjectListItem[], includeArchived: boolean): TypeCount[] {
  const counts = new Map<SubjectType, number>();
  for (const item of browsableItems(items)) {
    if (item.archived && !includeArchived) continue;
    counts.set(item.type, (counts.get(item.type) ?? 0) + 1);
  }
  return BROWSE_TYPE_ORDER.filter((t) => (counts.get(t) ?? 0) > 0).map((type) => ({ type, count: counts.get(type)! }));
}

/** The items of one type (or every type Browse lists), archived ones only when included: what the other filters narrow. */
export function itemsOfType(
  items: SubjectListItem[],
  type: BrowseType,
  includeArchived: boolean,
): SubjectListItem[] {
  return browsableItems(items).filter(
    (i) => (type === "all" || i.type === type) && (includeArchived || !i.archived),
  );
}

/**
 * What the filters offer for the items of a type: its status vocabulary (plus any other status
 * an item holds), and the Areas and tags its items carry, each with how many items have it.
 */
export function browseOptions(typed: SubjectListItem[], type: BrowseType): BrowseOptions {
  const statusCounts = new Map<string, number>();
  const areaCounts = new Map<string, { label: string; count: number }>();
  const tagCounts = new Map<string, number>();
  for (const item of typed) {
    const status = statusOf(item);
    if (status) statusCounts.set(status, (statusCounts.get(status) ?? 0) + 1);
    const areaKey = item.area || NO_AREA;
    const area = areaCounts.get(areaKey) ?? {
      label: item.area ? item.areaName || item.area : "No area",
      count: 0,
    };
    area.count += 1;
    areaCounts.set(areaKey, area);
    for (const tag of tagsOf(item)) tagCounts.set(tag, (tagCounts.get(tag) ?? 0) + 1);
  }

  const vocabulary = type === "all" ? [] : (STATUS_BY_TYPE[type] ?? []);
  const statusValues = [
    ...vocabulary,
    ...[...statusCounts.keys()]
      .filter((s) => !vocabulary.includes(s))
      .sort((a, b) => a.localeCompare(b)),
  ];
  const statuses = statusValues.map((value) => ({
    value,
    label: value,
    count: statusCounts.get(value) ?? 0,
  }));

  // Only worth offering when it can tell items apart: every item without an Area offers nothing.
  const areas =
    areaCounts.size === 1 && areaCounts.has(NO_AREA)
      ? []
      : [...areaCounts.entries()]
          .map(([value, a]) => ({ value, label: a.label, count: a.count }))
          .sort((a, b) =>
            a.value === NO_AREA ? 1 : b.value === NO_AREA ? -1 : a.label.localeCompare(b.label),
          );

  const tags = [...tagCounts.entries()]
    .map(([value, count]) => ({ value, label: value, count }))
    .sort((a, b) => a.label.localeCompare(b.label));

  return { statuses, areas, tags };
}

const collator = new Intl.Collator(undefined, { sensitivity: "base", numeric: true });

/** An item's tags as its Tags column reads them, "" for none. */
function tagsText(item: SubjectListItem): string {
  return tagsOf(item).join(" ");
}

/** Where an item's type comes in Browse's type list: the alignment chain first. */
function typeRank(item: SubjectListItem): number {
  const i = BROWSE_TYPE_ORDER.indexOf(item.type);
  return i === -1 ? BROWSE_TYPE_ORDER.length : i;
}

function statusRank(item: SubjectListItem): number {
  const vocabulary = STATUS_BY_TYPE[item.type] ?? [];
  const i = vocabulary.indexOf(statusOf(item));
  return i === -1 ? vocabulary.length : i;
}

/**
 * Sorted by the chosen field, ties by title. Items without the field (no due date, no Area, no
 * tags) go last whichever way the sort runs. Type runs in the type list's order (Identity
 * Statement, Goal, Project, Task…), as Status runs in the type's own order; Tags by the tags
 * as the column shows them.
 */
export function sortBrowse(rows: SubjectListItem[], sort: BrowseSort, dir: SortDir): SubjectListItem[] {
  const sign = dir === "asc" ? 1 : -1;
  const byTitle = (a: SubjectListItem, b: SubjectListItem) => collator.compare(a.title, b.title);
  const missingLast = (a: string | null | undefined, b: string | null | undefined) => {
    if (!a && !b) return 0;
    if (!a) return 1;
    if (!b) return -1;
    return sign * collator.compare(a, b);
  };
  return [...rows].sort((a, b) => {
    let c = 0;
    switch (sort) {
      case "title":
        return sign * byTitle(a, b);
      case "type":
        c = sign * (typeRank(a) - typeRank(b));
        break;
      case "status": {
        // In the type's own order (Open before Closed), status-less items last.
        const sa = statusOf(a);
        const sb = statusOf(b);
        if (!sa || !sb) c = !sa && !sb ? 0 : !sa ? 1 : -1;
        else c = sign * (statusRank(a) - statusRank(b) || collator.compare(sa, sb));
        break;
      }
      case "due":
        c = missingLast(a.due, b.due);
        break;
      case "entered":
        c = sign * (Date.parse(a.createdAt) - Date.parse(b.createdAt));
        break;
      case "area":
        c = missingLast(a.areaName, b.areaName);
        break;
      case "tags":
        c = missingLast(tagsText(a), tagsText(b));
        break;
    }
    return c || byTitle(a, b);
  });
}

/**
 * The list's columns for what's being looked at (BROWSE-2: context-sensitive). Title always;
 * Type only across all types; Status when the type has statuses; Due, Area and Tags when any
 * of the type's items carries one; Entered always. Read from the type's items before the other
 * filters, so the columns don't jump as filters change.
 */
export function browseColumns(type: BrowseType, typed: SubjectListItem[]): BrowseColumn[] {
  const columns: BrowseColumn[] = [];
  if (type === "all") columns.push("type");
  const hasStatus =
    type === "all" ? typed.some((i) => statusOf(i)) : (STATUS_BY_TYPE[type]?.length ?? 0) > 0;
  if (hasStatus) columns.push("status");
  if (typed.some((i) => i.due)) columns.push("due");
  if (typed.some((i) => i.area)) columns.push("area");
  if (typed.some((i) => tagsOf(i).length)) columns.push("tags");
  columns.push("entered");
  return columns;
}

/** The shown rows: the type's items through the filters, sorted. */
export function browseRows(
  items: SubjectListItem[],
  type: BrowseType,
  filters: Omit<BrowseQuery, "type" | "limit">,
  today: string,
): SubjectListItem[] {
  const typed = itemsOfType(items, type, filters.archived);
  const effective = effectiveFilters(filters, browseOptions(typed, type));
  return sortBrowse(
    typed.filter((i) => matchesBrowse(i, effective, today)),
    filters.sort,
    filters.dir,
  );
}

/** A page of Browse, as `GET /api/browse` answers it, over `items` on `today`. */
export function browsePage(items: SubjectListItem[], query: BrowseQuery, today: string): BrowseRead {
  const typed = itemsOfType(items, query.type, query.archived);
  const matching = browseRows(items, query.type, query, today);
  return {
    types: browseTypeCounts(items, query.archived),
    total: typed.length,
    matching: matching.length,
    options: browseOptions(typed, query.type),
    columns: browseColumns(query.type, typed),
    rows: matching.slice(0, query.limit),
  };
}

function words(text: string): string[] {
  return text.toLowerCase().split(/\s+/).filter(Boolean);
}

/**
 * The search box's subjects: every item whose title, type, URN, Area or tags hold each word
 * typed, in any order. Best first: the title equal to what's typed, then starting with it,
 * then a title word starting with it, then the title containing it, then a match elsewhere;
 * archived items after the rest; ties by title. Nothing until something is typed.
 */
export function searchSubjects(items: SubjectListItem[], query: string, limit = 50): SubjectSearch {
  const q = query.trim().toLowerCase();
  const qWords = words(q);
  if (!qWords.length) return { matches: [], total: 0 };

  const scored: { item: SubjectListItem; rank: number }[] = [];
  for (const item of browsableItems(items)) {
    const title = item.title.toLowerCase();
    const hay = [title, typeLabel(item.type), item.type, item.urn, item.areaName ?? "", ...tagsOf(item)]
      .join(" ")
      .toLowerCase();
    if (!qWords.every((w) => hay.includes(w))) continue;
    const rank =
      title === q
        ? 0
        : title.startsWith(q)
          ? 1
          : title.split(/[\s\-–—/:,.()]+/).some((w) => w.startsWith(qWords[0]))
            ? 2
            : title.includes(q)
              ? 3
              : 4;
    scored.push({ item, rank: rank + (item.archived ? 10 : 0) });
  }
  scored.sort((a, b) => a.rank - b.rank || collator.compare(a.item.title, b.item.title));
  return { matches: scored.slice(0, limit).map((s) => s.item), total: scored.length };
}

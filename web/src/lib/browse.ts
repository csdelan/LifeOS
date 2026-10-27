/**
 * Browse (BROWSE-2): every subject, narrowed by type and filters, in a context-sensitive list.
 *
 * Type is one filter among the others. Categories combine with AND; the values picked inside
 * one category (two Areas, say) combine with OR. A picked value the current type doesn't
 * offer (a Decision status left picked after switching to Tasks) is ignored, not applied.
 */

import { addDays } from "@/lib/dates";
import { ALIGNMENT_TYPES } from "@/lib/subject-meta";
import {
  CREATABLE_TYPES,
  STATUS_BY_TYPE,
  defaultStatus,
  typeLabel,
  type SubjectListItem,
  type SubjectType,
} from "@/lib/production-ui-types";

export type BrowseType = SubjectType | "all";
export type DueFilter = "any" | "overdue" | "today" | "upcoming" | "none";
/** Entered within the last N days, counting today. */
export type EnteredFilter = "any" | "7" | "30" | "90" | "365";
export type BrowseSort = "title" | "status" | "due" | "entered" | "area";
export type SortDir = "asc" | "desc";
export type BrowseColumn = "type" | "status" | "due" | "area" | "tags" | "entered";

export interface BrowseFilters {
  /** Words that must all appear in the title or URN. */
  text: string;
  statuses: string[];
  /** Area URNs; `NO_AREA` picks items without one. */
  areas: string[];
  tags: string[];
  due: DueFilter;
  entered: EnteredFilter;
  /** Include archived items alongside the rest (as the Pilot's Archived box does). */
  archived: boolean;
  sort: BrowseSort;
  dir: SortDir;
}

export const NO_AREA = "__none__";

export const DEFAULT_BROWSE_FILTERS: BrowseFilters = {
  text: "",
  statuses: [],
  areas: [],
  tags: [],
  due: "any",
  entered: "any",
  archived: false,
  sort: "title",
  dir: "asc",
};

/** Every subject type, in the order Browse lists them: the alignment chain first. */
export const BROWSE_TYPE_ORDER: readonly SubjectType[] = [
  "Value",
  "Goal",
  "Project",
  "Task",
  "Habit",
  "Commitment",
  "Decision",
  "Problem",
  "Idea",
  "Appointment",
  "Person",
  "Area",
  "Constraint",
  "Season",
  "Reward",
];

export function parseBrowseType(value: unknown): BrowseType | undefined {
  if (value === "all") return "all";
  return BROWSE_TYPE_ORDER.find((t) => t === value);
}

export const DUE_OPTIONS: { value: DueFilter; label: string }[] = [
  { value: "any", label: "Any due date" },
  { value: "overdue", label: "Overdue" },
  { value: "today", label: "Due today" },
  { value: "upcoming", label: "Due later" },
  { value: "none", label: "No due date" },
];

export const ENTERED_OPTIONS: { value: EnteredFilter; label: string }[] = [
  { value: "any", label: "Entered any time" },
  { value: "7", label: "Last 7 days" },
  { value: "30", label: "Last 30 days" },
  { value: "90", label: "Last 90 days" },
  { value: "365", label: "Last year" },
];

export const SORT_OPTIONS: { value: BrowseSort; label: string }[] = [
  { value: "title", label: "Title" },
  { value: "status", label: "Status" },
  { value: "due", label: "Due date" },
  { value: "entered", label: "Entered" },
  { value: "area", label: "Area" },
];

/** The shown status: the folded one, else the type's default. Empty for a status-less type. */
export function statusOf(item: SubjectListItem): string {
  return item.status || defaultStatus(item.type);
}

export function tagsOf(item: SubjectListItem): string[] {
  return (item.tags ?? "")
    .split(",")
    .map((t) => t.trim())
    .filter(Boolean);
}

/** The local calendar day an ISO timestamp falls on. */
export function localDay(timestamp: string): string {
  const d = new Date(timestamp);
  if (Number.isNaN(d.getTime())) return "";
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${d.getFullYear()}-${m}-${day}`;
}

export interface TypeCount {
  type: SubjectType;
  count: number;
}

/**
 * How many items each type holds, archived ones only when they're included. Every type New
 * can make is listed, even at zero, so it can be picked and created; the rest only when held.
 */
export function browseTypeCounts(
  items: SubjectListItem[],
  includeArchived: boolean,
): { total: number; types: TypeCount[] } {
  const counts = new Map<SubjectType, number>();
  let total = 0;
  for (const item of items) {
    if (item.archived && !includeArchived) continue;
    counts.set(item.type, (counts.get(item.type) ?? 0) + 1);
    total += 1;
  }
  const types = BROWSE_TYPE_ORDER.filter(
    (t) => CREATABLE_TYPES.includes(t) || (counts.get(t) ?? 0) > 0,
  ).map((type) => ({ type, count: counts.get(type) ?? 0 }));
  return { total, types };
}

/** The items of one type (or all), archived ones only when included: what the other filters narrow. */
export function itemsOfType(
  items: SubjectListItem[],
  type: BrowseType,
  includeArchived: boolean,
): SubjectListItem[] {
  return items.filter(
    (i) => (type === "all" || i.type === type) && (includeArchived || !i.archived),
  );
}

export interface FilterOption {
  value: string;
  label: string;
  count: number;
}

export interface BrowseOptions {
  statuses: FilterOption[];
  areas: FilterOption[];
  tags: FilterOption[];
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

/** The filters as they apply here: picked values the options don't offer are dropped. */
export function effectiveFilters(filters: BrowseFilters, options: BrowseOptions): BrowseFilters {
  const keep = (picked: string[], offered: FilterOption[]) =>
    picked.filter((p) => offered.some((o) => o.value === p));
  return {
    ...filters,
    statuses: keep(filters.statuses, options.statuses),
    areas: keep(filters.areas, options.areas),
    tags: keep(filters.tags, options.tags),
  };
}

/** How many filters narrow the list (sorting and Include archived don't count). */
export function activeFilterCount(filters: BrowseFilters): number {
  return (
    (filters.text.trim() ? 1 : 0) +
    (filters.statuses.length ? 1 : 0) +
    (filters.areas.length ? 1 : 0) +
    (filters.tags.length ? 1 : 0) +
    (filters.due !== "any" ? 1 : 0) +
    (filters.entered !== "any" ? 1 : 0)
  );
}

function words(text: string): string[] {
  return text.toLowerCase().split(/\s+/).filter(Boolean);
}

/** Whether an item passes every category (AND), any picked value within one (OR). */
export function matchesBrowse(item: SubjectListItem, filters: BrowseFilters, today: string): boolean {
  const query = words(filters.text);
  if (query.length) {
    const hay = `${item.title} ${item.urn}`.toLowerCase();
    if (!query.every((w) => hay.includes(w))) return false;
  }
  if (filters.statuses.length && !filters.statuses.includes(statusOf(item))) return false;
  if (filters.areas.length && !filters.areas.includes(item.area || NO_AREA)) return false;
  if (filters.tags.length) {
    const tags = tagsOf(item);
    if (!filters.tags.some((t) => tags.includes(t))) return false;
  }
  const due = item.due ?? null;
  if (filters.due === "overdue" && !(due && due < today)) return false;
  if (filters.due === "today" && due !== today) return false;
  if (filters.due === "upcoming" && !(due && due > today)) return false;
  if (filters.due === "none" && due) return false;
  if (filters.entered !== "any") {
    const since = addDays(today, -(Number(filters.entered) - 1));
    if (localDay(item.createdAt) < since) return false;
  }
  return true;
}

const collator = new Intl.Collator(undefined, { sensitivity: "base", numeric: true });

function statusRank(item: SubjectListItem): number {
  const vocabulary = STATUS_BY_TYPE[item.type] ?? [];
  const i = vocabulary.indexOf(statusOf(item));
  return i === -1 ? vocabulary.length : i;
}

/**
 * Sorted by the chosen field, ties by title. Items without the field (no due date, no Area)
 * go last whichever way the sort runs.
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
  filters: BrowseFilters,
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

export interface SubjectSearch {
  matches: SubjectListItem[];
  /** Every match, of which `matches` holds the best `limit`. */
  total: number;
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
  for (const item of items) {
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

/**
 * Where opening an item from the search box goes: Identity Statements, Goals, Projects and
 * Tasks to the Map (as before), archived ones with the Map showing archived; everything else
 * to Browse, on its type.
 */
export type OpenTarget =
  | { to: "/map"; search: { selected: string; archived?: boolean } }
  | { to: "/browse"; search: { type: SubjectType; selected: string } };

export function openTarget(item: SubjectListItem): OpenTarget {
  if (ALIGNMENT_TYPES.includes(item.type)) {
    return {
      to: "/map",
      search: item.archived ? { selected: item.id, archived: true } : { selected: item.id },
    };
  }
  return { to: "/browse", search: { type: item.type, selected: item.id } };
}

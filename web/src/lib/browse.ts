/**
 * Browse (BROWSE-2): every subject, narrowed by type and filters, in a context-sensitive list.
 *
 * Type is one filter among the others. Categories combine with AND; the values picked inside
 * one category (two Areas, say) combine with OR. A picked value the current type doesn't
 * offer (a Decision status left picked after switching to Tasks) is ignored, not applied.
 *
 * The server counts, filters, sorts and pages the list (`GET /api/browse`, code review CR120);
 * the mock's copy of that is `lib/mock/browse.ts`. What's here is the page's own: the filters'
 * vocabulary, and whether the open item passes them.
 */

import { addDays, homeDayAt, isOverdue } from "@/lib/dates";
import { ALIGNMENT_TYPES } from "@/lib/subject-meta";
import {
  CREATABLE_TYPES,
  defaultStatus,
  isTerminal,
  type SubjectListItem,
  type SubjectType,
} from "@/lib/production-ui-types";

export type BrowseType = SubjectType | "all";
export type DueFilter = "any" | "overdue" | "today" | "upcoming" | "none";
/** Entered within the last N days, counting today. */
export type EnteredFilter = "any" | "7" | "30" | "90" | "365";
export type BrowseSort = "title" | "type" | "status" | "due" | "entered" | "area" | "tags";
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
  { value: "type", label: "Type" },
  { value: "status", label: "Status" },
  { value: "due", label: "Due date" },
  { value: "entered", label: "Entered" },
  { value: "area", label: "Area" },
  { value: "tags", label: "Tags" },
];

/** The column each sort belongs to; Title's is always there. */
export const SORT_COLUMN: Record<Exclude<BrowseSort, "title">, BrowseColumn> = {
  type: "type",
  status: "status",
  due: "due",
  entered: "entered",
  area: "area",
  tags: "tags",
};

/** Clears what narrows the list; the sort and Include archived stay as they are. */
export function clearedFilters(f: BrowseFilters): BrowseFilters {
  return { ...DEFAULT_BROWSE_FILTERS, sort: f.sort, dir: f.dir, archived: f.archived };
}

/** A new sort runs ascending, except Entered, which starts newest first. */
export function nextSort(f: BrowseFilters, sort: BrowseSort): BrowseFilters {
  if (f.sort === sort) return { ...f, dir: f.dir === "asc" ? "desc" : "asc" };
  return { ...f, sort, dir: sort === "entered" ? "desc" : "asc" };
}

/**
 * The sorts worth offering where the list is too narrow for its headers: Title and the
 * columns shown, plus the sort in use so the menu never reads blank.
 */
export function sortOptionsFor(columns: BrowseColumn[], current: BrowseSort): { value: BrowseSort; label: string }[] {
  return SORT_OPTIONS.filter(
    (o) => o.value === "title" || o.value === current || columns.includes(SORT_COLUMN[o.value]),
  );
}

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

/** The home-zone day an ISO timestamp falls on, as "today" is (`todayIso`). */
export function localDay(timestamp: string): string {
  const d = new Date(timestamp);
  if (Number.isNaN(d.getTime())) return "";
  return homeDayAt(d);
}

export interface TypeCount {
  type: SubjectType;
  count: number;
}

/**
 * The type list from the types that hold an item (the server's counts): every type New can
 * make is listed, even at zero, so it can be picked and created; the rest only when held.
 */
export function browseTypeList(held: TypeCount[]): { total: number; types: TypeCount[] } {
  const counts = new Map(held.map((t) => [t.type, t.count]));
  const types = BROWSE_TYPE_ORDER.filter(
    (t) => CREATABLE_TYPES.includes(t) || (counts.get(t) ?? 0) > 0,
  ).map((type) => ({ type, count: counts.get(type) ?? 0 }));
  return { total: held.reduce((sum, t) => sum + t.count, 0), types };
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

/** The filters as they apply here: picked values the options don't offer are dropped. */
export function effectiveFilters<F extends BrowseFilters>(filters: F, options: BrowseOptions): F {
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

/**
 * Whether an item passes every category (AND), any picked value within one (OR): the page asks it
 * of the open item, to say when the filters hide it, and the mock of every item.
 */
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
  if (filters.due === "overdue" && !isOverdue(due, isTerminal(statusOf(item)), today)) return false;
  if (filters.due === "today" && due !== today) return false;
  if (filters.due === "upcoming" && !(due && due > today)) return false;
  if (filters.due === "none" && due) return false;
  if (filters.entered !== "any") {
    const since = addDays(today, -(Number(filters.entered) - 1));
    if (localDay(item.createdAt) < since) return false;
  }
  return true;
}

export interface SubjectSearch {
  matches: SubjectListItem[];
  /** Every match, of which `matches` holds the best `limit`. */
  total: number;
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

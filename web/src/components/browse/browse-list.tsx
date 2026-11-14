import type { CSSProperties } from "react";
import { ArrowDownIcon, ArrowUpIcon } from "lucide-react";
import { DateChip } from "@/components/primitives/date-chip";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeBadge } from "@/components/primitives/type-badge";
import { ShowMore } from "@/components/primitives/show-more";
import { usePaged } from "@/components/primitives/use-paged";
import {
  localDay,
  nextSort,
  statusOf,
  tagsOf,
  type BrowseColumn,
  type BrowseFilters,
  type BrowseSort,
} from "@/lib/browse";
import { formatDay } from "@/lib/dates";
import { isTerminal, type SubjectListItem } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

const COLUMN_WIDTH: Record<BrowseColumn, string> = {
  type: "8.5rem",
  status: "7.5rem",
  due: "6.5rem",
  area: "8rem",
  tags: "minmax(0,9rem)",
  entered: "6rem",
};

const COLUMN_LABEL: Record<BrowseColumn, string> = {
  type: "Type",
  status: "Status",
  due: "Due",
  area: "Area",
  tags: "Tags",
  entered: "Entered",
};

/** Every column sorts from its header (Chris: "Other columns like status are sortable, so why not tags?"). */
const COLUMN_SORT: Record<BrowseColumn, BrowseSort> = {
  type: "type",
  status: "status",
  due: "due",
  area: "area",
  tags: "tags",
  entered: "entered",
};

/**
 * The list: a grid with sortable column headers when there's room, stacked rows when there
 * isn't (the list's own width decides, so an open detail panel narrows it too). It draws a page
 * of rows at a time and the open item's (`usePaged`), so thousands of finished repetitions don't
 * all reach the page at once (code review CR76).
 */
export function BrowseList({
  pagedFor,
  label,
  rows,
  columns,
  filters,
  selected,
  onSelect,
  onSort,
}: {
  pagedFor: string;
  label: string;
  rows: SubjectListItem[];
  columns: BrowseColumn[];
  filters: BrowseFilters;
  selected?: string;
  onSelect: (id: string) => void;
  onSort: (update: (f: BrowseFilters) => BrowseFilters) => void;
}) {
  const template = ["minmax(0,1fr)", ...columns.map((c) => COLUMN_WIDTH[c])].join(" ");
  const paged = usePaged(rows, pagedFor, selected);
  return (
    <div style={{ "--browse-cols": template } as CSSProperties}>
      <div className="hidden items-center gap-x-3 px-3 pb-1 @xl:grid @xl:grid-cols-(--browse-cols)">
        <SortHeader label="Title" sort="title" filters={filters} onSort={onSort} />
        {columns.map((c) => {
          const sort = COLUMN_SORT[c];
          return sort ? (
            <SortHeader key={c} label={COLUMN_LABEL[c]} sort={sort} filters={filters} onSort={onSort} />
          ) : (
            <span key={c} className="text-[0.6875rem] font-medium text-muted-foreground">
              {COLUMN_LABEL[c]}
            </span>
          );
        })}
      </div>
      <ul aria-label={`${label} list`} className="space-y-0.5">
        {rows.slice(0, paged.drawn).map((item) => (
          <li key={item.id}>
            <BrowseRow
              item={item}
              columns={columns}
              showEntered={filters.sort === "entered"}
              selected={selected === item.id}
              onSelect={() => onSelect(item.id)}
            />
          </li>
        ))}
      </ul>
      <ShowMore {...paged} />
    </div>
  );
}

function SortHeader({
  label,
  sort,
  filters,
  onSort,
}: {
  label: string;
  sort: BrowseSort;
  filters: BrowseFilters;
  onSort: (update: (f: BrowseFilters) => BrowseFilters) => void;
}) {
  const active = filters.sort === sort;
  const Arrow = filters.dir === "asc" ? ArrowUpIcon : ArrowDownIcon;
  return (
    <button
      type="button"
      onClick={() => onSort((f) => nextSort(f, sort))}
      aria-label={
        active
          ? `${label}, sorted ${filters.dir === "asc" ? "ascending" : "descending"}`
          : `Sort by ${label}`
      }
      className={cn(
        "inline-flex min-h-6 items-center gap-1 justify-self-start rounded text-[0.6875rem] font-medium focus-visible:ring-2 focus-visible:ring-ring/50",
        active ? "text-foreground" : "text-muted-foreground hover:text-foreground",
      )}
    >
      {label}
      {active ? <Arrow className="size-3" aria-hidden /> : null}
    </button>
  );
}

function BrowseRow({
  item,
  columns,
  showEntered,
  selected,
  onSelect,
}: {
  item: SubjectListItem;
  columns: BrowseColumn[];
  /** Show Entered in a narrow list too: it's what the list is sorted by. */
  showEntered: boolean;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-current={selected ? "true" : undefined}
      className={cn(
        "flex min-h-11 w-full flex-wrap items-center gap-x-3 gap-y-1 rounded-lg px-3 py-2 text-left transition-colors ease-hearth @xl:grid @xl:grid-cols-(--browse-cols)",
        "hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring/40",
        selected && "bg-primary/8 ring-1 ring-primary/20",
      )}
    >
      <span className="flex min-w-0 basis-full items-center gap-2 @xl:basis-auto">
        <span className="truncate text-sm font-medium">{item.title}</span>
        {item.archived ? (
          <span className="shrink-0 text-[0.6875rem] text-muted-foreground">Archived</span>
        ) : null}
      </span>
      {columns.map((c) => (
        <Cell key={c} column={c} item={item} showEntered={showEntered} />
      ))}
    </button>
  );
}

/** One field of a row; an empty one keeps its column in the grid and takes no room when stacked. */
function Cell({
  column,
  item,
  showEntered,
}: {
  column: BrowseColumn;
  item: SubjectListItem;
  showEntered: boolean;
}) {
  const empty = <span className="hidden @xl:block" aria-hidden />;
  const quiet = "min-w-0 truncate text-[0.6875rem] text-muted-foreground";
  switch (column) {
    case "type":
      return <TypeBadge type={item.type} className="min-w-0" />;
    case "status": {
      const status = statusOf(item);
      return status ? <StatusPill status={status} className="justify-self-start" /> : empty;
    }
    case "due":
      // A finished item isn't overdue: its due date shows plain, by the rule the Overdue filter uses.
      return item.due ? (
        <DateChip date={item.due} kind="due" closed={isTerminal(statusOf(item))} className="justify-self-start" />
      ) : (
        empty
      );
    case "area":
      return item.areaName ? <span className={quiet}>{item.areaName}</span> : empty;
    case "tags": {
      const tags = tagsOf(item);
      return tags.length ? (
        <span className={quiet}>{tags.map((t) => `#${t}`).join(" ")}</span>
      ) : (
        empty
      );
    }
    case "entered": {
      const day = formatDay(localDay(item.createdAt));
      return (
        <span className={cn(quiet, "tabular-nums", !showEntered && "hidden @xl:block")}>
          <span className="@xl:hidden">Entered </span>
          {day}
        </span>
      );
    }
  }
}

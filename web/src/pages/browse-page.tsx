import { useEffect, useId, useMemo, useState, type CSSProperties } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { toast } from "sonner";
import {
  ArrowDownIcon,
  ArrowUpIcon,
  LibraryIcon,
  PlusIcon,
  SlidersHorizontalIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Switch } from "@/components/ui/switch";
import { DateChip } from "@/components/primitives/date-chip";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeBadge } from "@/components/primitives/type-badge";
import { TypeIcon } from "@/components/primitives/type-icon";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { useCreateActions } from "@/components/create/create-context";
import { useDirty } from "@/components/shell/app-shell";
import { PeekPanel } from "@/components/shell/panels";
import {
  DEFAULT_BROWSE_FILTERS,
  DUE_OPTIONS,
  ENTERED_OPTIONS,
  SORT_OPTIONS,
  activeFilterCount,
  browseColumns,
  browseOptions,
  browseRows,
  browseTypeCounts,
  browsableItems,
  itemsOfType,
  localDay,
  parseBrowseType,
  statusOf,
  tagsOf,
  type BrowseColumn,
  type BrowseFilters,
  type BrowseOptions,
  type BrowseSort,
  type BrowseType,
  type DueFilter,
  type EnteredFilter,
  type FilterOption,
  type TypeCount,
} from "@/lib/browse";
import { formatDay, todayIso } from "@/lib/dates";
import { useIsDesktop } from "@/lib/media-query";
import {
  CREATABLE_TYPES,
  isTerminal,
  typeLabel,
  type CreatedSubject,
  type SubjectListItem,
} from "@/lib/production-ui-types";
import { useSubjects } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { loadView, saveView } from "@/lib/view-state";

type BrowseStored = BrowseFilters & { type?: string; selected?: string };

const STORED_DEFAULT: BrowseStored = { ...DEFAULT_BROWSE_FILTERS, type: "all" };

const strings = (v: unknown): string[] =>
  Array.isArray(v) ? v.filter((x): x is string => typeof x === "string") : [];

/** The saved filters, with anything malformed back at its default. */
function storedFilters(stored: BrowseStored): BrowseFilters {
  const d = DEFAULT_BROWSE_FILTERS;
  const pick = <T extends string>(value: unknown, allowed: readonly { value: T }[], fallback: T) =>
    allowed.some((o) => o.value === value) ? (value as T) : fallback;
  return {
    text: typeof stored.text === "string" ? stored.text : d.text,
    statuses: strings(stored.statuses),
    areas: strings(stored.areas),
    tags: strings(stored.tags),
    due: pick<DueFilter>(stored.due, DUE_OPTIONS, d.due),
    entered: pick<EnteredFilter>(stored.entered, ENTERED_OPTIONS, d.entered),
    archived: stored.archived === true,
    sort: pick<BrowseSort>(stored.sort, SORT_OPTIONS, d.sort),
    dir: stored.dir === "desc" ? "desc" : "asc",
  };
}

/** Clears what narrows the list; the sort and Include archived stay as they are. */
function cleared(f: BrowseFilters): BrowseFilters {
  return { ...DEFAULT_BROWSE_FILTERS, sort: f.sort, dir: f.dir, archived: f.archived };
}

export function BrowsePage() {
  const search = useSearch({ from: "/browse" });
  const navigate = useNavigate({ from: "/browse" });
  const isDesktop = useIsDesktop();
  const { openNew } = useCreateActions();
  const { setDirty, requestNavigation } = useDirty();
  // The view saved last visit, read once before this visit's first save overwrites it.
  const [stored] = useState(() => loadView<BrowseStored>("browse", STORED_DEFAULT));
  const [filters, setFilters] = useState<BrowseFilters>(() => storedFilters(stored));
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [created, setCreated] = useState<CreatedSubject | null>(null);
  const { data, isLoading, isError } = useSubjects(undefined, true);
  const type: BrowseType = search.type ?? "all";
  const selected = search.selected;
  const today = todayIso();

  // Come back to the type and item left open last time (NAV-1), unless the link names them.
  useEffect(() => {
    const storedType = parseBrowseType(stored.type);
    const restoreType = !search.type && storedType && storedType !== "all";
    const restoreSelected = !search.selected && typeof stored.selected === "string";
    if (!restoreType && !restoreSelected) return;
    void navigate({
      search: (prev) => ({
        ...prev,
        type: prev.type ?? storedType,
        selected: prev.selected ?? stored.selected,
      }),
      replace: true,
    });
  }, []); // restore once

  useEffect(() => {
    saveView<BrowseStored>("browse", { ...filters, type, selected });
  }, [filters, type, selected]);

  const all = useMemo(() => browsableItems(data ?? []), [data]);
  const counts = useMemo(() => browseTypeCounts(all, filters.archived), [all, filters.archived]);
  const typed = useMemo(
    () => itemsOfType(all, type, filters.archived),
    [all, type, filters.archived],
  );
  const options = useMemo(() => browseOptions(typed, type), [typed, type]);
  const columns = useMemo(() => browseColumns(type, typed), [type, typed]);
  const rows = useMemo(() => browseRows(all, type, filters, today), [all, type, filters, today]);
  const narrowing = activeFilterCount(filters);

  const selectedItem = selected ? all.find((i) => i.id === selected) : undefined;
  const selectedHidden = !!selectedItem && !rows.some((r) => r.id === selectedItem.id);

  function select(id: string | undefined) {
    requestNavigation(() => {
      void navigate({ search: (prev) => ({ ...prev, selected: id }) });
    });
  }

  /** Opens another item here, on its type, as the Pilot's Browse does. */
  function openSubject(id: string) {
    const item = all.find((i) => i.id === id);
    requestNavigation(() => {
      void navigate({
        search: (prev) => ({ type: item ? item.type : prev.type, selected: id }),
      });
    });
  }

  function pickType(next: BrowseType) {
    // The open item stays open when it's one of the type picked.
    const keep = selectedItem && (next === "all" || selectedItem.type === next);
    requestNavigation(() => {
      void navigate({
        search: (prev) => ({ ...prev, type: next, selected: keep ? prev.selected : undefined }),
      });
    });
  }

  function showSelected() {
    if (!selectedItem) return;
    setFilters((f) => ({ ...cleared(f), archived: f.archived || selectedItem.archived }));
    if (type !== "all" && type !== selectedItem.type) {
      void navigate({ search: (prev) => ({ ...prev, type: selectedItem.type }) });
    }
  }

  // A new item is selected once it's in the list and the filters show it (BROWSE-2);
  // otherwise the filters stay as they are and a note says it was made.
  useEffect(() => {
    if (!created) return;
    const item = all.find((i) => i.id === created.id);
    if (!item) return; // not read back yet
    setCreated(null);
    if (rows.some((r) => r.id === item.id)) {
      void navigate({ search: (prev) => ({ ...prev, selected: item.id }) });
    } else {
      toast.success(`Created ${typeLabel(item.type)} “${item.title}”. The current filters hide it.`);
    }
  }, [created, all, rows, navigate]);

  const creatable = type !== "all" && CREATABLE_TYPES.includes(type);
  const heading = type === "all" ? "Everything" : typeLabel(type);
  const shown =
    rows.length === typed.length
      ? `${typed.length} ${typed.length === 1 ? "item" : "items"}`
      : `${rows.length} of ${typed.length} shown`;

  const controls = (
    <FilterControls
      filters={filters}
      options={options}
      showDue={columns.includes("due")}
      onChange={setFilters}
    />
  );

  return (
    <div className="flex h-full min-h-0">
      {isDesktop ? (
        <aside
          aria-label="Browse filters"
          className="hidden w-60 shrink-0 overflow-y-auto border-r px-3 py-4 md:block"
        >
          <TypeList counts={counts} type={type} onPick={pickType} />
          <div className="mt-6">{controls}</div>
        </aside>
      ) : null}

      <div className="@container flex min-w-0 flex-1 flex-col">
        <header className="shrink-0 border-b px-4 py-4 md:px-6">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="type-scale-section text-primary">Browse</p>
              <h1 className="truncate font-heading text-2xl">{heading}</h1>
              <p className="mt-1 text-xs text-muted-foreground" aria-live="polite">
                {isLoading ? "Loading…" : shown}
              </p>
            </div>
            <Button
              size="sm"
              className="shrink-0"
              onClick={() =>
                openNew({ type: creatable ? type : undefined, onCreated: setCreated })
              }
            >
              <PlusIcon /> {creatable ? `New ${typeLabel(type)}` : "New"}
            </Button>
          </div>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            {!isDesktop ? <TypeSelect counts={counts} type={type} onPick={pickType} /> : null}
            <Input
              type="search"
              value={filters.text}
              onChange={(e) => setFilters((f) => ({ ...f, text: e.target.value }))}
              placeholder="Filter by title or URN"
              aria-label="Filter by title or URN"
              className="h-8 min-w-0 flex-1 basis-40"
            />
            {!isDesktop ? (
              <Button variant="outline" size="sm" onClick={() => setFiltersOpen(true)}>
                <SlidersHorizontalIcon /> Filters
                {narrowing - (filters.text.trim() ? 1 : 0) > 0
                  ? ` (${narrowing - (filters.text.trim() ? 1 : 0)})`
                  : null}
              </Button>
            ) : null}
            <SortControl filters={filters} onChange={setFilters} />
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto px-2 py-2 md:px-4">
          {selectedHidden ? (
            <div
              role="status"
              className="mb-2 flex flex-wrap items-center justify-between gap-2 rounded-lg bg-muted/60 px-3 py-2 text-sm"
            >
              <span className="min-w-0">
                “{selectedItem.title}” is open, but the filters hide it in the list.
              </span>
              <Button variant="outline" size="sm" onClick={showSelected}>
                Show it
              </Button>
            </div>
          ) : null}
          {isLoading ? <ListSkeleton rows={8} /> : null}
          {isError ? (
            <EmptyState
              tone="error"
              icon={LibraryIcon}
              title="Items failed to load"
              description="This is not an empty list — the reader never returned."
            />
          ) : null}
          {!isLoading && !isError && typed.length === 0 ? (
            <EmptyState
              icon={LibraryIcon}
              title={type === "all" ? "Nothing here yet" : `No ${typeLabel(type)} yet`}
              description={
                filters.archived ? undefined : "Archived items are hidden. Include them in the filters."
              }
            />
          ) : null}
          {!isLoading && typed.length > 0 && rows.length === 0 ? (
            <EmptyState
              icon={LibraryIcon}
              title="Nothing matches these filters"
              action={
                <Button variant="outline" size="sm" onClick={() => setFilters(cleared)}>
                  Clear filters
                </Button>
              }
            />
          ) : null}
          {rows.length > 0 ? (
            <BrowseList
              label={heading}
              rows={rows}
              columns={columns}
              filters={filters}
              selected={selected}
              onSelect={select}
              onSort={setFilters}
            />
          ) : null}
        </div>
      </div>

      <PeekPanel
        open={!!selected}
        title="Item detail"
        onClose={() => select(undefined)}
      >
        {selected ? (
          <SubjectDetail
            subjectId={selected}
            onDirtyChange={setDirty}
            onOpenSubject={openSubject}
            onClose={() => select(undefined)}
          />
        ) : null}
      </PeekPanel>

      {!isDesktop ? (
        <Sheet open={filtersOpen} onOpenChange={setFiltersOpen}>
          <SheetContent side="bottom" className="max-h-[85dvh] gap-0 overflow-y-auto rounded-t-2xl">
            <SheetHeader>
              <SheetTitle>Filters</SheetTitle>
              <SheetDescription>They apply as you pick them.</SheetDescription>
            </SheetHeader>
            <div className="px-4 pb-6">{controls}</div>
          </SheetContent>
        </Sheet>
      ) : null}
    </div>
  );
}

function TypeList({
  counts,
  type,
  onPick,
}: {
  counts: { total: number; types: TypeCount[] };
  type: BrowseType;
  onPick: (type: BrowseType) => void;
}) {
  const headingId = useId();
  return (
    <section aria-labelledby={headingId}>
      <h2 id={headingId} className="type-scale-section px-2 text-muted-foreground">
        Type
      </h2>
      <ul className="mt-1.5 space-y-0.5">
        <li>
          <TypeButton
            label="All types"
            count={counts.total}
            active={type === "all"}
            onClick={() => onPick("all")}
          />
        </li>
        {counts.types.map((t) => (
          <li key={t.type}>
            <TypeButton
              label={typeLabel(t.type)}
              icon={<TypeIcon type={t.type} className="size-3.5" />}
              count={t.count}
              active={type === t.type}
              onClick={() => onPick(t.type)}
            />
          </li>
        ))}
      </ul>
    </section>
  );
}

function TypeButton({
  label,
  icon,
  count,
  active,
  onClick,
}: {
  label: string;
  icon?: React.ReactNode;
  count: number;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={active ? "true" : undefined}
      className={cn(
        "flex min-h-8 w-full items-center gap-2 rounded-lg px-2 text-left text-sm transition-colors ease-hearth focus-visible:ring-2 focus-visible:ring-ring/50",
        active ? "bg-primary/10 font-medium text-foreground" : "hover:bg-muted/70",
        !active && count === 0 && "text-muted-foreground",
      )}
    >
      {icon ?? <LibraryIcon className="size-3.5" aria-hidden />}
      <span className="min-w-0 flex-1 truncate">{label}</span>
      <span className="text-xs tabular-nums text-muted-foreground">{count}</span>
    </button>
  );
}

function TypeSelect({
  counts,
  type,
  onPick,
}: {
  counts: { total: number; types: TypeCount[] };
  type: BrowseType;
  onPick: (type: BrowseType) => void;
}) {
  return (
    <Select value={type} onValueChange={(v) => onPick(v as BrowseType)}>
      <SelectTrigger size="sm" className="h-8 min-w-36" aria-label="Type">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">All types ({counts.total})</SelectItem>
        {counts.types.map((t) => (
          <SelectItem key={t.type} value={t.type}>
            {typeLabel(t.type)} ({t.count})
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

function FilterControls({
  filters,
  options,
  showDue,
  onChange,
}: {
  filters: BrowseFilters;
  options: BrowseOptions;
  showDue: boolean;
  onChange: (update: (f: BrowseFilters) => BrowseFilters) => void;
}) {
  const archivedId = useId();
  const narrowing = activeFilterCount(filters) - (filters.text.trim() ? 1 : 0);
  return (
    <div className="space-y-5">
      {options.statuses.length ? (
        <CheckList
          legend="Status"
          options={options.statuses}
          picked={filters.statuses}
          onChange={(statuses) => onChange((f) => ({ ...f, statuses }))}
        />
      ) : null}
      {showDue ? (
        <LabeledSelect
          label="Due date"
          value={filters.due}
          options={DUE_OPTIONS}
          onChange={(due) => onChange((f) => ({ ...f, due }))}
        />
      ) : null}
      <LabeledSelect
        label="Entered"
        value={filters.entered}
        options={ENTERED_OPTIONS}
        onChange={(entered) => onChange((f) => ({ ...f, entered }))}
      />
      {options.areas.length ? (
        <CheckList
          legend="Area"
          options={options.areas}
          picked={filters.areas}
          onChange={(areas) => onChange((f) => ({ ...f, areas }))}
        />
      ) : null}
      {options.tags.length ? (
        <CheckList
          legend="Tags"
          options={options.tags}
          picked={filters.tags}
          onChange={(tags) => onChange((f) => ({ ...f, tags }))}
          scroll
        />
      ) : null}
      <div className="flex items-center justify-between gap-2 px-1">
        <Label htmlFor={archivedId} className="font-normal">
          Include archived
        </Label>
        <Switch
          id={archivedId}
          checked={filters.archived}
          onCheckedChange={(archived) => onChange((f) => ({ ...f, archived }))}
        />
      </div>
      {narrowing > 0 ? (
        <Button variant="outline" size="sm" onClick={() => onChange((f) => ({ ...cleared(f), text: f.text }))}>
          Clear filters
        </Button>
      ) : null}
    </div>
  );
}

/** Picked values inside one category combine with OR (BROWSE-2). */
function CheckList({
  legend,
  options,
  picked,
  onChange,
  scroll = false,
}: {
  legend: string;
  options: FilterOption[];
  picked: string[];
  onChange: (picked: string[]) => void;
  scroll?: boolean;
}) {
  const idPrefix = useId();
  return (
    <fieldset>
      <legend className="type-scale-section px-1 text-muted-foreground">{legend}</legend>
      <ul className={cn("mt-1.5 space-y-1.5 px-1", scroll && "max-h-48 overflow-y-auto py-0.5")}>
        {options.map((o, i) => {
          const id = `${idPrefix}-${i}`;
          const on = picked.includes(o.value);
          return (
            <li key={o.value} className="flex min-h-8 items-center gap-2 md:min-h-0">
              <Checkbox
                id={id}
                checked={on}
                onCheckedChange={(next) =>
                  onChange(
                    next === true
                      ? [...picked, o.value]
                      : picked.filter((p) => p !== o.value),
                  )
                }
              />
              <Label htmlFor={id} className="min-w-0 flex-1 truncate font-normal">
                {o.label}
              </Label>
              <span className="text-xs tabular-nums text-muted-foreground">{o.count}</span>
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}

function LabeledSelect<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onChange: (value: T) => void;
}) {
  return (
    <div className="space-y-1.5 px-1">
      <p className="type-scale-section text-muted-foreground">{label}</p>
      <Select value={value} onValueChange={(v) => onChange(v as T)}>
        <SelectTrigger size="sm" className="h-8 w-full" aria-label={label}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {options.map((o) => (
            <SelectItem key={o.value} value={o.value}>
              {o.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

/** A new sort runs ascending, except Entered, which starts newest first. */
function nextSort(f: BrowseFilters, sort: BrowseSort): BrowseFilters {
  if (f.sort === sort) return { ...f, dir: f.dir === "asc" ? "desc" : "asc" };
  return { ...f, sort, dir: sort === "entered" ? "desc" : "asc" };
}

/** The sort as a menu, for a list too narrow for its column headers. */
function SortControl({
  filters,
  onChange,
}: {
  filters: BrowseFilters;
  onChange: (update: (f: BrowseFilters) => BrowseFilters) => void;
}) {
  const asc = filters.dir === "asc";
  return (
    <div className="flex items-center gap-1 @xl:hidden">
      <Select
        value={filters.sort}
        onValueChange={(v) => onChange((f) => ({ ...nextSort(f, v as BrowseSort) }))}
      >
        <SelectTrigger size="sm" className="h-8" aria-label="Sort by">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {SORT_OPTIONS.map((o) => (
            <SelectItem key={o.value} value={o.value}>
              {o.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        variant="outline"
        size="icon"
        aria-label={asc ? "Sorted ascending: sort descending" : "Sorted descending: sort ascending"}
        onClick={() => onChange((f) => ({ ...f, dir: f.dir === "asc" ? "desc" : "asc" }))}
      >
        {asc ? <ArrowUpIcon /> : <ArrowDownIcon />}
      </Button>
    </div>
  );
}

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

const COLUMN_SORT: Partial<Record<BrowseColumn, BrowseSort>> = {
  status: "status",
  due: "due",
  area: "area",
  entered: "entered",
};

/**
 * The list: a grid with sortable column headers when there's room, stacked rows when there
 * isn't (the list's own width decides, so an open detail panel narrows it too).
 */
function BrowseList({
  label,
  rows,
  columns,
  filters,
  selected,
  onSelect,
  onSort,
}: {
  label: string;
  rows: SubjectListItem[];
  columns: BrowseColumn[];
  filters: BrowseFilters;
  selected?: string;
  onSelect: (id: string) => void;
  onSort: (update: (f: BrowseFilters) => BrowseFilters) => void;
}) {
  const template = ["minmax(0,1fr)", ...columns.map((c) => COLUMN_WIDTH[c])].join(" ");
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
        {rows.map((item) => (
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
      // A finished item isn't overdue: its due date shows plain.
      return item.due ? (
        <DateChip
          date={item.due}
          kind={isTerminal(statusOf(item)) ? "plain" : "due"}
          className="justify-self-start"
        />
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

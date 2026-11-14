import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { toast } from "sonner";
import { LibraryIcon, PlusIcon, SlidersHorizontalIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { useCreateActions } from "@/components/create/create-context";
import { useDirty } from "@/components/shell/unsaved-edits";
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
  clearedFilters,
  itemsOfType,
  parseBrowseType,
  type BrowseFilters,
  type BrowseSort,
  type BrowseType,
  type DueFilter,
  type EnteredFilter,
} from "@/lib/browse";
import { todayIso } from "@/lib/dates";
import { useIsDesktop } from "@/lib/media-query";
import { CREATABLE_TYPES, typeLabel, type CreatedSubject } from "@/lib/production-ui-types";
import { useSubjects } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { ReadFailed } from "@/components/primitives/read-state";
import {
  FilterControls,
  SortControl,
  TypeList,
  TypeSelect,
} from "@/components/browse/browse-filters";
import { BrowseList } from "@/components/browse/browse-list";

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
  const subjects = useSubjects(undefined, true);
  const { data, isLoading, isError } = subjects;
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
    // Once, on opening: later changes to the link are the user's own moves, not a visit to restore.
    // oxlint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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
    setFilters((f) => ({ ...clearedFilters(f), archived: f.archived || selectedItem.archived }));
    if (type !== "all" && type !== selectedItem.type) {
      void navigate({ search: (prev) => ({ ...prev, type: selectedItem.type }) });
    }
  }

  // A new item is selected once it's in the list and the filters show it (BROWSE-2);
  // otherwise the filters stay as they are and a note says it was made.
  const shownCreated = useRef<string | null>(null);
  useEffect(() => {
    if (!created || shownCreated.current === created.id) return;
    const item = all.find((i) => i.id === created.id);
    if (!item) return; // not read back yet
    shownCreated.current = created.id;
    if (rows.some((r) => r.id === item.id)) {
      void navigate({ search: (prev) => ({ ...prev, selected: item.id }) });
    } else {
      toast.success(`Created ${typeLabel(item.type)} “${item.title}”. The current filters hide it.`);
    }
  }, [created, all, rows, navigate]);

  const creatable = type !== "all" && CREATABLE_TYPES.includes(type);
  // New makes the type looked at, or anything from All types. A type New can't make (Season,
  // Constraint, Reward) gets no button: a plain "New" there wouldn't make one.
  const showNew = type === "all" || creatable;
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
            {showNew ? (
              <Button
                size="sm"
                className="shrink-0"
                onClick={() =>
                  openNew({ type: creatable ? type : undefined, onCreated: setCreated })
                }
              >
                <PlusIcon /> {creatable ? `New ${typeLabel(type)}` : "New"}
              </Button>
            ) : null}
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
            <SortControl filters={filters} columns={columns} onChange={setFilters} />
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
          <ReadFailed
            read={subjects}
            what="Items"
            notEmpty="an empty list"
            size="page"
            icon={LibraryIcon}
          />
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
                <Button variant="outline" size="sm" onClick={() => setFilters(clearedFilters)}>
                  Clear filters
                </Button>
              }
            />
          ) : null}
          {rows.length > 0 ? (
            <BrowseList
              // A new type or filter starts again from the first page of rows.
              pagedFor={`${type}:${JSON.stringify(filters)}`}
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

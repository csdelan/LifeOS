import { lazy, Suspense, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useAlignmentEdges, useAlignmentForest, useAreas, useSubjects } from "@/lib/queries";
import type { AlignmentEdge, MapLens } from "@/lib/derive";
import { buildAlignmentGraph } from "@/lib/graph-model";
import type { GraphLayoutKind, MapViewMode, OrphanFilter } from "@/lib/graph-model";
import { DEFAULT_MAP_SHOW, MAP_SHOW_OPTIONS, showForest, type MapShow } from "@/lib/map-show";
import type { AreaRow, SubjectListItem } from "@/lib/production-ui-types";
import { TreeSkeleton, GraphSkeleton } from "@/components/primitives/skeletons";
import { EmptyState } from "@/components/primitives/empty-state";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useDirty } from "@/components/shell/unsaved-edits";
import { PeekPanel } from "@/components/shell/panels";
import { loadExpanded, saveExpanded, loadView, saveView } from "@/lib/view-state";
import { mapSubjects } from "@/lib/task-series";
import { MapIcon } from "lucide-react";
import { ReadFailed } from "@/components/primitives/read-state";
import { MapBranch } from "@/components/map/map-branch";
import { Segmented } from "@/components/map/segmented";
import { ancestorIds, collectNodeIds, findNode } from "@/lib/map-tree";

// The graph engine (React Flow and ELK) is most of the Map's weight, so it loads only when the
// graph is shown: the Outline, the default view, never fetches it (code review CR84).
const AlignmentGraph = lazy(() =>
  import("@/components/graph/alignment-graph").then((m) => ({ default: m.AlignmentGraph })),
);

const LENSES: { id: MapLens; label: string }[] = [
  { id: "all", label: "All" },
  { id: "Goal", label: "Goals" },
  { id: "Project", label: "Projects" },
  { id: "Task", label: "Tasks" },
];

const EMPTY_SUBJECTS: SubjectListItem[] = [];
const EMPTY_EDGES: AlignmentEdge[] = [];
const EMPTY_AREAS: AreaRow[] = [];

type MapStored = {
  archived?: boolean;
  lens?: MapLens;
  selected?: string;
  view?: MapViewMode;
  layout?: GraphLayoutKind;
  orphans?: OrphanFilter;
  area?: string;
  colorByArea?: boolean;
  expandAll?: boolean;
  show?: MapShow;
};

export function MapPage() {
  const search = useSearch({ from: "/map" });
  const navigate = useNavigate({ from: "/map" });
  const archived = Boolean(search.archived);
  const lens = (search.lens ?? "all") as MapLens;
  const selected = search.selected as string | undefined;
  const view: MapViewMode = search.view === "graph" ? "graph" : "outline";
  const layout: GraphLayoutKind = search.layout === "force" ? "force" : "layered";
  const orphans: OrphanFilter =
    search.orphans === "only" || search.orphans === "hide" ? search.orphans : "all";
  const area = search.area;
  const colorByArea = Boolean(search.colorByArea);
  const show: MapShow = search.show ?? DEFAULT_MAP_SHOW;
  const forestRead = useAlignmentForest(archived, lens);
  const { data, isLoading } = forestRead;
  // Show hides finished work; the selected item stays, so marking it Completed doesn't
  // make it vanish from under you.
  const shown = useMemo(
    () => (data ? showForest(data, show, selected) : null),
    [data, show, selected],
  );
  const forest = shown?.nodes;
  // A repeating Task is one node, its series; its repetitions stay off the Map (TASKS-3).
  const subjectsQuery = useSubjects(undefined, archived, true);
  const graphSubjects = useMemo(
    () => mapSubjects(subjectsQuery.data ?? EMPTY_SUBJECTS),
    [subjectsQuery.data],
  );
  const edgesQuery = useAlignmentEdges(archived);
  const areasQuery = useAreas();
  const graphModel = useMemo(
    () =>
      buildAlignmentGraph(graphSubjects, edgesQuery.data ?? EMPTY_EDGES, {
        lens,
        area,
        orphans,
        show,
        keep: selected,
      }),
    [graphSubjects, edgesQuery.data, lens, area, orphans, show, selected],
  );
  const hiddenCount = view === "graph" ? graphModel.hiddenFinished : (shown?.hidden ?? 0);
  const { setDirty, requestNavigation } = useDirty();
  // The view saved last visit, read once before this visit's first save overwrites it.
  const [storedAtMount] = useState(() => loadView<MapStored>("map", {}));
  const [expanded, setExpanded] = useState<Set<string>>(() => loadExpanded());
  const [expandAll, setExpandAll] = useState(() => Boolean(storedAtMount.expandAll));
  const [creating, setCreating] = useState<{ parentId: string } | null>(null);
  const pendingReveal = useRef<string | null>(null);
  const skipExpandBootstrap = useRef(storedAtMount.expandAll === false);

  useEffect(() => {
    saveExpanded(expanded);
  }, [expanded]);

  useEffect(() => {
    saveView("map", {
      archived,
      lens,
      selected,
      view,
      layout,
      orphans,
      area,
      colorByArea,
      expandAll,
      show,
    } satisfies MapStored);
  }, [archived, lens, selected, view, layout, orphans, area, colorByArea, expandAll, show]);

  useEffect(() => {
    const stored = storedAtMount;
    const patch: MapStored = {};
    if (!search.selected && stored.selected) patch.selected = stored.selected;
    if (!search.view && stored.view) patch.view = stored.view;
    if (!search.layout && stored.layout) patch.layout = stored.layout;
    if (!search.orphans && stored.orphans) patch.orphans = stored.orphans;
    if (!search.area && stored.area) patch.area = stored.area;
    if (!search.show && stored.show) patch.show = stored.show;
    if (search.colorByArea === undefined && stored.colorByArea) {
      patch.colorByArea = stored.colorByArea;
    }
    if (Object.keys(patch).length === 0) return;
    void navigate({
      search: (prev) => ({
        ...prev,
        ...patch,
        lens: (search.lens as MapLens) ?? stored.lens ?? "all",
        archived: search.archived ?? stored.archived ?? false,
      }),
      replace: true,
    });
    // Once, on opening: later changes to the link are the user's own moves, not a visit to restore.
    // oxlint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!data || !expandAll) return;
    // Kept as the set itself, not worked out from the switch: turning Expand all off leaves every
    // row open, to be closed one by one, and the set saved is the one shown.
    // oxlint-disable-next-line react/set-state-in-effect
    setExpanded(collectNodeIds(data));
  }, [data, expandAll]);

  useEffect(() => {
    if (!data || expandAll || skipExpandBootstrap.current || expanded.size > 0) return;
    skipExpandBootstrap.current = true;
    const ids = new Set<string>();
    for (const n of data) {
      ids.add(n.id);
      for (const c of n.children) ids.add(c.id);
    }
    setExpanded(ids);
  }, [data, expandAll, expanded.size]);

  function select(id: string) {
    if (id === selected) return;
    requestNavigation(() => {
      void navigate({
        search: (prev) => ({ ...prev, selected: id }),
      });
    });
  }

  function navigateToParent(id: string) {
    if (data) {
      const ancestors = ancestorIds(data, id) ?? [];
      if (ancestors.length > 0) {
        setExpanded((prev) => {
          const next = new Set(prev);
          for (const a of ancestors) next.add(a);
          return next;
        });
      }
    }
    pendingReveal.current = id;
    select(id);
    if (id === selected) {
      requestAnimationFrame(() => {
        if (pendingReveal.current !== id) return;
        pendingReveal.current = null;
        const el = document.querySelector(`[data-map-node="${CSS.escape(id)}"]`);
        el?.scrollIntoView({ block: "center", behavior: "smooth" });
      });
    }
  }

  useEffect(() => {
    if (!pendingReveal.current || pendingReveal.current !== selected) return;
    const id = pendingReveal.current;
    pendingReveal.current = null;
    const frame = requestAnimationFrame(() => {
      const el = document.querySelector(`[data-map-node="${CSS.escape(id)}"]`);
      el?.scrollIntoView({ block: "center", behavior: "smooth" });
    });
    return () => cancelAnimationFrame(frame);
  }, [selected, expanded]);

  function toggle(id: string) {
    setExpandAll(false);
    skipExpandBootstrap.current = true;
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function onExpandAllChange(checked: boolean) {
    setExpandAll(checked);
    skipExpandBootstrap.current = !checked;
    if (!data) return;
    setExpanded(checked ? collectNodeIds(data) : new Set());
  }

  const selectedTitle = useMemo(() => {
    if (!selected || !data) return null;
    const found = findNode(data, selected);
    return found?.subject.title ?? null;
  }, [data, selected]);
  const graphLoading =
    subjectsQuery.isLoading || edgesQuery.isLoading || areasQuery.isLoading;
  const graphError = subjectsQuery.isError || edgesQuery.isError;

  return (
    <div className="flex h-full min-h-0">
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex flex-col gap-2 border-b px-4 py-3 md:px-6">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h1 className="font-heading text-2xl">Map</h1>
              <p className="text-xs text-muted-foreground">
                {view === "graph"
                  ? "Alignment graph. Area colors grouping, never edges. Outline is the accessible equivalent."
                  : "Alignment graph as an outline. A node may appear under more than one parent."}
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-2 sm:justify-end">
              <Segmented
                ariaLabel="Map view"
                value={view}
                options={[
                  { id: "outline", label: "Outline" },
                  { id: "graph", label: "Graph" },
                ]}
                onChange={(id) =>
                  void navigate({ search: (prev) => ({ ...prev, view: id }) })
                }
              />
              <Segmented
                ariaLabel="Type lens"
                value={lens}
                options={LENSES}
                onChange={(id) =>
                  void navigate({ search: (prev) => ({ ...prev, lens: id }) })
                }
              />
              <div className="flex items-center gap-1.5">
                <span className="text-xs text-muted-foreground" aria-hidden>
                  Show
                </span>
                <Select
                  value={show}
                  onValueChange={(value) =>
                    void navigate({
                      search: (prev) => ({ ...prev, show: value as MapShow }),
                    })
                  }
                >
                  <SelectTrigger size="sm" className="h-7 min-w-24" aria-label="Show">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {MAP_SHOW_OPTIONS.map((o) => (
                      <SelectItem key={o.id} value={o.id}>
                        {o.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {hiddenCount > 0 ? (
                  <span className="text-xs text-muted-foreground" data-testid="map-hidden-count">
                    {hiddenCount} finished hidden
                  </span>
                ) : null}
              </div>
              <label className="flex min-h-11 items-center gap-1.5 text-xs text-muted-foreground md:min-h-0">
                <input
                  id="map-archived"
                  type="checkbox"
                  checked={archived}
                  onChange={(e) =>
                    void navigate({
                      search: (prev) => ({ ...prev, archived: e.target.checked }),
                    })
                  }
                />
                Archived
              </label>
            </div>
          </div>
          {view === "graph" ? (
            <div className="flex flex-wrap items-center gap-2">
              <Segmented
                ariaLabel="Graph layout"
                value={layout}
                options={[
                  { id: "layered", label: "Layered" },
                  { id: "force", label: "Force" },
                ]}
                onChange={(id) =>
                  void navigate({ search: (prev) => ({ ...prev, layout: id }) })
                }
              />
              <Segmented
                ariaLabel="Orphan filter"
                value={orphans}
                options={[
                  { id: "all", label: "All" },
                  { id: "hide", label: "Hide orphans" },
                  { id: "only", label: "Orphans" },
                ]}
                onChange={(id) =>
                  void navigate({ search: (prev) => ({ ...prev, orphans: id }) })
                }
              />
              <Select
                value={area ?? "all"}
                onValueChange={(value) =>
                  void navigate({
                    search: (prev) => ({
                      ...prev,
                      area: value === "all" ? undefined : value,
                    }),
                  })
                }
              >
                <SelectTrigger size="sm" className="h-7 min-w-36" aria-label="Filter by area">
                  <SelectValue placeholder="Area" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All areas</SelectItem>
                  {(areasQuery.data ?? []).map((a) => (
                    <SelectItem key={a.id} value={a.urn}>
                      {a.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <label className="flex min-h-11 items-center gap-2 text-xs text-muted-foreground md:min-h-0">
                <Switch
                  size="sm"
                  checked={colorByArea}
                  onCheckedChange={(checked) =>
                    void navigate({
                      search: (prev) => ({ ...prev, colorByArea: checked }),
                    })
                  }
                  aria-label="Color by Area"
                />
                Color by Area
              </label>
            </div>
          ) : null}
        </header>
        {view === "graph" ? (
          <div className="min-h-0 flex-1">
            {graphLoading ? (
              <div className="h-full p-6">
                <GraphSkeleton />
              </div>
            ) : null}
            <ReadFailed
              read={[subjectsQuery, edgesQuery]}
              what="The graph"
              notEmpty="an empty graph"
              size="page"
              icon={MapIcon}
            />
            {!graphLoading && !graphError ? (
              <>
                <p className="sr-only">
                  The graph is a visual enhancement. Switch to Outline view for a fully
                  keyboard and screen-reader accessible map.
                </p>
                <Suspense
                  fallback={
                    <div className="h-full p-6">
                      <GraphSkeleton />
                    </div>
                  }
                >
                  <AlignmentGraph
                    model={graphModel}
                    areas={areasQuery.data ?? EMPTY_AREAS}
                    colorByArea={colorByArea}
                    layout={layout}
                    selected={selected}
                    onSelect={select}
                  />
                </Suspense>
              </>
            ) : null}
          </div>
        ) : (
          <div className="min-h-0 flex-1 overflow-y-auto px-3 py-3">
            <label className="mb-1 flex items-center gap-2 px-2 text-xs text-muted-foreground">
              <Switch
                size="sm"
                checked={expandAll}
                onCheckedChange={onExpandAllChange}
                aria-label="Expand all"
              />
              Expand all
            </label>
            {isLoading ? <TreeSkeleton /> : null}
            <ReadFailed
              read={forestRead}
              what="Map"
              notEmpty="an empty graph"
              size="page"
              icon={MapIcon}
            />
            {data && data.length === 0 ? (
              <EmptyState
                icon={MapIcon}
                title="Nothing on the map"
                description="Create an Identity Statement to grow the alignment graph from."
              />
            ) : null}
            {data && data.length > 0 && forest?.length === 0 ? (
              <EmptyState
                icon={MapIcon}
                title="Nothing open on the map"
                description="Everything here is finished. Set Show to All to see it."
              />
            ) : null}
            <ul aria-label="Alignment outline">
              {forest?.map((n) => (
                <MapBranch
                  key={n.instanceKey}
                  node={n}
                  expanded={expanded}
                  selected={selected}
                  creating={creating}
                  onToggle={toggle}
                  onSelect={select}
                  onNavigateParent={navigateToParent}
                  onCreate={(node) => setCreating({ parentId: node.id })}
                  onCancelCreate={() => setCreating(null)}
                />
              ))}
            </ul>
          </div>
        )}
      </div>
      <PeekPanel
        open={!!selected}
        title={selectedTitle ?? "Subject detail"}
        onClose={() =>
          requestNavigation(() =>
            void navigate({ search: (prev) => ({ ...prev, selected: undefined }) }),
          )
        }
      >
        {selected ? (
          <SubjectDetail
            subjectId={selected}
            onDirtyChange={setDirty}
            onOpenSubject={select}
            onClose={() =>
              requestNavigation(() =>
                void navigate({ search: (prev) => ({ ...prev, selected: undefined }) }),
              )
            }
          />
        ) : null}
      </PeekPanel>
    </div>
  );
}

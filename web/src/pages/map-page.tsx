import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import {
  useAlignmentEdges,
  useAlignmentForest,
  useAreas,
  useCreateSubject,
  useSubjects,
} from "@/lib/queries";
import type { AlignmentEdge, AlignmentNode, MapLens } from "@/lib/derive";
import type { GraphLayoutKind, MapViewMode, OrphanFilter } from "@/lib/graph-model";
import { childTypesFor, inferChildRelation, typeLabel } from "@/lib/production-ui-types";
import type { AreaRow, SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { TreeNode } from "@/components/primitives/tree-node";
import { TreeSkeleton, GraphSkeleton } from "@/components/primitives/skeletons";
import { EmptyState } from "@/components/primitives/empty-state";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { AlignmentGraph } from "@/components/graph/alignment-graph";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useDirty } from "@/components/shell/app-shell";
import { PeekPanel } from "@/components/shell/panels";
import { loadExpanded, saveExpanded, loadView, saveView } from "@/lib/view-state";
import { MapIcon } from "lucide-react";

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
  const { data, isLoading, isError } = useAlignmentForest(archived, lens);
  const subjectsQuery = useSubjects(undefined, archived);
  const edgesQuery = useAlignmentEdges(archived);
  const areasQuery = useAreas();
  const { setDirty, requestNavigation } = useDirty();
  const [expanded, setExpanded] = useState<Set<string>>(() => loadExpanded());
  const [creating, setCreating] = useState<{ parentId: string } | null>(null);
  const pendingReveal = useRef<string | null>(null);

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
    } satisfies MapStored);
  }, [archived, lens, selected, view, layout, orphans, area, colorByArea]);

  useEffect(() => {
    const stored = loadView<MapStored>("map", {});
    const patch: MapStored = {};
    if (!search.selected && stored.selected) patch.selected = stored.selected;
    if (!search.view && stored.view) patch.view = stored.view;
    if (!search.layout && stored.layout) patch.layout = stored.layout;
    if (!search.orphans && stored.orphans) patch.orphans = stored.orphans;
    if (!search.area && stored.area) patch.area = stored.area;
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
  }, []); // restore once

  useEffect(() => {
    if (!data || expanded.size > 0) return;
    const ids = new Set<string>();
    for (const n of data) {
      ids.add(n.id);
      for (const c of n.children) ids.add(c.id);
    }
    setExpanded(ids);
  }, [data, expanded.size]);

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
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const selectedTitle = useMemo(() => {
    if (!selected || !data) return null;
    const found = findNode(data, selected);
    return found?.subject.title ?? null;
  }, [data, selected]);
  void selectedTitle;

  const graphLoading =
    subjectsQuery.isLoading || edgesQuery.isLoading || areasQuery.isLoading;
  const graphError = subjectsQuery.isError || edgesQuery.isError;

  return (
    <div className="flex h-full min-h-0">
      <div
        className="flex min-w-0 flex-1 flex-col"
        onKeyDown={(e) => {
          if (view !== "outline" || !selected) return;
          if (e.key === "Enter" && !(e.target instanceof HTMLInputElement)) {
            e.preventDefault();
            setCreating({ parentId: selected });
          }
        }}
      >
        <header className="flex flex-col gap-2 border-b px-6 py-3">
          <div className="flex items-center justify-between gap-3">
            <div>
              <h1 className="font-heading text-2xl">Map</h1>
              <p className="text-xs text-muted-foreground">
                {view === "graph"
                  ? "Alignment graph. Area colors grouping, never edges."
                  : "Alignment graph as an outline. A node may appear under more than one parent."}
              </p>
            </div>
            <div className="flex flex-wrap items-center justify-end gap-2">
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
              <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <input
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
                <SelectTrigger size="sm" className="h-7 min-w-36">
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
              <label className="flex items-center gap-2 text-xs text-muted-foreground">
                <Switch
                  size="sm"
                  checked={colorByArea}
                  onCheckedChange={(checked) =>
                    void navigate({
                      search: (prev) => ({ ...prev, colorByArea: checked }),
                    })
                  }
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
            {graphError ? (
              <EmptyState
                tone="error"
                icon={MapIcon}
                title="Graph failed to load"
                description="This is not an empty graph — the reader never returned."
              />
            ) : null}
            {!graphLoading && !graphError ? (
              <AlignmentGraph
                subjects={subjectsQuery.data ?? EMPTY_SUBJECTS}
                edges={edgesQuery.data ?? EMPTY_EDGES}
                areas={areasQuery.data ?? EMPTY_AREAS}
                lens={lens}
                orphans={orphans}
                area={area}
                colorByArea={colorByArea}
                layout={layout}
                selected={selected}
                onSelect={select}
              />
            ) : null}
          </div>
        ) : (
          <div className="min-h-0 flex-1 overflow-y-auto px-3 py-3">
            {isLoading ? <TreeSkeleton /> : null}
            {isError ? (
              <EmptyState
                tone="error"
                icon={MapIcon}
                title="Map failed to load"
                description="This is not an empty graph — the reader never returned."
              />
            ) : null}
            {data && data.length === 0 ? (
              <EmptyState
                icon={MapIcon}
                title="Nothing on the map"
                description="Create an Identity Statement to grow the alignment graph from."
              />
            ) : null}
            {data?.map((n) => (
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
          </div>
        )}
      </div>
      <PeekPanel open={!!selected}>
        {selected ? (
          <SubjectDetail
            subjectId={selected}
            onDirtyChange={setDirty}
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

function Segmented<T extends string>({
  value,
  options,
  onChange,
  ariaLabel,
}: {
  value: T;
  options: { id: T; label: string }[];
  onChange: (id: T) => void;
  ariaLabel: string;
}) {
  return (
    <div role="group" aria-label={ariaLabel} className="flex rounded-lg bg-muted p-0.5">
      {options.map((o) => (
        <button
          key={o.id}
          type="button"
          aria-pressed={value === o.id}
          onClick={() => onChange(o.id)}
          className={`rounded-md px-2.5 py-1 text-xs font-medium ${
            value === o.id ? "bg-background shadow-sm" : "text-muted-foreground"
          }`}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

function MapBranch({
  node,
  expanded,
  selected,
  creating,
  onToggle,
  onSelect,
  onNavigateParent,
  onCreate,
  onCancelCreate,
}: {
  node: AlignmentNode;
  expanded: Set<string>;
  selected?: string;
  creating: { parentId: string } | null;
  onToggle: (id: string) => void;
  onSelect: (id: string) => void;
  onNavigateParent: (id: string) => void;
  onCreate: (node: AlignmentNode) => void;
  onCancelCreate: () => void;
}) {
  const isOpen = expanded.has(node.id);
  const childTypes = childTypesFor(node.subject.type);
  const canCreate = childTypes.length > 0 || node.parentId !== null;
  const showCreate = creating && creating.parentId === node.id;

  return (
    <TreeNode
      subject={node.subject}
      depth={node.depth}
      expanded={isOpen}
      expandable={node.children.length > 0 || !!showCreate}
      selected={selected === node.id}
      extraParents={node.extraParents}
      onToggle={() => onToggle(node.id)}
      onSelect={() => onSelect(node.id)}
      onNavigateParent={onNavigateParent}
      onCreateChild={canCreate ? () => onCreate(node) : undefined}
      creating={
        showCreate ? (
          <InlineCreate
            parent={node}
            depth={node.depth + 1}
            onDone={onCancelCreate}
          />
        ) : null
      }
    >
      {isOpen
        ? node.children.map((c) => (
            <MapBranch
              key={c.instanceKey}
              node={c}
              expanded={expanded}
              selected={selected}
              creating={creating}
              onToggle={onToggle}
              onSelect={onSelect}
              onNavigateParent={onNavigateParent}
              onCreate={onCreate}
              onCancelCreate={onCancelCreate}
            />
          ))
        : null}
    </TreeNode>
  );
}

function InlineCreate({
  parent,
  depth,
  onDone,
}: {
  parent: AlignmentNode;
  depth: number;
  onDone: () => void;
}) {
  const childTypes = childTypesFor(parent.subject.type);
  const isSibling = childTypes.length === 0;
  const types: SubjectType[] = isSibling ? ["Task"] : childTypes;
  const [type, setType] = useState<SubjectType>(types[0]);
  const [title, setTitle] = useState("");
  const create = useCreateSubject();

  async function save() {
    if (!title.trim()) return;
    const parentRef = isSibling ? parent.parentId : parent.id;
    const parentType = isSibling ? undefined : parent.subject.type;
    const relation =
      parentRef && parentType ? inferChildRelation(type, parentType) : undefined;
    await create.mutateAsync({
      type,
      title: title.trim(),
      parent: parentRef ?? undefined,
      relation,
    });
    onDone();
  }

  return (
    <div className="flex items-center gap-2 py-1 pr-2" style={{ paddingLeft: 8 + depth * 18 }}>
      <div className="flex gap-1">
        {types.map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setType(t)}
            className={`rounded-md px-1.5 py-0.5 text-[0.6875rem] ${
              type === t ? "bg-primary/12 text-primary" : "text-muted-foreground"
            }`}
          >
            {typeLabel(t)}
          </button>
        ))}
      </div>
      <Input
        autoFocus
        value={title}
        placeholder={isSibling ? "New sibling…" : `New ${typeLabel(type).toLowerCase()}…`}
        className="h-7"
        onChange={(e) => setTitle(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            e.preventDefault();
            void save();
          }
          if (e.key === "Escape") {
            e.preventDefault();
            onDone();
          }
          if (e.key === "Tab" && types.length > 1) {
            e.preventDefault();
            const i = types.indexOf(type);
            setType(types[(i + 1) % types.length]);
          }
        }}
      />
      <Button size="xs" onClick={() => void save()} disabled={!title.trim() || create.isPending}>
        Add
      </Button>
    </div>
  );
}

function findNode(nodes: AlignmentNode[], id: string): AlignmentNode | null {
  for (const n of nodes) {
    if (n.id === id) return n;
    const c = findNode(n.children, id);
    if (c) return c;
  }
  return null;
}

function ancestorIds(nodes: AlignmentNode[], id: string, trail: string[] = []): string[] | null {
  for (const n of nodes) {
    if (n.id === id) return trail;
    const found = ancestorIds(n.children, id, [...trail, n.id]);
    if (found) return found;
  }
  return null;
}

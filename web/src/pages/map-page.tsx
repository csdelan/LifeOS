import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { childTypesFor, inferChildRelation, typeLabel } from "@/lib/production-ui-types";
import type { SubjectType } from "@/lib/production-ui-types";
import { useAlignmentForest, useCreateSubject } from "@/lib/queries";
import type { AlignmentNode, MapLens } from "@/lib/derive";
import { TreeNode } from "@/components/primitives/tree-node";
import { TreeSkeleton } from "@/components/primitives/skeletons";
import { EmptyState } from "@/components/primitives/empty-state";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useDirty } from "@/components/shell/app-shell";
import { loadExpanded, saveExpanded, loadView, saveView } from "@/lib/view-state";
import { MapIcon } from "lucide-react";

const LENSES: { id: MapLens; label: string }[] = [
  { id: "all", label: "All" },
  { id: "Goal", label: "Goals" },
  { id: "Project", label: "Projects" },
  { id: "Task", label: "Tasks" },
];

export function MapPage() {
  const search = useSearch({ from: "/map" });
  const navigate = useNavigate({ from: "/map" });
  const archived = Boolean(search.archived);
  const lens = (search.lens ?? "all") as MapLens;
  const selected = search.selected as string | undefined;
  const { data, isLoading, isError } = useAlignmentForest(archived, lens);
  const { setDirty, requestNavigation } = useDirty();
  const [expanded, setExpanded] = useState<Set<string>>(() => loadExpanded());
  const [creating, setCreating] = useState<{ parentId: string } | null>(null);

  useEffect(() => {
    saveExpanded(expanded);
  }, [expanded]);

  useEffect(() => {
    saveView("map", { archived, lens, selected });
  }, [archived, lens, selected]);

  useEffect(() => {
    const stored = loadView<{ archived?: boolean; lens?: MapLens; selected?: string }>("map", {});
    if (!search.selected && stored.selected) {
      void navigate({
        search: {
          selected: stored.selected,
          lens: (search.lens as MapLens) ?? stored.lens ?? "all",
          archived: search.archived ?? stored.archived ?? false,
        },
        replace: true,
      });
    }
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

  return (
    <div className="flex h-full min-h-0">
      <div
        className="flex min-w-0 flex-1 flex-col"
        onKeyDown={(e) => {
          if (!selected) return;
          if (e.key === "Enter" && !(e.target instanceof HTMLInputElement)) {
            e.preventDefault();
            setCreating({ parentId: selected });
          }
        }}
      >
        <header className="flex items-center justify-between gap-3 border-b px-6 py-3">
          <div>
            <h1 className="font-heading text-2xl">Map</h1>
            <p className="text-xs text-muted-foreground">
              Alignment graph as an outline. A node may appear under more than one parent.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <div className="flex rounded-lg bg-muted p-0.5">
              {LENSES.map((l) => (
                <button
                  key={l.id}
                  type="button"
                  onClick={() =>
                    void navigate({ search: (prev) => ({ ...prev, lens: l.id }) })
                  }
                  className={`rounded-md px-2.5 py-1 text-xs font-medium ${
                    lens === l.id ? "bg-background shadow-sm" : "text-muted-foreground"
                  }`}
                >
                  {l.label}
                </button>
              ))}
            </div>
            <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <input
                type="checkbox"
                checked={archived}
                onChange={(e) =>
                  void navigate({ search: (prev) => ({ ...prev, archived: e.target.checked }) })
                }
              />
              Archived
            </label>
          </div>
        </header>
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
              onCreate={(node) =>
                setCreating({ parentId: node.id })
              }
              onCancelCreate={() => setCreating(null)}
            />
          ))}
        </div>
      </div>
      <aside
        className={`shrink-0 overflow-hidden border-l bg-card shadow-(--shadow-peek) transition-[width] duration-200 ease-hearth ${
          selected ? "w-[26rem]" : "w-0 border-l-0"
        }`}
      >
        {selected ? (
          <div className="h-full w-[26rem]">
            <SubjectDetail
              subjectId={selected}
              onDirtyChange={setDirty}
              onClose={() =>
                requestNavigation(() =>
                  void navigate({ search: (prev) => ({ ...prev, selected: undefined }) }),
                )
              }
            />
          </div>
        ) : null}
      </aside>
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
  onCreate,
  onCancelCreate,
}: {
  node: AlignmentNode;
  expanded: Set<string>;
  selected?: string;
  creating: { parentId: string } | null;
  onToggle: (id: string) => void;
  onSelect: (id: string) => void;
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
    const parentType = isSibling
      ? undefined
      : parent.subject.type;
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

import { useState } from "react";
import { useCreateSubject } from "@/lib/queries";
import type { AlignmentNode } from "@/lib/derive";
import { isFinished } from "@/lib/map-show";
import { childTypesFor, inferChildRelation, typeLabel } from "@/lib/production-ui-types";
import type { SubjectType } from "@/lib/production-ui-types";
import { TreeNode } from "@/components/primitives/tree-node";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useWriteAction } from "@/lib/write-action";

export function MapBranch({
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
      finished={isFinished(node.subject)}
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
  // Enter and Add both run through here, one create at a time: a second Enter while the first is
  // still saving made a second item (code review CR60).
  const adding = useWriteAction({ onDone });

  function save() {
    if (!title.trim()) return;
    const parentRef = isSibling ? parent.parentId : parent.id;
    const parentType = isSibling ? undefined : parent.subject.type;
    const relation =
      parentRef && parentType ? inferChildRelation(type, parentType) : undefined;
    void adding.run(() =>
      create.mutateAsync({
        type,
        title: title.trim(),
        parent: parentRef ?? undefined,
        relation,
      }),
    );
  }

  return (
    <div className="flex flex-wrap items-center gap-2 py-1 pr-2" style={{ paddingLeft: 8 + depth * 18 }}>
      <div role="group" aria-label="New item's type" className="flex gap-1">
        {types.map((t) => (
          <button
            key={t}
            type="button"
            aria-pressed={type === t}
            onClick={() => setType(t)}
            className={`min-h-11 rounded-md px-1.5 py-0.5 text-[0.6875rem] md:min-h-0 ${
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
        aria-label={isSibling ? "New sibling title" : `New ${typeLabel(type)} title`}
        className="h-7"
        onChange={(e) => setTitle(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            e.preventDefault();
            save();
          }
          // Tab leaves the field as it does anywhere else; the type is chosen by its buttons,
          // before the field. Tab used to change the type, so the keyboard couldn't reach Add or
          // anything past it (code review CR95).
          if (e.key === "Escape") {
            e.preventDefault();
            onDone();
          }
        }}
      />
      <Button size="xs" onClick={save} disabled={!title.trim() || adding.pending}>
        Add
      </Button>
      {adding.error ? (
        <p role="alert" className="basis-full text-xs text-destructive">
          {adding.error}
        </p>
      ) : null}
    </div>
  );
}

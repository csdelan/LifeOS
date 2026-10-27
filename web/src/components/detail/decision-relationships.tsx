import { useId } from "react";
import { PlusIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { TypeBadge } from "@/components/primitives/type-badge";
import { TypeIcon } from "@/components/primitives/type-icon";
import type { CreateOpts } from "@/components/create/create-context";
import { RESULTING_WORK_TYPES, decisionGroups, type LinkedItem } from "@/lib/decisions";
import { typeLabel } from "@/lib/production-ui-types";
import type { RelationEdge } from "@/lib/production-ui-types";

/**
 * A Decision's own actions (GEN-13): create work that carries it out, or a replacement
 * Decision that reverses it. Each opens the normal New form with the link shown before Save.
 */
export function DecisionActions({
  decision,
  openNew,
}: {
  decision: LinkedItem;
  openNew: (opts: CreateOpts) => void;
}) {
  return (
    <>
      <DropdownMenu modal={false}>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="xs" className="-my-1 text-muted-foreground">
            <PlusIcon aria-hidden /> Resulting work
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" className="w-48">
          <DropdownMenuLabel>Create resulting work</DropdownMenuLabel>
          {RESULTING_WORK_TYPES.map((type) => (
            <DropdownMenuItem
              key={type}
              onSelect={() => openNew({ type, link: { kind: "resulting-work", decision } })}
            >
              <TypeIcon type={type} /> {typeLabel(type)}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
      <Button
        variant="ghost"
        size="xs"
        className="-my-1 text-muted-foreground"
        title="Create a replacement Decision. This one becomes Cancelled; its conclusion is kept."
        onClick={() => openNew({ type: "Decision", link: { kind: "replacement", decision } })}
      >
        <PlusIcon aria-hidden /> Replacement
      </Button>
    </>
  );
}

/** A new Decision from another item's detail, with that item as its Context (GEN-13). */
export function RecordDecisionAction({
  item,
  openNew,
}: {
  item: LinkedItem;
  openNew: (opts: CreateOpts) => void;
}) {
  return (
    <Button
      variant="ghost"
      size="xs"
      className="-my-1 text-muted-foreground"
      title="A Decision this led to, with this as its Context"
      onClick={() => openNew({ type: "Decision", link: { kind: "context", item } })}
    >
      <TypeIcon type="Decision" /> Record decision
    </Button>
  );
}

/**
 * A Decision's links as two groups, Context and Resulting work, plus Replaces / Replaced by
 * when it reversed or was reversed, and anything else linked in words. Not Parents and
 * Children: a Decision sits outside that hierarchy.
 */
export function DecisionRelationships({
  rels,
  onOpenSubject,
}: {
  rels: { parents: RelationEdge[]; children: RelationEdge[] } | undefined;
  onOpenSubject?: (id: string) => void;
}) {
  const groups = decisionGroups(rels ?? { parents: [], children: [] });
  return (
    <>
      <RelationGroup
        title="Context"
        hint="What prompted or informed this decision."
        rows={groups.context}
        empty="Nothing yet. A Decision recorded from a Problem, Idea or Goal lists it here, or add one below."
        onOpenSubject={onOpenSubject}
      />
      <RelationGroup
        title="Resulting work"
        hint="The work that carries it out. Finishing it doesn't move this Decision's status: you do."
        rows={groups.resultingWork}
        empty="None yet. Create some with Resulting work, or add existing work below."
        onOpenSubject={onOpenSubject}
      />
      {groups.replaces.length > 0 ? (
        <RelationGroup
          title="Replaces"
          hint="The earlier Decision this one reversed."
          rows={groups.replaces}
          onOpenSubject={onOpenSubject}
        />
      ) : null}
      {groups.replacedBy.length > 0 ? (
        <RelationGroup
          title="Replaced by"
          hint="The Decision that reversed this one."
          rows={groups.replacedBy}
          onOpenSubject={onOpenSubject}
        />
      ) : null}
      {groups.other.length > 0 ? (
        <RelationGroup
          title="Other links"
          rows={groups.other.map((o) => o.edge)}
          labels={groups.other.map((o) => o.label)}
          onOpenSubject={onOpenSubject}
        />
      ) : null}
    </>
  );
}

function RelationGroup({
  title,
  hint,
  rows,
  labels,
  empty,
  onOpenSubject,
}: {
  title: string;
  hint?: string;
  rows: RelationEdge[];
  /** A word per row saying how it's linked, for links outside the groups. */
  labels?: string[];
  empty?: string;
  onOpenSubject?: (id: string) => void;
}) {
  const headingId = useId();
  return (
    <section aria-labelledby={headingId} className="rounded-xl bg-muted/40 p-3">
      <h3 id={headingId} className="type-scale-section text-muted-foreground">
        {title}
      </h3>
      {hint ? <p className="mt-1 text-xs text-muted-foreground">{hint}</p> : null}
      {rows.length > 0 ? (
        <ul className="mt-2 space-y-1">
          {rows.map((e, i) => (
            <li
              key={`${e.relation}-${e.subjectId}-${i}`}
              className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-sm"
            >
              {labels ? <span className="text-[0.6875rem] text-muted-foreground">{labels[i]}</span> : null}
              <TypeBadge type={e.type} />
              {onOpenSubject ? (
                <Button
                  variant="link"
                  size="sm"
                  className="h-auto p-0 text-left whitespace-normal text-foreground"
                  onClick={() => onOpenSubject(e.subjectId)}
                >
                  {e.title}
                </Button>
              ) : (
                <span>{e.title}</span>
              )}
            </li>
          ))}
        </ul>
      ) : empty ? (
        <p className="mt-2 text-sm text-muted-foreground">{empty}</p>
      ) : null}
    </section>
  );
}

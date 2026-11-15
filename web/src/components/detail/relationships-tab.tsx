import { useCallback, useState } from "react";
import { ArrowDownIcon, ArrowUpIcon, Link2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { SubjectSearchCombobox } from "@/components/subject-search-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DetailSection } from "@/components/primitives/detail-section";
import { TypeBadge } from "@/components/primitives/type-badge";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { DecisionRelationships } from "@/components/detail/decision-relationships";
import { DECISION_LINK_GROUPS, decisionLinkWrite, type DecisionLinkGroup } from "@/lib/decisions";
import { useRelations, useWrites } from "@/lib/queries";
import { LINK_TARGET_TYPES, typeLabel } from "@/lib/production-ui-types";
import type { Relation, SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { useWriteAction } from "@/lib/write-action";

export function RelationshipsTab({
  subjectId,
  type,
  onOpenSubject,
}: {
  subjectId: string;
  type: SubjectType;
  onOpenSubject?: (id: string) => void;
}) {
  const { data, isLoading } = useRelations(subjectId);
  const [rel, setRel] = useState<Relation>("serves");
  // A Decision adds to its groups instead of naming a relation (GEN-13).
  const decision = type === "Decision";
  const [group, setGroup] = useState<DecisionLinkGroup>("context");
  const [targetType, setTargetType] = useState<SubjectType | "any">("any");
  const [includeArchived, setIncludeArchived] = useState(false);
  // The picker searches on the server (code review CR107), so it holds the item chosen.
  const [picked, setPicked] = useState<SubjectListItem | null>(null);
  const writes = useWrites();
  const reported = useWriteAction({ report: "toast" });

  // A choice the filters no longer allow (another type, archived hidden) isn't the one Add links.
  const chosen =
    picked &&
    picked.id !== subjectId &&
    (targetType === "any" || picked.type === targetType) &&
    (includeArchived || !picked.archived)
      ? picked
      : null;
  const target = chosen?.id ?? "";

  const typed = targetType !== "any";
  const targetLabel = useCallback(
    (s: SubjectListItem) => linkTargetLabel(s, typed, includeArchived),
    [typed, includeArchived],
  );

  if (isLoading) return <ListSkeleton rows={4} />;

  const subjectPlaceholder = typed
    ? `Choose ${typeLabel(targetType)}…`
    : "Choose an existing item…";

  const grouped = groupByType(data?.children ?? []);

  function addLink() {
    const edge = decision ? decisionLinkWrite(group, subjectId, target) : { from: subjectId, relation: rel, to: target };
    void reported.run(() => writes.link(edge.from, edge.relation, edge.to));
    setPicked(null);
  }

  return (
    <div className="space-y-4">
      {decision ? (
        <DecisionRelationships rels={data} onOpenSubject={onOpenSubject} />
      ) : (
        <>
          <DetailSection title="Parents" icon={ArrowUpIcon}>
            <p className="text-xs text-muted-foreground">Alignment edges — this is structure, not tagging.</p>
            {data?.parents.length ? (
              <ul className="space-y-1.5">
                {data.parents.map((e) => (
                  <li key={`${e.relation}-${e.subjectId}`} className="flex items-center gap-2 text-sm">
                    <TypeBadge type={e.type} />
                    <span>{e.title}</span>
                    <span className="text-[0.6875rem] text-muted-foreground">{e.relation}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">No parents — unaligned for now.</p>
            )}
          </DetailSection>
          <DetailSection title="Children" icon={ArrowDownIcon}>
            {data?.children.length ? (
              <div className="space-y-3">
                {Object.entries(grouped).map(([type, rows]) => (
                  <div key={type}>
                    <p className="mb-1 text-[0.8125rem] text-muted-foreground">{typeLabel(type as SubjectType)}</p>
                    <ul className="space-y-1.5">
                      {rows.map((e) => (
                        <li key={`${e.relation}-${e.subjectId}`} className="flex items-center gap-2 text-sm">
                          <TypeBadge type={e.type} />
                          <span>{e.title}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">No children yet.</p>
            )}
          </DetailSection>
        </>
      )}
      <DetailSection title="Add relationship" icon={Link2Icon}>
        <p className="text-xs text-muted-foreground">
          {decision
            ? "Add an existing item to Context (what prompted it), Resulting work (what carries it out) or Replaces (an earlier Decision this one reversed). "
            : null}
          Choose a type to filter, then pick an existing item. Archived items are
          hidden unless you check Archived. This is a link between two items, not a
          tag.
        </p>
        <div className="flex flex-wrap items-center gap-2">
          {decision ? (
            <>
              <span className="text-xs text-muted-foreground">Add to</span>
              <Select value={group} onValueChange={(v) => setGroup(v as DecisionLinkGroup)}>
                <SelectTrigger className="w-40" aria-label="Group">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {DECISION_LINK_GROUPS.map((g) => (
                    <SelectItem key={g.value} value={g.value}>
                      {g.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </>
          ) : (
            <>
              <span className="text-xs text-muted-foreground">This item</span>
              <Select value={rel} onValueChange={(v) => setRel(v as Relation)}>
                <SelectTrigger className="w-32" aria-label="Relation">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="serves">serves</SelectItem>
                  <SelectItem value="results_in">results_in</SelectItem>
                  <SelectItem value="supersedes">supersedes</SelectItem>
                </SelectContent>
              </Select>
            </>
          )}
          <span className="text-xs text-muted-foreground">this existing</span>
          <Select
            value={targetType}
            onValueChange={(v) => setTargetType(v as SubjectType | "any")}
          >
            <SelectTrigger className="w-44" aria-label="Target type">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="any">item</SelectItem>
              {LINK_TARGET_TYPES.map((t) => (
                <SelectItem key={t} value={t}>
                  {typeLabel(t)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <SubjectSearchCombobox
            className="min-w-48 flex-1"
            value={chosen}
            onChange={setPicked}
            types={typed ? [targetType] : undefined}
            includeArchived={includeArchived}
            exclude={subjectId}
            ariaLabel="Target subject"
            placeholder={subjectPlaceholder}
            label={targetLabel}
          />
          <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <Checkbox
              checked={includeArchived}
              onCheckedChange={(v) => setIncludeArchived(v === true)}
            />
            Archived
          </label>
          <Button size="sm" disabled={!target} onClick={addLink}>
            Link
          </Button>
        </div>
      </DetailSection>
    </div>
  );
}
function groupByType<T extends { type: SubjectType }>(rows: T[]): Record<string, T[]> {
  const out: Record<string, T[]> = {};
  for (const row of rows) {
    (out[row.type] ??= []).push(row);
  }
  return out;
}
function linkTargetLabel(item: SubjectListItem, typed: boolean, includeArchived: boolean) {
  const label = typed ? item.title : `${typeLabel(item.type)}: ${item.title}`;
  return includeArchived && item.archived ? `${label} (archived)` : label;
}

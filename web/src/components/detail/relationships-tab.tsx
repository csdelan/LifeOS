import { useCallback, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { SubjectCombobox } from "@/components/subject-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TypeBadge } from "@/components/primitives/type-badge";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { DecisionRelationships } from "@/components/detail/decision-relationships";
import { DECISION_LINK_GROUPS, decisionLinkWrite, type DecisionLinkGroup } from "@/lib/decisions";
import { useRelations, useSubjects, useWrites } from "@/lib/queries";
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
  const [to, setTo] = useState("");
  const { data: subjects } = useSubjects(
    targetType === "any" ? undefined : targetType,
    includeArchived,
  );
  const writes = useWrites();
  const reported = useWriteAction({ report: "toast" });

  const options = useMemo(
    () => (subjects ?? []).filter((s) => s.id !== subjectId),
    [subjects, subjectId],
  );

  // A choice the filters no longer list (another type, archived hidden) isn't the one Add links.
  const target = to && options.some((s) => s.id === to) ? to : "";

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
    setTo("");
  }

  return (
    <div className="space-y-6">
      {decision ? (
        <DecisionRelationships rels={data} onOpenSubject={onOpenSubject} />
      ) : (
        <>
          <section className="rounded-xl bg-muted/40 p-3">
            <h3 className="type-scale-section text-muted-foreground">Parents</h3>
            <p className="mt-1 mb-2 text-xs text-muted-foreground">
              Alignment edges — this is structure, not tagging.
            </p>
            {data?.parents.length ? (
              <ul className="space-y-1">
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
          </section>
          <section className="rounded-xl bg-muted/40 p-3">
            <h3 className="type-scale-section text-muted-foreground">Children</h3>
            {data?.children.length ? (
              <div className="mt-2 space-y-3">
                {Object.entries(grouped).map(([type, rows]) => (
                  <div key={type}>
                    <p className="mb-1 text-[0.6875rem] font-medium text-muted-foreground">
                      {typeLabel(type as SubjectType)}
                    </p>
                    <ul className="space-y-1">
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
              <p className="mt-2 text-sm text-muted-foreground">No children yet.</p>
            )}
          </section>
        </>
      )}
      <section>
        <h3 className="type-scale-section text-muted-foreground">Add relationship</h3>
        <p className="mt-1 text-xs text-muted-foreground">
          {decision
            ? "Add an existing item to Context (what prompted it), Resulting work (what carries it out) or Replaces (an earlier Decision this one reversed). "
            : null}
          Choose a type to filter, then pick an existing item. Archived items are
          hidden unless you check Archived. This is a link between two items, not a
          tag.
        </p>
        <div className="mt-2 flex flex-wrap items-center gap-2">
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
          <SubjectCombobox
            className="min-w-48 flex-1"
            options={options}
            value={target}
            onChange={setTo}
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
      </section>
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

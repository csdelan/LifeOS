import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import type { Resolver } from "react-hook-form";
import { toast } from "sonner";
import {
  BookOpenIcon,
  HistoryIcon,
  LayoutListIcon,
  Link2Icon,
  PlusIcon,
  TagIcon,
  XIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Separator } from "@/components/ui/separator";
import { DateChip } from "@/components/primitives/date-chip";
import { EmptyState } from "@/components/primitives/empty-state";
import { StatusPill } from "@/components/primitives/status-pill";
import { Tag } from "@/components/primitives/tag";
import { TypeBadge } from "@/components/primitives/type-badge";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { SubjectTypeFields } from "@/components/create/subject-type-fields";
import { useCreateActions } from "@/components/create/create-context";
import { StreakGrid } from "@/components/habits/streak-grid";
import { JournalEditor, looksLikeHtml } from "@/components/journal/journal-editor";
import {
  useHabits,
  useHistory,
  useJournal,
  useListItem,
  useOccurrences,
  useRelations,
  useSubject,
  useSubjectTags,
  useSubjects,
  useTagUniverse,
  useWrites,
} from "@/lib/queries";
import {
  LINK_TARGET_TYPES,
  childTypesFor,
  defaultStatus,
  inferChildRelation,
  typeLabel,
} from "@/lib/production-ui-types";
import type { Relation, SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { formatTimestamp } from "@/lib/dates";
import { parseRecurrence } from "@/lib/recurrence";
import {
  attrsToWrite,
  emptyFormValues,
  formConfig,
  formValuesFromDetail,
  selectedParentIds,
  subjectFormSchema,
  type SubjectFormValues,
} from "@/lib/subject-forms";

export function SubjectDetail({
  subjectId,
  onDirtyChange,
  onClose,
}: {
  subjectId: string;
  onDirtyChange?: (dirty: boolean) => void;
  onClose?: () => void;
}) {
  const { data, isLoading, isError } = useSubject(subjectId);
  const { data: list } = useListItem(subjectId);
  const { data: rels } = useRelations(subjectId);
  const { data: tags } = useSubjectTags(subjectId);
  const { data: habits } = useHabits();
  const { data: occurrences } = useOccurrences(subjectId);
  const [editing, setEditing] = useState(false);
  const writes = useWrites();
  const { openNew } = useCreateActions();

  const habit = useMemo(
    () => (habits ?? []).find((h) => h.id === subjectId),
    [habits, subjectId],
  );

  const form = useForm<SubjectFormValues>({
    resolver: zodResolver(subjectFormSchema) as Resolver<SubjectFormValues>,
    defaultValues: emptyFormValues("Task", "edit"),
  });

  useEffect(() => {
    if (!data || editing) return;
    const valueParent = (rels?.parents ?? []).find((p) => p.type === "Value");
    const habitParents = (rels?.parents ?? [])
      .filter((p) => p.type === "Goal" || p.type === "Value")
      .map((p) => p.subjectId);
    form.reset(
      formValuesFromDetail(data, {
        list,
        habit,
        parentId: valueParent?.subjectId,
        parents: habitParents,
        tags: (tags ?? []).join(", "),
      }),
    );
  }, [data, list, habit, rels, tags, editing, form]);

  const dirty = editing && form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  if (isLoading) {
    return (
      <div className="p-5">
        <ListSkeleton rows={6} />
      </div>
    );
  }
  if (isError) {
    return (
      <EmptyState
        tone="error"
        title="Couldn't load this subject"
        description="The reader failed. Retry from the Map."
      />
    );
  }
  if (!data) {
    return (
      <EmptyState
        title="Select something"
        description="Detail opens here — not a separate page."
      />
    );
  }

  const subject = data;
  const cfg = formConfig(subject.type);
  const childTypes = childTypesFor(subject.type);

  async function save(values: SubjectFormValues) {
    if (subject.type === "Goal" && values.status === "Active" && !values.attrs.target_date) {
      toast.error("A Goal needs a target date before it can be Active.");
      return;
    }
    if (subject.type === "Habit" && selectedParentIds(values).length < 1) {
      toast.error("A Habit needs at least one Goal or Identity Statement.");
      return;
    }
    const attrs = attrsToWrite(subject.type, values);
    if (Object.keys(attrs).length > 0) {
      await writes.setAttributes(subject.id, attrs);
    }
    const nextStatus = values.status || defaultStatus(subject.type);
    if (nextStatus && nextStatus !== (subject.status || defaultStatus(subject.type))) {
      await writes.setStatus(subject.id, nextStatus);
    }
    if (subject.type === "Habit" && values.attrs.recurrence) {
      await writes.recur(subject.id, parseRecurrence(values.attrs.recurrence));
    }
    if (subject.type === "Habit") {
      const existing = new Set((rels?.parents ?? []).map((p) => p.subjectId));
      for (const pid of selectedParentIds(values)) {
        if (existing.has(pid)) continue;
        await writes.link(subject.id, "serves", pid);
      }
    }
    if (subject.type === "Goal" && values.parent) {
      const existing = new Set((rels?.parents ?? []).map((p) => p.subjectId));
      if (!existing.has(values.parent)) {
        await writes.link(subject.id, inferChildRelation("Goal", "Value") ?? "serves", values.parent);
      }
    }
    if (subject.type === "Commitment" && values.person) {
      await writes.involve(subject.id, values.person, "involves");
    }
    setEditing(false);
  }

  function cancel() {
    if (!data) return;
    const valueParent = (rels?.parents ?? []).find((p) => p.type === "Value");
    const habitParents = (rels?.parents ?? [])
      .filter((p) => p.type === "Goal" || p.type === "Value")
      .map((p) => p.subjectId);
    form.reset(
      formValuesFromDetail(data, {
        list,
        habit,
        parentId: valueParent?.subjectId,
        parents: habitParents,
        tags: (tags ?? []).join(", "),
      }),
    );
    setEditing(false);
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="shrink-0 border-b px-5 py-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <TypeBadge type={subject.type} />
            {/* TODO: title rename is not in Application — bsk set only patches attributes. */}
            <h2 className="mt-1 font-heading text-2xl leading-tight">{subject.title}</h2>
            <div className="mt-2 flex flex-wrap items-center gap-1.5">
              <StatusPill status={subject.status || defaultStatus(subject.type) || null} />
              <DateChip
                date={
                  subject.type === "Goal" || subject.type === "Project"
                    ? subject.targetDate
                    : subject.due
                }
                kind={subject.type === "Goal" || subject.type === "Project" ? "target" : "due"}
              />
              {subject.areaName ? (
                <span className="text-[0.75rem] text-muted-foreground">{subject.areaName}</span>
              ) : null}
              {subject.archived ? (
                <span className="text-[0.75rem] text-muted-foreground">Archived</span>
              ) : null}
            </div>
          </div>
          <div className="flex shrink-0 flex-wrap justify-end gap-1.5">
            {childTypes.length > 0 ? (
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  openNew({
                    parent: { id: subject.id, title: subject.title, type: subject.type },
                    type: childTypes[0],
                  })
                }
              >
                <PlusIcon /> Child
              </Button>
            ) : null}
            {editing ? (
              <>
                <Button variant="outline" size="sm" onClick={cancel}>
                  Cancel
                </Button>
                <Button size="sm" onClick={() => void form.handleSubmit((v) => void save(v))()} disabled={!dirty}>
                  Save
                </Button>
              </>
            ) : (
              <Button variant="outline" size="sm" onClick={() => setEditing(true)}>
                Edit
              </Button>
            )}
            {onClose ? (
              <Button
                variant="destructive"
                size="icon-sm"
                onClick={onClose}
                aria-label="Close panel"
              >
                <XIcon />
              </Button>
            ) : null}
          </div>
        </div>
      </header>

      <Tabs defaultValue="overview" className="flex min-h-0 flex-1 flex-col gap-0">
        <div className="shrink-0 border-b px-5">
          <TabsList variant="line" className="w-full justify-start">
            <TabsTrigger value="overview">
              <LayoutListIcon /> Overview
            </TabsTrigger>
            <TabsTrigger value="relationships">
              <Link2Icon /> Relationships
            </TabsTrigger>
            <TabsTrigger value="tags">
              <TagIcon /> Tags
            </TabsTrigger>
            <TabsTrigger value="journal">
              <BookOpenIcon /> Journal
            </TabsTrigger>
            <TabsTrigger value="history">
              <HistoryIcon /> History
            </TabsTrigger>
          </TabsList>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
          <TabsContent value="overview">
            <div className="space-y-5">
              <SubjectTypeFields form={form} editing={editing} />
              {subject.type === "Habit" ? (
                <StreakGrid
                  occurrences={occurrences ?? []}
                  recurrence={habit?.recurrence ?? form.watch("attrs.recurrence")}
                  startDate={habit?.startDate}
                  endDate={habit?.endDate}
                  allowsPartial={habit?.allowsPartial ?? true}
                  onRecord={(date, state, note) =>
                    void writes.adhere(subject.id, state, { on: date, note })
                  }
                />
              ) : null}
              <Field label="URN">
                <code className="text-[0.75rem] text-muted-foreground">{subject.urn}</code>
              </Field>
              {cfg.canArchive ? (
                <>
                  <Separator />
                  <div>
                    <p className="type-scale-section text-muted-foreground">Archive</p>
                    <p className="mt-1 text-sm text-muted-foreground">
                      LifeOS archives, never deletes.
                    </p>
                    <div className="mt-2">
                      {subject.archived ? (
                        <Button variant="outline" size="sm" onClick={() => void writes.restore(subject.id)}>
                          Restore
                        </Button>
                      ) : (
                        <Button variant="outline" size="sm" onClick={() => void writes.archive(subject.id)}>
                          Archive
                        </Button>
                      )}
                    </div>
                  </div>
                </>
              ) : (
                <p className="text-sm text-muted-foreground">
                  Areas are permanent — they are not archived or deleted.
                </p>
              )}
            </div>
          </TabsContent>
          <TabsContent value="relationships">
            <RelationshipsTab subjectId={subject.id} />
          </TabsContent>
          <TabsContent value="tags">
            <TagsTab subjectId={subject.id} />
          </TabsContent>
          <TabsContent value="journal">
            <JournalTab subjectId={subject.id} />
          </TabsContent>
          <TabsContent value="history">
            <HistoryTab subjectId={subject.id} />
          </TabsContent>
        </div>
      </Tabs>
    </div>
  );
}

function RelationshipsTab({ subjectId }: { subjectId: string }) {
  const { data, isLoading } = useRelations(subjectId);
  const [rel, setRel] = useState<Relation>("serves");
  const [targetType, setTargetType] = useState<SubjectType | "any">("any");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [to, setTo] = useState("");
  const { data: subjects } = useSubjects(
    targetType === "any" ? undefined : targetType,
    includeArchived,
  );
  const writes = useWrites();

  const options = useMemo(
    () => (subjects ?? []).filter((s) => s.id !== subjectId),
    [subjects, subjectId],
  );

  useEffect(() => {
    if (to && !options.some((s) => s.id === to)) setTo("");
  }, [to, options]);

  if (isLoading) return <ListSkeleton rows={4} />;

  const typed = targetType !== "any";
  const subjectPlaceholder = typed
    ? `Choose ${typeLabel(targetType)}…`
    : "Choose an existing item…";

  const grouped = groupByType(data?.children ?? []);

  return (
    <div className="space-y-6">
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
      <section>
        <h3 className="type-scale-section text-muted-foreground">Add relationship</h3>
        <p className="mt-1 text-xs text-muted-foreground">
          Choose a type to filter, then pick an existing item. Archived items are
          hidden unless you check Archived. This is a link between two items, not a
          tag.
        </p>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <span className="text-xs text-muted-foreground">This item</span>
          <Select value={rel} onValueChange={(v) => setRel(v as Relation)}>
            <SelectTrigger className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="serves">serves</SelectItem>
              <SelectItem value="results_in">results_in</SelectItem>
              <SelectItem value="supersedes">supersedes</SelectItem>
            </SelectContent>
          </Select>
          <span className="text-xs text-muted-foreground">this existing</span>
          <Select
            value={targetType}
            onValueChange={(v) => setTargetType(v as SubjectType | "any")}
          >
            <SelectTrigger className="w-44">
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
          <Select value={to} onValueChange={setTo}>
            <SelectTrigger className="min-w-48 flex-1">
              <SelectValue placeholder={subjectPlaceholder} />
            </SelectTrigger>
            <SelectContent>
              {options.length === 0 ? (
                <div className="px-2 py-1.5 text-xs text-muted-foreground">
                  No matching items.
                </div>
              ) : (
                options.map((s) => (
                  <SelectItem key={s.id} value={s.id}>
                    {linkTargetLabel(s, typed, includeArchived)}
                  </SelectItem>
                ))
              )}
            </SelectContent>
          </Select>
          <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <Checkbox
              checked={includeArchived}
              onCheckedChange={(v) => setIncludeArchived(v === true)}
            />
            Archived
          </label>
          <Button
            size="sm"
            disabled={!to}
            onClick={() => {
              void writes.link(subjectId, rel, to);
              setTo("");
            }}
          >
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

function TagsTab({ subjectId }: { subjectId: string }) {
  const { data: tags } = useSubjectTags(subjectId);
  const { data: universe } = useTagUniverse();
  const writes = useWrites();
  const [draft, setDraft] = useState("");

  return (
    <div className="space-y-4 rounded-xl border border-dashed border-border p-3">
      <div>
        <h3 className="type-scale-section text-muted-foreground">Tags</h3>
        <p className="mt-1 text-xs text-muted-foreground">
          Classification, not structure. Relationships live on the other tab (GEN-1).
        </p>
      </div>
      <div className="flex flex-wrap gap-1.5">
        {(tags ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">No tags yet.</p>
        ) : (
          (tags ?? []).map((t) => (
            <Tag key={t} onRemove={() => void writes.tag(subjectId, { remove: [t] })}>
              {t}
            </Tag>
          ))
        )}
      </div>
      <form
        className="flex gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          if (!draft.trim()) return;
          void writes.tag(subjectId, { add: [draft.trim()] });
          setDraft("");
        }}
      >
        <Input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Add a tag"
          list={`tag-universe-${subjectId}`}
        />
        <datalist id={`tag-universe-${subjectId}`}>
          {(universe ?? []).map((t) => (
            <option key={t.tag} value={t.tag} />
          ))}
        </datalist>
        <Button type="submit" size="sm">
          Add
        </Button>
      </form>
    </div>
  );
}

function JournalTab({ subjectId }: { subjectId: string }) {
  const { data, isLoading } = useJournal(subjectId);
  const writes = useWrites();
  const [composing, setComposing] = useState(false);

  return (
    <div className="space-y-4">
      {isLoading ? <ListSkeleton rows={3} /> : null}
      {!isLoading && (data ?? []).length === 0 ? (
        <EmptyState title="No journal yet" description="Entries are append-only. A correction is a new entry." />
      ) : !isLoading ? (
        <ol className="space-y-3">
          {[...(data ?? [])].reverse().map((e) => (
            <li key={e.eventId} className="rounded-lg bg-muted/50 px-3 py-2">
              <p className="text-[0.6875rem] text-muted-foreground">
                {formatTimestamp(e.occurredAt)}
              </p>
              {looksLikeHtml(e.content) ? (
                <div
                  className="journal-prose mt-1 text-sm"
                  dangerouslySetInnerHTML={{ __html: e.content }}
                />
              ) : (
                <p className="mt-1 whitespace-pre-wrap text-sm">{e.content}</p>
              )}
            </li>
          ))}
        </ol>
      ) : null}
      <Separator />
      {composing ? (
        <JournalEditor
          onAppend={(html) => {
            void writes.appendJournal(subjectId, html);
            setComposing(false);
          }}
        />
      ) : (
        <Button variant="outline" size="sm" onClick={() => setComposing(true)}>
          Append
        </Button>
      )}
    </div>
  );
}

function HistoryTab({ subjectId }: { subjectId: string }) {
  const { data } = useHistory(subjectId);
  if (!data?.length) {
    return (
      <EmptyState
        title="No status history"
        description="Status moves only by event. Changes will appear here."
      />
    );
  }
  return (
    <ol className="relative ml-2 space-y-0 border-l border-border">
      {data.map((e) => (
        <li key={e.id} className="relative py-2 pl-4">
          <span className="absolute top-3.5 -left-1 size-2 rounded-full bg-primary" />
          <StatusPill status={e.status} />
          <p className="mt-1 text-[0.6875rem] text-muted-foreground">
            {formatTimestamp(e.occurredAt)}
          </p>
        </li>
      ))}
    </ol>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <p className="type-scale-section text-muted-foreground">{label}</p>
      {children}
    </div>
  );
}

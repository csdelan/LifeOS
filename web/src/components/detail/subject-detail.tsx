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
  PaperclipIcon,
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
import { TagInput } from "@/components/primitives/tag-input";
import { TypeBadge } from "@/components/primitives/type-badge";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { SubjectTypeFields } from "@/components/create/subject-type-fields";
import { useCreateActions } from "@/components/create/create-context";
import { AttachmentChip } from "@/components/artifacts/attachment-chip";
import { StreakGrid } from "@/components/habits/streak-grid";
import { ReadyToEstablish } from "@/components/habits/establish-dialog";
import { BountyChip } from "@/components/rewards/bounty-chip";
import { BountySection } from "@/components/rewards/bounty-editor";
import { TaskTimerPanel } from "@/components/timer/task-timer";
import { JournalEditor, looksLikeHtml } from "@/components/journal/journal-editor";
import {
  useHabits,
  useHistory,
  useJournal,
  useListItem,
  useOccurrences,
  useRelations,
  useSubject,
  useSubjectFiles,
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
  isTerminal,
  shortIdOf,
  typeLabel,
} from "@/lib/production-ui-types";
import type { HabitRow, Relation, SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { formatTimestamp } from "@/lib/dates";
import { bountyDirty, bountyProblem, bountyWrites, type BountyDraft } from "@/lib/bounty";
import { establishmentProgress, habitStage, isReadyToEstablish } from "@/lib/establish";
import { splitTags } from "@/lib/tag-suggest";
import { parseRecurrence } from "@/lib/recurrence";
import {
  attrsToWrite,
  emptyFormValues,
  formConfig,
  formValuesFromDetail,
  isDetailDirty,
  parseAttrBag,
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
  const { data: files } = useSubjectFiles(subjectId);
  const { data: habits } = useHabits();
  const { data: occurrences } = useOccurrences(subjectId);
  const [editing, setEditing] = useState(false);
  const [bountyDraft, setBountyDraft] = useState<BountyDraft>({});
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

  // A new subject starts with nothing typed into its bounty.
  useEffect(() => {
    setBountyDraft({});
  }, [subjectId]);

  const bountyLines = data?.bounty ?? [];
  const dirty = isDetailDirty(editing, form.formState.isDirty || bountyDirty(bountyLines, bountyDraft));
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
    const bountyIssue = bountyProblem(bountyDraft);
    if (bountyIssue) {
      toast.error(bountyIssue);
      return;
    }
    // Rename first, so a refused rename (two Problems can't share a title) stops the
    // save before any other field changes — the same order the Pilot's Edit uses.
    const nextTitle = values.title.trim().split(/\s+/).join(" ");
    if (nextTitle && nextTitle !== subject.title) {
      try {
        await writes.rename(subject.id, nextTitle);
      } catch (err) {
        toast.error(err instanceof Error && err.message ? err.message : "Could not rename.");
        return;
      }
    }
    const attrs = attrsToWrite(subject.type, values);
    if (Object.keys(attrs).length > 0) {
      await writes.setAttributes(subject.id, attrs);
    }
    // Price before the status moves, so completing in the same save pays the new bounty.
    for (const req of bountyWrites(subject.id, bountyLines, bountyDraft)) {
      await writes.setBounty(req);
    }
    setBountyDraft({});
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
    setBountyDraft({});
    setEditing(false);
  }

  const headline = bountyLines[0];

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="shrink-0 border-b px-5 py-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <TypeBadge type={subject.type} />
            {editing ? (
              <div className="mt-1 space-y-1">
                <Input
                  aria-label={cfg.titleLabel}
                  aria-invalid={form.formState.errors.title ? true : undefined}
                  className="h-auto py-0.5 font-heading text-2xl leading-tight md:text-2xl"
                  {...form.register("title")}
                />
                {form.formState.errors.title ? (
                  <p className="text-xs text-destructive">{form.formState.errors.title.message}</p>
                ) : null}
              </div>
            ) : (
              <h2 className="mt-1 font-heading text-2xl leading-tight">{subject.title}</h2>
            )}
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
              {headline && !isTerminal(subject.status) ? (
                <BountyChip
                  amounts={headline.amounts}
                  suffix={headline.trigger === "followed" ? "a day" : undefined}
                />
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
        {subject.type === "Task" ? (
          <TaskTimerPanel
            taskId={subject.id}
            estimate={parseAttrBag(subject.attributes).estimated_duration}
            className="mt-3"
          />
        ) : null}
      </header>

      <Tabs defaultValue="overview" className="flex min-h-0 flex-1 flex-col gap-0">
        <div className="shrink-0 border-b px-5">
          <TabsList variant="line" className="w-full justify-start overflow-x-auto">
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
              {subject.type === "Habit" && habit ? <EstablishmentPanel habit={habit} /> : null}
              <BountySection
                key={`${subject.id}-${editing}`}
                idPrefix={`bounty-${subject.id}`}
                type={subject.type}
                lines={bountyLines}
                draft={bountyDraft}
                onChange={setBountyDraft}
                editing={editing}
              />
              {(files ?? []).length > 0 ? (
                <section>
                  <p className="type-scale-section text-muted-foreground">
                    <span className="inline-flex items-center gap-1.5">
                      <PaperclipIcon className="size-3.5" aria-hidden />
                      Files
                    </span>
                  </p>
                  <p className="mt-1 text-xs text-muted-foreground">
                    Related documents and kept audio. Open plays the managed copy.
                  </p>
                  <ul className="mt-2 space-y-2">
                    {(files ?? []).map((file) => (
                      <li key={file.id}>
                        <AttachmentChip artifact={file} />
                      </li>
                    ))}
                  </ul>
                </section>
              ) : null}
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
              <Field label="ID">
                <code className="text-[0.75rem] text-muted-foreground">{shortIdOf(subject.urn)}</code>
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
            <SelectTrigger className="w-32" aria-label="Relation">
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
          <Select value={to} onValueChange={setTo}>
            <SelectTrigger className="min-w-48 flex-1" aria-label="Target subject">
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
  const [renaming, setRenaming] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState("");
  const usedOn = (universe ?? []).find((u) => u.tag === renaming)?.itemCount;

  function addTags(add: string[]) {
    if (add.length === 0) return;
    void writes.tag(subjectId, { add });
    setDraft("");
  }

  async function submitRename() {
    if (!renaming) return;
    const next = renameDraft.trim();
    if (!next || next.toLowerCase() === renaming) {
      setRenaming(null);
      return;
    }
    try {
      await writes.renameTag(renaming, next);
      setRenaming(null);
    } catch (err) {
      toast.error(err instanceof Error && err.message ? err.message : "Could not rename the tag.");
    }
  }

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
            <Tag
              key={t}
              onRename={() => {
                setRenaming(t);
                setRenameDraft(t);
              }}
              onRemove={() => void writes.tag(subjectId, { remove: [t] })}
            >
              {t}
            </Tag>
          ))
        )}
      </div>
      {renaming ? (
        <form
          className="space-y-2 rounded-lg border border-border p-3"
          onSubmit={(e) => {
            e.preventDefault();
            void submitRename();
          }}
        >
          <p className="text-sm">
            Rename <span className="font-medium">#{renaming}</span> everywhere it's used
            {usedOn ? ` (${usedOn} item${usedOn === 1 ? "" : "s"})` : ""}.
          </p>
          <div className="flex gap-2">
            <TagInput
              autoFocus
              value={renameDraft}
              onValueChange={setRenameDraft}
              exclude={[renaming]}
              fillOnPick
              placeholder="New tag name"
              aria-label="New tag name"
            />
            <Button type="submit" size="sm">
              Rename
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={() => setRenaming(null)}>
              Cancel
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            If the new name is already a tag, the two merge into one.
          </p>
        </form>
      ) : null}
      <div className="flex gap-2">
        <TagInput
          value={draft}
          onValueChange={setDraft}
          exclude={tags ?? []}
          onCommit={addTags}
          placeholder="Add a tag"
          aria-label="Add a tag"
        />
        <Button type="button" size="sm" disabled={!splitTags(draft).length} onClick={() => addTags(splitTags(draft))}>
          Add
        </Button>
      </div>
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

/** Where a habit stands on its way to graduating, and the Establish suggestion. */
function EstablishmentPanel({ habit }: { habit: HabitRow }) {
  const stage = habitStage(habit);
  if (stage === "abandoned") return null;
  return (
    <section className="rounded-xl bg-muted/40 p-3">
      <p className="type-scale-section text-muted-foreground">Establishing</p>
      {stage === "established" ? (
        <p className="mt-1 text-sm">
          Established — graduated from daily tracking. Move it back to Building to track it again.
        </p>
      ) : habit.establishment ? (
        <div className="mt-1 flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm">{establishmentProgress(habit.establishment)}</p>
          {isReadyToEstablish(habit) ? <ReadyToEstablish habit={habit} /> : null}
        </div>
      ) : null}
    </section>
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

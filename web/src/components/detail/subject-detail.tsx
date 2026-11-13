import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import type { Resolver } from "react-hook-form";
import { toast } from "sonner";
import {
  BookOpenIcon,
  HistoryIcon,
  ImagePlusIcon,
  LayoutListIcon,
  Link2Icon,
  PaperclipIcon,
  PlusIcon,
  RepeatIcon,
  TagIcon,
  XIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { applyRelationshipWrites, relationshipWrites } from "@/lib/subject-save";
import { Input } from "@/components/ui/input";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Separator } from "@/components/ui/separator";
import { DateChip } from "@/components/primitives/date-chip";
import { EmptyState } from "@/components/primitives/empty-state";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeBadge } from "@/components/primitives/type-badge";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { SubjectTypeFields } from "@/components/create/subject-type-fields";
import { useCreateActions } from "@/components/create/create-context";
import { AttachmentChip } from "@/components/artifacts/attachment-chip";
import { StreakGrid } from "@/components/habits/streak-grid";
import { BountyChip } from "@/components/rewards/bounty-chip";
import { BountySection } from "@/components/rewards/bounty-editor";
import { TaskTimerPanel } from "@/components/timer/task-timer";
import { MaterializePanel } from "@/components/appointments/materialize-panel";
import { RepeatPanel } from "@/components/tasks/repeat-panel";
import { CoverBanner } from "@/components/cover/cover-banner";
import { CoverPicker } from "@/components/cover/cover-picker";
import { canHaveCover } from "@/lib/cover";
import { DecisionActions, RecordDecisionAction } from "@/components/detail/decision-relationships";
import { canStartDecisionFrom, type LinkedItem } from "@/lib/decisions";
import {
  useHabits,
  useListItem,
  useOccurrences,
  useRelations,
  useSubject,
  useSubjectFiles,
  useSubjectTags,
  useWrites,
} from "@/lib/queries";
import { childTypesFor, defaultStatus, isTerminal, shortIdOf } from "@/lib/production-ui-types";
import { bountyDirty, bountyProblem, bountyWrites, type BountyDraft } from "@/lib/bounty";
import { habitStage, targetDirty, targetProblem, targetWrite, type TargetDraft } from "@/lib/establish";
import { parseRecurrence, sameRecurrence } from "@/lib/recurrence";
import { repeatLabel } from "@/lib/task-series";
import {
  attrsToWrite,
  emptyFormValues,
  formConfig,
  formValuesFromDetail,
  hasRecurrenceField,
  isDetailDirty,
  parseAttrBag,
  selectedParentIds,
  subjectFormSchema,
  type SubjectFormValues,
} from "@/lib/subject-forms";
import { RelationshipsTab } from "./relationships-tab";
import { TagsTab } from "./tags-tab";
import { JournalTab } from "./journal-tab";
import { HistoryTab } from "./history-tab";
import { EstablishmentPanel } from "./establishment-panel";

export function SubjectDetail({
  subjectId,
  onDirtyChange,
  onClose,
  onOpenSubject,
}: {
  subjectId: string;
  onDirtyChange?: (dirty: boolean) => void;
  onClose?: () => void;
  /** Opens another subject here: a repeating Task's series, or one of its repetitions. */
  onOpenSubject?: (id: string) => void;
}) {
  const { data, isLoading, isError } = useSubject(subjectId);
  const { data: list } = useListItem(subjectId);
  const { data: rels } = useRelations(subjectId);
  const { data: tags } = useSubjectTags(subjectId);
  const { data: files } = useSubjectFiles(subjectId);
  const { data: habits } = useHabits();
  const { data: occurrences } = useOccurrences(subjectId);
  const [editing, setEditing] = useState(false);
  const [choosingCover, setChoosingCover] = useState(false);
  const [coverPicks, setCoverPicks] = useState(0);
  const [bountyDraft, setBountyDraft] = useState<BountyDraft>({});
  const [targetDraft, setTargetDraft] = useState<TargetDraft>({});
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

  // A new subject starts with nothing typed into its bounty or establish target.
  useEffect(() => {
    setBountyDraft({});
    setTargetDraft({});
    setChoosingCover(false);
  }, [subjectId]);

  const bountyLines = data?.bounty ?? [];
  const dirty = isDetailDirty(
    editing,
    form.formState.isDirty ||
      bountyDirty(bountyLines, bountyDraft) ||
      targetDirty(habit?.establishment, targetDraft),
  );
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
    const bountyIssue = bountyProblem(bountyDraft) ?? targetProblem(targetDraft);
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
    const target = subject.type === "Habit" ? targetWrite(subject.id, habit?.establishment, targetDraft) : null;
    if (target) {
      await writes.setHabitTarget(target);
    }
    setTargetDraft({});
    const nextStatus = values.status || defaultStatus(subject.type);
    if (nextStatus && nextStatus !== (subject.status || defaultStatus(subject.type))) {
      await writes.setStatus(subject.id, nextStatus);
    }
    // The repeat pattern is its own write (D8), sent only when it changed. A Task's start
    // (the first date it repeats from) goes with it (TASKS-3).
    const saved = form.formState.defaultValues?.attrs;
    const patternChanged = !sameRecurrence(values.attrs.recurrence, saved?.recurrence);
    const startChanged =
      subject.type === "Task" && !!values.attrs.recurrence && (values.attrs.start ?? "") !== (saved?.start ?? "");
    if (hasRecurrenceField(subject.type) && (patternChanged || startChanged)) {
      try {
        await writes.recur(
          subject.id,
          parseRecurrence(values.attrs.recurrence),
          subject.type === "Task" && values.attrs.start ? { start: values.attrs.start } : undefined,
        );
      } catch (err) {
        toast.error(err instanceof Error && err.message ? err.message : "Could not change the repeat.");
        return;
      }
    }
    // Parents and people as the form now has them, against what it showed: a new item's create
    // makes the same writes (lib/subject-save.ts). An Appointment's attendees are involved, and a
    // Goal's old identity statement is let go.
    const shown = form.formState.defaultValues as SubjectFormValues | undefined;
    await applyRelationshipWrites(
      relationshipWrites(
        subject.type,
        subject.id,
        values,
        shown
          ? { values: shown, edges: (rels?.parents ?? []).map((p) => ({ subjectId: p.subjectId, relation: p.relation })) }
          : null,
      ),
      writes,
    );
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
    setTargetDraft({});
    setEditing(false);
  }

  const headline = bountyLines[0];
  const coverable = canHaveCover(subject.type);
  // A repeating Task (TASKS-3): its series holds the pattern; each repetition is the Task done.
  const repeating = subject.repeating ?? null;
  const series = !!repeating?.isSeries;
  const repeats = repeating ? repeatLabel(repeating.recurrence) : null;
  // Each opening of the picker starts afresh from the title.
  function chooseCover() {
    setCoverPicks((n) => n + 1);
    setChoosingCover(true);
  }
  const cover = coverable ? subject.cover : null;
  // What a Decision started here, or work created from this Decision, links back to (GEN-13).
  const linked: LinkedItem = {
    id: subject.id,
    title: subject.title,
    type: subject.type,
    status: subject.status || defaultStatus(subject.type),
  };

  return (
    <div className="flex h-full min-h-0 flex-col">
      {cover ? (
        <CoverBanner key={subject.id} subjectId={subject.id} cover={cover} onChangeCover={chooseCover} />
      ) : null}
      {coverable ? (
        <CoverPicker
          key={`${subject.id}-${coverPicks}`}
          open={choosingCover}
          onOpenChange={setChoosingCover}
          subject={{ id: subject.id, title: subject.title }}
        />
      ) : null}
      <header className="shrink-0 border-b px-5 py-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <TypeBadge type={subject.type} />
              {/* Beside the type, not with the buttons, so the title keeps its width. */}
              {coverable && !cover && !editing ? (
                <Button
                  variant="ghost"
                  size="xs"
                  className="-my-1 text-muted-foreground"
                  onClick={chooseCover}
                >
                  <ImagePlusIcon aria-hidden /> Add cover
                </Button>
              ) : null}
              {/* Off while editing, so a replacement's Cancelled isn't undone by the Save. */}
              {!editing && subject.type === "Decision" ? (
                <DecisionActions decision={linked} openNew={openNew} />
              ) : null}
              {!editing && canStartDecisionFrom(subject.type) ? (
                <RecordDecisionAction item={linked} openNew={openNew} />
              ) : null}
            </div>
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
                    : series
                      ? repeating?.nextDue
                      : subject.due
                }
                kind={subject.type === "Goal" || subject.type === "Project" ? "target" : "due"}
                // A series' date is its open repetition's, which a stopped series can still owe.
                closed={!series && isTerminal(subject.status)}
              />
              {repeats ? (
                <span className="inline-flex items-center gap-1 text-[0.75rem] text-muted-foreground">
                  <RepeatIcon className="size-3" aria-hidden />
                  {repeats}
                </span>
              ) : null}
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
        {/* The series is never worked on itself: its repetitions are timed. */}
        {subject.type === "Task" && !series ? (
          <TaskTimerPanel
            taskId={subject.id}
            title={subject.title}
            status={subject.status}
            estimate={parseAttrBag(subject.attributes).estimated_duration}
            className="mt-3"
          />
        ) : null}
        {subject.type === "Appointment" && parseAttrBag(subject.attributes).recurrence ? (
          <MaterializePanel
            seriesId={subject.id}
            recurrence={parseAttrBag(subject.attributes).recurrence}
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
              <SubjectTypeFields form={form} editing={editing} repeating={repeating} />
              {repeating ? (
                <RepeatPanel subjectId={subject.id} repeating={repeating} onOpenSubject={onOpenSubject} />
              ) : null}
              {subject.type === "Habit" && habit ? (
                <EstablishmentPanel
                  habit={habit}
                  editing={editing}
                  draft={targetDraft}
                  onChange={setTargetDraft}
                />
              ) : null}
              <BountySection
                key={`${subject.id}-${editing}`}
                idPrefix={`bounty-${subject.id}`}
                type={subject.type}
                lines={bountyLines}
                draft={bountyDraft}
                onChange={setBountyDraft}
                editing={editing}
                note={
                  series
                    ? "Every repetition pays this, unless one has a price of its own."
                    : subject.bountyFromSeries
                      ? "The repeating task's price. A price set here is for this one only."
                      : undefined
                }
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
                  onRecord={
                    habit && habitStage(habit) === "planned"
                      ? undefined
                      : (date, state, note) => void writes.adhere(subject.id, state, { on: date, note })
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
            <RelationshipsTab subjectId={subject.id} type={subject.type} onOpenSubject={onOpenSubject} />
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

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <p className="type-scale-section text-muted-foreground">{label}</p>
      {children}
    </div>
  );
}

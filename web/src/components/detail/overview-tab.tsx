import type { UseFormReturn } from "react-hook-form";
import {
  CalendarDaysIcon,
  ClockIcon,
  PaperclipIcon,
  RepeatIcon,
  SlidersHorizontalIcon,
  SparklesIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { DetailSection, PropertyList } from "@/components/primitives/detail-section";
import type { Tone } from "@/lib/tones";
import { FieldBlock } from "@/components/create/subject-type-fields";
import { useFieldOptions, type FieldLayout } from "@/components/create/field-options";
import { AttachmentChip } from "@/components/artifacts/attachment-chip";
import { StreakGrid } from "@/components/habits/streak-grid";
import { BountySection } from "@/components/rewards/bounty-editor";
import { MaterializePanel } from "@/components/appointments/materialize-panel";
import { RepeatPanel } from "@/components/tasks/repeat-panel";
import { useSubjectFiles, useWrites } from "@/lib/queries";
import { shortIdOf } from "@/lib/production-ui-types";
import type { BountyLine, HabitRow, SubjectDetail } from "@/lib/production-ui-types";
import type { BountyDraft } from "@/lib/bounty";
import { habitStage, type TargetDraft } from "@/lib/establish";
import { parseRecurrence } from "@/lib/recurrence";
import {
  formConfig,
  groupFields,
  groupTitle,
  parseAttrBag,
  visibleFields,
  type FieldDef,
  type SubjectFormValues,
} from "@/lib/subject-forms";
import { useWriteAction } from "@/lib/write-action";
import { EstablishmentPanel } from "./establishment-panel";

/** Each group's colour (`lib/tones.ts`): a Habit's loop is growth, the dates are when. */
const GROUP_TONE: Record<"loop" | "details" | "when", Tone> = { loop: "green", details: "teal", when: "blue" };

/**
 * An item's Overview, in groups: what it's about as prose, then its details and dates as label
 * and value side by side, what it pays, and how it repeats, all of it in one place near the end.
 * Read-only until Edit, when the same groups hold the inputs. Free text with nothing in it is left
 * out until Edit, so a Goal with no motivation doesn't show an empty one.
 */
export function OverviewTab({
  subject,
  form,
  editing,
  habit,
  bountyLines,
  bountyDraft,
  onBountyDraft,
  targetDraft,
  onTargetDraft,
  onOpenSubject,
}: {
  subject: SubjectDetail;
  form: UseFormReturn<SubjectFormValues>;
  editing: boolean;
  habit?: HabitRow;
  bountyLines: BountyLine[];
  bountyDraft: BountyDraft;
  onBountyDraft: (next: BountyDraft) => void;
  targetDraft: TargetDraft;
  onTargetDraft: (next: TargetDraft) => void;
  /** Opens another subject here: a repeating Task's series, or one of its repetitions. */
  onOpenSubject?: (id: string) => void;
}) {
  const writes = useWrites();
  const reported = useWriteAction({ report: "toast" });
  const { data: files } = useSubjectFiles(subject.id);
  const values = form.watch();
  const type = subject.type;
  const visible = visibleFields(type, values.mode, values);
  const options = useFieldOptions(visible, values);
  const shown = editing
    ? visible
    : visible.filter((f) => f.kind !== "textarea" || f.key === "statement" || !!values.attrs[f.key]?.trim());
  const groups = groupFields(shown);
  const repeating = subject.repeating ?? null;
  const series = !!repeating?.isSeries;

  const fieldsOf = (fields: FieldDef[], layout: FieldLayout = "row") =>
    fields.map((field) => (
      <FieldBlock
        key={field.key}
        field={field}
        form={form}
        editing={editing}
        type={type}
        options={options}
        repeating={repeating}
        layout={layout}
      />
    ));
  const group = (key: "loop" | "details" | "when", icon: typeof SlidersHorizontalIcon) => {
    const fields = groups[key];
    return fields ? (
      <DetailSection title={groupTitle(key, type)} tone={GROUP_TONE[key]} icon={icon}>
        <PropertyList>{fieldsOf(fields)}</PropertyList>
      </DetailSection>
    ) : null;
  };

  // Its repeat: a repeating Task's panel holds the pattern with the repetitions; any other type's
  // pattern is a group of its own, shown read-only only when there is one.
  const repeatFields = groups.repeat ?? [];
  const savedPattern = parseAttrBag(subject.attributes).recurrence;
  const hasRepeat = repeatFields.some((f) =>
    f.kind === "recurrence" ? !!parseRecurrence(values.attrs[f.key]) : !!values.attrs[f.key],
  );

  return (
    <div className="@container space-y-4">
      {groups.about ? <div className="space-y-4 px-0.5">{fieldsOf(groups.about, "block")}</div> : null}
      {groups.loop || groups.details || groups.when ? (
        // Side by side when the panel is wide; a group left over on its own takes the whole row.
        <div className="grid gap-4 @2xl:grid-cols-2 @2xl:items-start @2xl:[&>:last-child:nth-child(odd)]:col-span-2">
          {group("loop", SparklesIcon)}
          {group("details", SlidersHorizontalIcon)}
          {group("when", type === "Appointment" ? ClockIcon : CalendarDaysIcon)}
        </div>
      ) : null}
      {subject.type === "Habit" && habit ? (
        <EstablishmentPanel habit={habit} editing={editing} draft={targetDraft} onChange={onTargetDraft} />
      ) : null}
      {subject.type === "Habit" ? (
        <StreakGrid
          habitId={subject.id}
          allowsPartial={habit?.allowsPartial ?? true}
          onRecord={
            habit && habitStage(habit) === "planned"
              ? undefined
              : (date, state, note) => void reported.run(() => writes.adhere(subject.id, state, { on: date, note }))
          }
        />
      ) : null}
      <BountySection
        key={`${subject.id}-${editing}`}
        framed
        idPrefix={`bounty-${subject.id}`}
        type={subject.type}
        lines={bountyLines}
        draft={bountyDraft}
        onChange={onBountyDraft}
        editing={editing}
        note={
          series
            ? "Every repetition pays this, unless one has a price of its own."
            : subject.bountyFromSeries
              ? "The repeating task's price. A price set here is for this one only."
              : undefined
        }
      />
      {repeating ? (
        <RepeatPanel
          subjectId={subject.id}
          repeating={repeating}
          onOpenSubject={onOpenSubject}
          editing={editing}
          editRows={editing && repeatFields.length > 0 ? fieldsOf(repeatFields) : undefined}
        />
      ) : repeatFields.length > 0 && (editing || hasRepeat) ? (
        <DetailSection title={groupTitle("repeat", type)} tone="violet" icon={RepeatIcon}>
          <PropertyList>{fieldsOf(repeatFields)}</PropertyList>
          {/* Making occurrences is a write of its own, so it waits until the edit is saved. */}
          {subject.type === "Appointment" && savedPattern && !editing ? (
            <MaterializePanel seriesId={subject.id} className="border-t border-foreground/8 pt-3" />
          ) : null}
        </DetailSection>
      ) : null}
      {(files ?? []).length > 0 ? (
        <DetailSection title="Files" tone="teal" icon={PaperclipIcon}>
          <p className="text-xs text-muted-foreground">Related documents and kept audio. Open plays the managed copy.</p>
          <ul className="space-y-2">
            {(files ?? []).map((file) => (
              <li key={file.id}>
                <AttachmentChip artifact={file} />
              </li>
            ))}
          </ul>
        </DetailSection>
      ) : null}
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t border-foreground/8 pt-3 text-xs text-muted-foreground">
        <p className="flex items-center gap-1.5">
          ID
          <code className="rounded bg-muted px-1.5 py-0.5 text-[0.75rem] text-foreground/80">{shortIdOf(subject.urn)}</code>
        </p>
        {formConfig(subject.type).canArchive ? (
          <div className="flex flex-wrap items-center gap-2">
            <span>LifeOS archives, never deletes.</span>
            {subject.archived ? (
              <Button variant="outline" size="xs" onClick={() => void reported.run(() => writes.restore(subject.id))}>
                Restore
              </Button>
            ) : (
              <Button variant="outline" size="xs" onClick={() => void reported.run(() => writes.archive(subject.id))}>
                Archive
              </Button>
            )}
          </div>
        ) : (
          <p>Areas are permanent — they are not archived or deleted.</p>
        )}
      </div>
    </div>
  );
}

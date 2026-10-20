import { useEffect, useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { toast } from "sonner";
import { zodResolver } from "@hookform/resolvers/zod";
import type { FieldErrors, Resolver } from "react-hook-form";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import { SubjectTypeFields } from "@/components/create/subject-type-fields";
import { BountySection } from "@/components/rewards/bounty-editor";
import { TagListField } from "@/components/primitives/tag-input";
import { splitTags } from "@/lib/tag-suggest";
import {
  CREATABLE_TYPES,
  childTypesFor,
  defaultStatus,
  inferChildRelation,
  typeLabel,
} from "@/lib/production-ui-types";
import type { CreatedSubject, NewSubjectRequest, SubjectType } from "@/lib/production-ui-types";
import { parseRecurrence } from "@/lib/recurrence";
import { bountyProblem, bountyWrites, defaultLines, type BountyDraft } from "@/lib/bounty";
import { useBountyDefaults, useCreateSubject, useSubjects, useWrites } from "@/lib/queries";
import {
  attrsToWrite,
  emptyFormValues,
  formConfig,
  selectedParentIds,
  subjectFormSchema,
  type SubjectFormValues,
} from "@/lib/subject-forms";

export type CreateParent = { id: string; title: string; type: SubjectType };

export function NewSubjectDialog({
  open,
  onOpenChange,
  initialType,
  initialParent,
  initialTitle,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  initialType?: SubjectType;
  initialParent?: CreateParent;
  initialTitle?: string;
}) {
  const create = useCreateSubject();
  const writes = useWrites();
  const { data: allSubjects } = useSubjects();
  const childTypes = initialParent ? childTypesFor(initialParent.type) : [];
  const typeChoices = childTypes.length > 0 ? childTypes : CREATABLE_TYPES;
  const startType = (
    initialType && typeChoices.includes(initialType) ? initialType : typeChoices[0]
  ) as SubjectType;

  const form = useForm<SubjectFormValues>({
    resolver: zodResolver(subjectFormSchema) as Resolver<SubjectFormValues>,
    defaultValues: seedValues(startType, initialParent, initialTitle),
  });

  const type = form.watch("type");
  const cfg = formConfig(type);
  const [submitError, setSubmitError] = useState<string | null>(null);
  // The bounty starts on the type's defaults; only triggers Chris changes are saved.
  const { data: bountyDefaults } = useBountyDefaults();
  const bountyLines = defaultLines(type, bountyDefaults);
  const [bountyDraft, setBountyDraft] = useState<BountyDraft>({});

  useEffect(() => {
    if (!open) return;
    form.reset(seedValues(startType, initialParent, initialTitle));
    setSubmitError(null);
    setBountyDraft({});
  }, [open, startType, initialParent, initialTitle, form]);

  // The dialog scrolls, so a field error can sit out of view; repeat it by Save.
  function onInvalid(errors: FieldErrors<SubjectFormValues>) {
    const messages = collectMessages(errors);
    setSubmitError(messages.length ? messages.join(" ") : "Check the highlighted fields.");
  }

  async function onSubmit(values: SubjectFormValues) {
    setSubmitError(null);
    const bountyIssue = bountyProblem(bountyDraft);
    if (bountyIssue) {
      setSubmitError(bountyIssue);
      return;
    }
    const t = values.type;
    const parents = selectedParentIds(values);
    const primaryParent = initialParent?.id || values.parent || parents[0];
    const parentType =
      initialParent?.type ||
      allSubjects?.find((s) => s.id === primaryParent)?.type;
    // Habit → Goal / Identity Statement has no canonical pair; it serves, as the
    // extra parents linked below do.
    const relation =
      primaryParent && parentType
        ? (inferChildRelation(t, parentType) ?? (t === "Habit" ? "serves" : undefined))
        : undefined;
    const attrs = attrsToWrite(t, values, { omitEmpty: true });
    const req: NewSubjectRequest = {
      type: t,
      title: values.title.trim(),
      area: values.area || undefined,
      parent: primaryParent || undefined,
      relation,
      attrs,
    };
    let created: CreatedSubject;
    try {
      created = await create.mutateAsync(req);
    } catch (err) {
      setSubmitError(errorMessage(err, `Could not create the ${typeLabel(t)}.`));
      return;
    }
    // The subject exists now; a failed follow-up must not leave the dialog open,
    // or a second Save would create a duplicate.
    try {
      await applyFollowUps(created, values, t, attrs, parents, primaryParent);
    } catch (err) {
      toast.error(`Created ${created.title}, but not every detail saved: ${errorMessage(err, "unknown error")}`);
    }
    onOpenChange(false);
  }

  async function applyFollowUps(
    created: CreatedSubject,
    values: SubjectFormValues,
    t: SubjectType,
    attrs: Record<string, string>,
    parents: string[],
    primaryParent: string | undefined,
  ) {
    // Priced before any status change, so creating it Completed pays the new bounty.
    for (const req of bountyWrites(created.id, bountyLines, bountyDraft)) {
      await writes.setBounty(req);
    }
    const initialStatus = values.status || defaultStatus(t);
    if (initialStatus && initialStatus !== defaultStatus(t)) {
      await writes.setStatus(created.id, initialStatus);
    }
    if (t === "Habit" && attrs.recurrence) {
      await writes.recur(created.id, parseRecurrence(attrs.recurrence));
    }
    if (values.tags.trim()) {
      const tags = splitTags(values.tags);
      if (tags.length) await writes.tag(created.id, { add: tags });
    }
    if (t === "Habit") {
      for (const pid of parents) {
        if (pid === primaryParent) continue;
        const other = inferChildRelation(t, "Goal") ?? "serves";
        await writes.link(created.id, other, pid);
      }
    }
    if (t === "Commitment" && values.person) {
      await writes.involve(created.id, values.person, "involves");
    }
    if (t === "Appointment" && attrs.attendees) {
      for (const pid of attrs.attendees.split(",").map((x) => x.trim()).filter(Boolean)) {
        await writes.involve(created.id, pid, "attendee");
      }
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[88vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>New {typeLabel(type)}</DialogTitle>
          <DialogDescription>
            Type-specific create. Attributes are filtered to this type; due and scheduled
            stay independent.
            {initialParent ? ` Child of ${initialParent.title}.` : null}
          </DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => void form.handleSubmit(onSubmit, onInvalid)(e)}
        >
          {typeChoices.length > 1 ? (
            <div className="space-y-1.5">
              <Label htmlFor="create-type">Type</Label>
              <Select
                value={type}
                onValueChange={(v) => {
                  const next = v as SubjectType;
                  const prev = form.getValues();
                  setBountyDraft({});
                  const keep = attrsToWrite(next, { ...prev, type: next }, { omitEmpty: true });
                  form.reset({
                    ...emptyFormValues(next, "create"),
                    title: prev.title,
                    tags: prev.tags,
                    parent: prev.parent,
                    parents: prev.parents,
                    person: prev.person,
                    area: prev.area,
                    attrs: { ...emptyFormValues(next).attrs, ...keep },
                  });
                }}
              >
                <SelectTrigger id="create-type" aria-label="Subject type">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {typeChoices.map((t) => (
                    <SelectItem key={t} value={t}>
                      {typeLabel(t)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          ) : null}

          <div className="space-y-1.5">
            <Label htmlFor="title">{cfg.titleLabel}</Label>
            <Input
              id="title"
              autoFocus
              placeholder={cfg.titlePlaceholder ?? cfg.titleLabel}
              {...form.register("title")}
              onKeyDown={(e) => {
                if (e.key === "Enter" && type === "Task" && !e.shiftKey) {
                  e.preventDefault();
                  void form.handleSubmit(onSubmit, onInvalid)();
                }
              }}
            />
            {form.formState.errors.title ? (
              <p className="text-xs text-destructive">{form.formState.errors.title.message}</p>
            ) : null}
          </div>

          <SubjectTypeFields form={form} editing />

          <BountySection
            key={type}
            idPrefix="create-bounty"
            type={type}
            lines={bountyLines}
            draft={bountyDraft}
            onChange={setBountyDraft}
            editing
          />

          <Separator />
          <div className="space-y-1.5">
            <Label htmlFor="tags">Tags (optional)</Label>
            <Controller
              control={form.control}
              name="tags"
              render={({ field }) => (
                <TagListField
                  id="tags"
                  value={field.value}
                  onChange={field.onChange}
                  placeholder="Type to find a tag, Enter to add"
                />
              )}
            />
            <p className="text-xs text-muted-foreground">
              Tags classify. Existing tags are suggested as you type; a new one is fine too.
              Relationships are assigned separately — the parent above is a relationship,
              not a tag.
            </p>
          </div>

          {submitError ? (
            <p role="alert" className="text-sm text-destructive">
              {submitError}
            </p>
          ) : null}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting ? "Creating…" : "Save"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function errorMessage(err: unknown, fallback: string): string {
  return err instanceof Error && err.message ? err.message : fallback;
}

function collectMessages(errors: unknown): string[] {
  if (!errors || typeof errors !== "object") return [];
  const out: string[] = [];
  for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
    // `ref` is the field's DOM node; walking it would recurse through React internals.
    if (key === "ref" || !value || typeof value !== "object") continue;
    const message = (value as { message?: unknown }).message;
    if (typeof message === "string" && message) out.push(message);
    else out.push(...collectMessages(value));
  }
  return [...new Set(out)];
}

function seedValues(
  type: SubjectType,
  parent?: CreateParent,
  title?: string,
): SubjectFormValues {
  const base = emptyFormValues(type, "create");
  return {
    ...base,
    title: title ?? "",
    parent: parent?.id ?? "",
    parents: parent ? [parent.id] : [],
  };
}

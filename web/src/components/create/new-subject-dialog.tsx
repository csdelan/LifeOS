import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import type { Resolver } from "react-hook-form";
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
import {
  CREATABLE_TYPES,
  childTypesFor,
  defaultStatus,
  inferChildRelation,
  typeLabel,
} from "@/lib/production-ui-types";
import type { NewSubjectRequest, SubjectType } from "@/lib/production-ui-types";
import { parseRecurrence } from "@/lib/recurrence";
import { useCreateSubject, useSubjects, useWrites } from "@/lib/queries";
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

  useEffect(() => {
    if (!open) return;
    form.reset(seedValues(startType, initialParent, initialTitle));
  }, [open, startType, initialParent, initialTitle, form]);

  async function onSubmit(values: SubjectFormValues) {
    const t = values.type;
    const parents = selectedParentIds(values);
    const primaryParent = initialParent?.id || values.parent || parents[0];
    const parentType =
      initialParent?.type ||
      allSubjects?.find((s) => s.id === primaryParent)?.type;
    const relation =
      primaryParent && parentType ? inferChildRelation(t, parentType) : undefined;
    const attrs = attrsToWrite(t, values, { omitEmpty: true });
    const req: NewSubjectRequest = {
      type: t,
      title: values.title.trim(),
      area: values.area || undefined,
      parent: primaryParent || undefined,
      relation,
      attrs,
    };
    const created = await create.mutateAsync(req);
    const initialStatus = values.status || defaultStatus(t);
    if (initialStatus && initialStatus !== defaultStatus(t)) {
      await writes.setStatus(created.id, initialStatus);
    }
    if (t === "Habit" && attrs.recurrence) {
      await writes.recur(created.id, parseRecurrence(attrs.recurrence));
    }
    if (values.tags.trim()) {
      const tags = values.tags.split(",").map((x) => x.trim()).filter(Boolean);
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
    onOpenChange(false);
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
          onSubmit={form.handleSubmit((v) => void onSubmit(v))}
        >
          {typeChoices.length > 1 ? (
            <div className="space-y-1.5">
              <Label>Type</Label>
              <Select
                value={type}
                onValueChange={(v) => {
                  const next = v as SubjectType;
                  const prev = form.getValues();
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
                <SelectTrigger>
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
                  void form.handleSubmit((v) => void onSubmit(v))();
                }
              }}
            />
            {form.formState.errors.title ? (
              <p className="text-xs text-destructive">{form.formState.errors.title.message}</p>
            ) : null}
          </div>

          <SubjectTypeFields form={form} editing />

          <Separator />
          <div className="space-y-1.5">
            <Label htmlFor="tags">Tags (comma-separated, optional)</Label>
            <Input id="tags" placeholder="health, focus" {...form.register("tags")} />
            <p className="text-xs text-muted-foreground">
              Tags classify. Relationships are assigned separately — the parent above is a
              relationship, not a tag.
            </p>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? "Creating…" : "Save"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
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

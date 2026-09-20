import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
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
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { CREATABLE_TYPES, STATUS_BY_TYPE, defaultStatus, typeLabel } from "@/lib/production-ui-types";
import type { NewSubjectRequest, SubjectType } from "@/lib/production-ui-types";
import { useAreas, useCreateSubject, useSubjects } from "@/lib/queries";
import { Separator } from "@/components/ui/separator";

const schema = z
  .object({
    type: z.string(),
    title: z.string().min(1, "Title is required"),
    statement: z.string().optional(),
    why: z.string().optional(),
    desiredEndState: z.string().optional(),
    targetDate: z.string().optional(),
    area: z.string().optional(),
    status: z.string().optional(),
    description: z.string().optional(),
    motivation: z.string().optional(),
    due: z.string().optional(),
    scheduled: z.string().optional(),
    parent: z.string().optional(),
    tags: z.string().optional(),
    personKind: z.string().optional(),
    cue: z.string().optional(),
    routine: z.string().optional(),
    date: z.string().optional(),
  })
  .superRefine((val, ctx) => {
    if (val.type === "Goal" && val.status === "Active" && !val.targetDate) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["targetDate"],
        message: "A Goal cannot become Active without a target date.",
      });
    }
    if (val.type === "Goal" && !val.parent) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["parent"],
        message: "Connect this Goal to an Identity Statement.",
      });
    }
  });

type FormValues = z.infer<typeof schema>;

export function NewSubjectDialog({
  open,
  onOpenChange,
  initialType,
  initialParent,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  initialType?: SubjectType;
  initialParent?: { id: string; title: string };
}) {
  const create = useCreateSubject();
  const { data: areas } = useAreas();
  const { data: values } = useSubjects("Value");
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      type: initialType ?? "Task",
      title: "",
      status: defaultStatus(initialType ?? "Task"),
      parent: initialParent?.id ?? "",
      personKind: "human",
    },
  });

  const type = form.watch("type") as SubjectType;

  useEffect(() => {
    if (!open) return;
    form.reset({
      type: initialType ?? "Task",
      title: "",
      status: defaultStatus(initialType ?? "Task"),
      parent: initialParent?.id ?? "",
      personKind: "human",
    });
  }, [open, initialType, initialParent, form]);

  async function onSubmit(values: FormValues) {
    const t = values.type as SubjectType;
    const req: NewSubjectRequest = {
      type: t,
      title: values.title,
      area: values.area || undefined,
      parent: values.parent || undefined,
      attrs: {},
    };
    const attrs: Record<string, string> = {};
    if (values.statement) attrs.statement = values.statement;
    if (values.why) attrs.why = values.why;
    if (values.desiredEndState) attrs.desired_end_state = values.desiredEndState;
    if (values.targetDate) attrs.target_date = values.targetDate;
    if (values.description) attrs.description = values.description;
    if (values.motivation) attrs.motivation = values.motivation;
    if (values.due) attrs.due = values.due;
    if (values.scheduled) attrs.scheduled = values.scheduled;
    if (values.cue) attrs.cue = values.cue;
    if (values.routine) attrs.routine = values.routine;
    if (values.date) attrs.date = values.date;
    if (values.personKind) attrs.person_kind = values.personKind;
    if (values.status && values.status !== defaultStatus(t)) attrs.status = values.status;
    req.attrs = attrs;
    const created = await create.mutateAsync(req);
    if (values.tags?.trim()) {
      // tags after create via write client — create mutation already toasts
      void created;
    }
    onOpenChange(false);
  }

  const statuses = STATUS_BY_TYPE[type];

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[88vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>New {typeLabel(type)}</DialogTitle>
          <DialogDescription>
            Type-specific create. Save is a single mock write.
            {initialParent ? ` Child of ${initialParent.title}.` : null}
          </DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={form.handleSubmit((v) => void onSubmit(v))}
        >
          <div className="space-y-1.5">
            <Label>Type</Label>
            <Select
              value={type}
              onValueChange={(v) => {
                form.setValue("type", v);
                form.setValue("status", defaultStatus(v as SubjectType));
              }}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CREATABLE_TYPES.map((t) => (
                  <SelectItem key={t} value={t}>
                    {typeLabel(t)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="title">Title</Label>
            <Input
              id="title"
              autoFocus
              placeholder={type === "Task" ? "Title, then Enter" : "Title"}
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

          {type === "Value" ? (
            <>
              <Field label="Statement">
                <Textarea placeholder="I'm the type of person who…" {...form.register("statement")} />
              </Field>
              <Field label="Why it matters">
                <Textarea {...form.register("why")} />
              </Field>
            </>
          ) : null}

          {type === "Goal" ? (
            <>
              <Field label="Desired end state">
                <Textarea {...form.register("desiredEndState")} />
              </Field>
              <Field label="Identity Statement">
                <Select
                  value={form.watch("parent") ?? ""}
                  onValueChange={(v) => form.setValue("parent", v)}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Serves…" />
                  </SelectTrigger>
                  <SelectContent>
                    {(values ?? []).map((v) => (
                      <SelectItem key={v.id} value={v.id}>
                        {v.title}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {form.formState.errors.parent ? (
                  <p className="text-xs text-destructive">{form.formState.errors.parent.message}</p>
                ) : null}
              </Field>
              <Field label="Target date">
                <Input type="date" {...form.register("targetDate")} />
                {form.formState.errors.targetDate ? (
                  <p className="text-xs text-destructive">
                    {form.formState.errors.targetDate.message}
                  </p>
                ) : null}
              </Field>
              <Field label="Motivation">
                <Textarea {...form.register("motivation")} />
              </Field>
            </>
          ) : null}

          {type === "Project" ? (
            <>
              <Field label="Description / scope">
                <Textarea {...form.register("description")} />
              </Field>
              <Field label="Target / due date">
                <Input type="date" {...form.register("targetDate")} />
              </Field>
            </>
          ) : null}

          {type === "Task" ? (
            <div className="grid grid-cols-2 gap-3">
              <Field label="Due date">
                <Input type="date" {...form.register("due")} />
              </Field>
              <Field label="Scheduled / do date">
                <Input type="date" {...form.register("scheduled")} />
              </Field>
            </div>
          ) : null}

          {type === "Person" ? (
            <Field label="Kind">
              <Select
                value={form.watch("personKind") ?? "human"}
                onValueChange={(v) => form.setValue("personKind", v)}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="human">Human</SelectItem>
                  <SelectItem value="ai">AI agent</SelectItem>
                </SelectContent>
              </Select>
            </Field>
          ) : null}

          {type === "Habit" ? (
            <>
              <Field label="Cue">
                <Input {...form.register("cue")} />
              </Field>
              <Field label="Routine">
                <Input {...form.register("routine")} />
              </Field>
            </>
          ) : null}

          {type === "Appointment" ? (
            <Field label="Date">
              <Input type="date" {...form.register("date")} />
            </Field>
          ) : null}

          {(type === "Problem" || type === "Decision" || type === "Idea" || type === "Commitment") && (
            <Field label="Description">
              <Textarea {...form.register("description")} />
            </Field>
          )}

          {statuses && statuses.length > 0 ? (
            <Field label="Status">
              <Select
                value={form.watch("status") ?? defaultStatus(type)}
                onValueChange={(v) => form.setValue("status", v)}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {statuses.map((s) => (
                    <SelectItem key={s} value={s}>
                      {s}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
          ) : null}

          {type !== "Area" ? (
            <Field label="Area">
              <Select
                value={form.watch("area") ?? ""}
                onValueChange={(v) => form.setValue("area", v)}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Optional" />
                </SelectTrigger>
                <SelectContent>
                  {(areas ?? []).map((a) => (
                    <SelectItem key={a.id} value={a.urn}>
                      {a.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
          ) : (
            <Field label="Description">
              <Textarea {...form.register("description")} />
            </Field>
          )}

          <Separator />
          <Field label="Tags (comma-separated, optional)">
            <Input placeholder="health, focus" {...form.register("tags")} />
            <p className="text-xs text-muted-foreground">
              Tags classify. Relationships are assigned separately.
            </p>
          </Field>

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

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <Label>{label}</Label>
      {children}
    </div>
  );
}

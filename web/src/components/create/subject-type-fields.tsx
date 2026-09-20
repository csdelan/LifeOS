import type { UseFormReturn } from "react-hook-form";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { RecurrenceEditor } from "@/components/create/recurrence-editor";
import { StatusPill } from "@/components/primitives/status-pill";
import { DateChip } from "@/components/primitives/date-chip";
import { useAreas, usePeople, useSubjects } from "@/lib/queries";
import { STATUS_BY_TYPE, defaultStatus, typeLabel } from "@/lib/production-ui-types";
import type { SubjectType } from "@/lib/production-ui-types";
import {
  NONE,
  areaSelectValue,
  formConfig,
  visibleFields,
  type FieldDef,
  type SubjectFormValues,
} from "@/lib/subject-forms";
import { parseRecurrence, recurrenceLabel } from "@/lib/recurrence";
import { cn } from "@/lib/utils";

export function SubjectTypeFields({
  form,
  editing,
}: {
  form: UseFormReturn<SubjectFormValues>;
  editing: boolean;
}) {
  const type = form.watch("type") as SubjectType;
  const mode = form.watch("mode");
  const values = form.watch();
  const fields = visibleFields(type, mode, values);
  const { data: areas } = useAreas();
  const { data: people } = usePeople();
  const { data: allSubjects } = useSubjects();

  return (
    <div className="space-y-4">
      {fields.map((field) => (
        <FieldBlock
          key={field.key}
          field={field}
          form={form}
          editing={editing}
          type={type}
          areas={areas ?? []}
          people={people ?? []}
          subjects={allSubjects ?? []}
        />
      ))}
    </div>
  );
}

function FieldBlock({
  field,
  form,
  editing,
  type,
  areas,
  people,
  subjects,
}: {
  field: FieldDef;
  form: UseFormReturn<SubjectFormValues>;
  editing: boolean;
  type: SubjectType;
  areas: { id: string; urn: string; name: string }[];
  people: { id: string; title: string; personKind?: string | null }[];
  subjects: { id: string; title: string; type: SubjectType; archived: boolean }[];
}) {
  const error = fieldError(form, field);
  const attr = form.watch(`attrs.${field.key}`) ?? "";
  const cfg = formConfig(type);

  if (field.kind === "status") {
    const statuses = STATUS_BY_TYPE[type] ?? [];
    const status = form.watch("status") || defaultStatus(type);
    if (!cfg.hasStatus || statuses.length === 0) {
      return (
        <p className="text-sm text-muted-foreground">
          {type === "Value"
            ? "Identity Statements have no status — they simply are."
            : "This type has no status workflow."}
        </p>
      );
    }
    return (
      <Field label={field.label} required={field.required} error={error} hint={field.hint}>
        {editing ? (
          <Select value={status} onValueChange={(v) => form.setValue("status", v, { shouldDirty: true })}>
            <SelectTrigger aria-label={field.label}>
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
        ) : (
          <StatusPill status={status || null} />
        )}
      </Field>
    );
  }

  if (field.kind === "area") {
    const area = form.watch("area");
    const name = areas.find((a) => a.urn === area || a.id === area)?.name;
    return (
      <Field label={field.label} error={error} hint={field.hint}>
        {editing ? (
          <Select
            value={areaSelectValue(area)}
            onValueChange={(v) => form.setValue("area", v === NONE ? "" : v, { shouldDirty: true })}
          >
            <SelectTrigger aria-label={field.label}>
              <SelectValue placeholder="Optional" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>None</SelectItem>
              {areas.map((a) => (
                <SelectItem key={a.id} value={a.urn}>
                  {a.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : (
          <p className="text-sm">{name || "—"}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "parent") {
    const parent = form.watch("parent");
    const options = subjects.filter(
      (s) => !s.archived && (!field.parentTypes || field.parentTypes.includes(s.type)),
    );
    const selected = options.find((s) => s.id === parent);
    return (
      <Field label={field.label} required={field.required} error={error} hint={field.hint}>
        {editing ? (
          <Select
            value={parent || NONE}
            onValueChange={(v) => form.setValue("parent", v === NONE ? "" : v, { shouldDirty: true })}
          >
            <SelectTrigger aria-label={field.label}>
              <SelectValue placeholder="Optional" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>{field.required ? "Choose…" : "None"}</SelectItem>
              {options.map((s) => (
                <SelectItem key={s.id} value={s.id}>
                  {field.parentTypes && field.parentTypes.length > 1
                    ? `${typeLabel(s.type)}: ${s.title}`
                    : s.title}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : (
          <p className="text-sm">{selected?.title || "—"}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "parents") {
    const selected = new Set(form.watch("parents"));
    const parent = form.watch("parent");
    if (parent) selected.add(parent);
    const options = subjects.filter(
      (s) => !s.archived && (!field.parentTypes || field.parentTypes.includes(s.type)),
    );
    return (
      <Field label={field.label} required={field.required} error={error} hint={field.hint}>
        {editing ? (
          <ul className="max-h-40 space-y-1 overflow-y-auto rounded-lg border p-2">
            {options.map((s) => {
              const on = selected.has(s.id);
              return (
                <li key={s.id}>
                  <label className="flex cursor-pointer items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={on}
                      onChange={() => {
                        const next = new Set(selected);
                        if (on) next.delete(s.id);
                        else next.add(s.id);
                        const arr = [...next];
                        form.setValue("parents", arr, { shouldDirty: true });
                        form.setValue("parent", arr[0] ?? "", { shouldDirty: true });
                      }}
                    />
                    <span>{s.title}</span>
                    <span className="text-[0.6875rem] text-muted-foreground">{typeLabel(s.type)}</span>
                  </label>
                </li>
              );
            })}
          </ul>
        ) : (
          <p className="text-sm">
            {options
              .filter((s) => selected.has(s.id))
              .map((s) => s.title)
              .join(", ") || "—"}
          </p>
        )}
      </Field>
    );
  }

  if (field.kind === "person") {
    const person = form.watch("person");
    const selected = people.find((p) => p.id === person);
    return (
      <Field label={field.label} error={error} hint={field.hint}>
        {editing ? (
          <Select
            value={person || NONE}
            onValueChange={(v) => form.setValue("person", v === NONE ? "" : v, { shouldDirty: true })}
          >
            <SelectTrigger aria-label={field.label}>
              <SelectValue placeholder="Optional" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>None</SelectItem>
              {people.map((p) => (
                <SelectItem key={p.id} value={p.id}>
                  {p.title}
                  {p.personKind === "ai" ? " (AI)" : ""}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : (
          <p className="text-sm">
            {selected ? `${selected.title}${selected.personKind === "ai" ? " (AI)" : ""}` : "—"}
          </p>
        )}
      </Field>
    );
  }

  if (field.kind === "attendees") {
    const raw = attr;
    const ids = new Set(raw.split(",").map((x) => x.trim()).filter(Boolean));
    return (
      <Field label={field.label} hint={field.hint}>
        {editing ? (
          <ul className="max-h-36 space-y-1 overflow-y-auto rounded-lg border p-2">
            {people.map((p) => {
              const on = ids.has(p.id);
              return (
                <li key={p.id}>
                  <label className="flex cursor-pointer items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={on}
                      onChange={() => {
                        const next = new Set(ids);
                        if (on) next.delete(p.id);
                        else next.add(p.id);
                        form.setValue(`attrs.${field.key}`, [...next].join(","), { shouldDirty: true });
                      }}
                    />
                    {p.title}
                    {p.personKind === "ai" ? (
                      <span className="text-[0.6875rem] text-muted-foreground">AI</span>
                    ) : null}
                  </label>
                </li>
              );
            })}
          </ul>
        ) : (
          <p className="text-sm">
            {people
              .filter((p) => ids.has(p.id))
              .map((p) => p.title)
              .join(", ") || "—"}
          </p>
        )}
      </Field>
    );
  }

  if (field.kind === "recurrence") {
    return (
      <Field label={field.label} hint={field.hint}>
        {editing ? (
          <RecurrenceEditor
            value={attr}
            onChange={(next) => form.setValue(`attrs.${field.key}`, next, { shouldDirty: true })}
          />
        ) : (
          <p className="text-sm">{attr ? recurrenceLabel(parseRecurrence(attr)) : "—"}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "toggle") {
    const on = attr === "true";
    return (
      <Field label={field.label} hint={field.hint}>
        {editing ? (
          <Switch
            checked={on}
            onCheckedChange={(checked) =>
              form.setValue(`attrs.${field.key}`, checked ? "true" : "false", { shouldDirty: true })
            }
          />
        ) : (
          <p className="text-sm">{on ? "Yes" : "No"}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "select") {
    return (
      <Field label={field.label} required={field.required} error={error} hint={field.hint}>
        {editing ? (
          <Select
            value={attr || field.options?.[0]?.value}
            onValueChange={(v) => form.setValue(`attrs.${field.key}`, v, { shouldDirty: true })}
          >
            <SelectTrigger aria-label={field.label}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {(field.options ?? []).map((o) => (
                <SelectItem key={o.value} value={o.value}>
                  {o.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : (
          <p className="text-sm">{attr || "—"}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "textarea") {
    return (
      <Field label={field.label} required={field.required} error={error} hint={field.hint}>
        {editing ? (
          <Textarea
            placeholder={field.placeholder}
            aria-label={field.label}
            value={attr}
            onChange={(e) => form.setValue(`attrs.${field.key}`, e.target.value, { shouldDirty: true })}
          />
        ) : field.key === "statement" ? (
          <p className="font-heading text-lg italic text-foreground/90">{attr || "No statement yet."}</p>
        ) : (
          <p className="whitespace-pre-wrap text-sm text-foreground/85">{attr || "—"}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "date") {
    return (
      <Field label={field.label} required={field.required} error={error} hint={field.hint}>
        {editing ? (
          <Input
            type="date"
            className="w-48"
            aria-label={field.label}
            value={attr}
            onChange={(e) => form.setValue(`attrs.${field.key}`, e.target.value, { shouldDirty: true })}
          />
        ) : attr ? (
          <DateChip
            date={attr}
            kind={
              field.key === "target_date"
                ? "target"
                : field.key === "scheduled"
                  ? "scheduled"
                  : field.key === "due"
                    ? "due"
                    : "plain"
            }
          />
        ) : (
          <span className="text-sm text-muted-foreground">None</span>
        )}
      </Field>
    );
  }

  if (field.kind === "time") {
    return (
      <Field label={field.label} hint={field.hint}>
        {editing ? (
          <Input
            type="time"
            className="w-36"
            aria-label={field.label}
            value={attr}
            onChange={(e) => form.setValue(`attrs.${field.key}`, e.target.value, { shouldDirty: true })}
          />
        ) : (
          <p className="text-sm">{attr || "—"}</p>
        )}
      </Field>
    );
  }

  return (
    <Field label={field.label} required={field.required} error={error} hint={field.hint}>
      {editing ? (
        <Input
          placeholder={field.placeholder}
          aria-label={field.label}
          value={attr}
          onChange={(e) => form.setValue(`attrs.${field.key}`, e.target.value, { shouldDirty: true })}
        />
      ) : (
        <p className="text-sm">{attr || "—"}</p>
      )}
    </Field>
  );
}

function fieldError(form: UseFormReturn<SubjectFormValues>, field: FieldDef): string | undefined {
  if (field.kind === "status") return form.formState.errors.status?.message;
  if (field.kind === "area") return form.formState.errors.area?.message;
  if (field.kind === "parent") return form.formState.errors.parent?.message;
  if (field.kind === "parents") return form.formState.errors.parents?.message as string | undefined;
  if (field.kind === "person") return form.formState.errors.person?.message;
  const attrs = form.formState.errors.attrs as Record<string, { message?: string }> | undefined;
  return attrs?.[field.key]?.message;
}

function Field({
  label,
  required,
  error,
  hint,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <Label className={cn("type-scale-section text-muted-foreground")}>
        {label}
        {required ? <span className="text-destructive"> *</span> : null}
      </Label>
      {children}
      {hint ? <p className="text-xs text-muted-foreground">{hint}</p> : null}
      {error ? <p className="text-xs text-destructive">{error}</p> : null}
    </div>
  );
}

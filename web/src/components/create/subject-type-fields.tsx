import type { UseFormReturn } from "react-hook-form";
import { Input } from "@/components/ui/input";
import { SubjectCombobox } from "@/components/subject-combobox";
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
import { EmptyValue, PropertyRow } from "@/components/primitives/detail-section";
import { useFieldOptions, type FieldLayout, type FieldOptions } from "@/components/create/field-options";
import { defaultStatus, isTerminal, typeLabel } from "@/lib/production-ui-types";
import type { SubjectType, TaskRepeat } from "@/lib/production-ui-types";
import {
  NONE,
  areaSelectValue,
  formConfig,
  statusOptionsFor,
  subGoalDateWarning,
  visibleFields,
  type FieldDef,
  type SubjectFormValues,
} from "@/lib/subject-forms";
import { parseRecurrence, recurrenceLabel } from "@/lib/recurrence";
import { cn } from "@/lib/utils";

export function SubjectTypeFields({
  form,
  editing,
  repeating,
}: {
  form: UseFormReturn<SubjectFormValues>;
  editing: boolean;
  /** The repeating Task this is, or belongs to (TASKS-3): a series that has made one keeps its pattern. */
  repeating?: TaskRepeat | null;
}) {
  const type = form.watch("type") as SubjectType;
  const mode = form.watch("mode");
  const values = form.watch();
  const fields = visibleFields(type, mode, values);
  const options = useFieldOptions(fields, values);

  return (
    <div className="space-y-4">
      {fields.map((field) => (
        <FieldBlock
          key={field.key}
          field={field}
          form={form}
          editing={editing}
          type={type}
          options={options}
          repeating={repeating}
        />
      ))}
    </div>
  );
}

export function FieldBlock({
  field,
  form,
  editing,
  type,
  options,
  repeating,
  layout = "stacked",
}: {
  field: FieldDef;
  form: UseFormReturn<SubjectFormValues>;
  editing: boolean;
  type: SubjectType;
  options: FieldOptions;
  repeating?: TaskRepeat | null;
  layout?: FieldLayout;
}) {
  const { areas, people, subjects } = options;
  const error = fieldError(form, field);
  const attr = form.watch(`attrs.${field.key}`) ?? "";
  const cfg = formConfig(type);

  if (field.kind === "status") {
    const statuses = statusOptionsFor(type, form.watch());
    const status = form.watch("status") || defaultStatus(type);
    if (!cfg.hasStatus || statuses.length === 0) {
      const none =
        type === "Value" ? "Identity Statements have no status — they simply are." : "This type has no status workflow.";
      return layout === "row" ? (
        <PropertyRow label={field.label}>
          <EmptyValue>{none}</EmptyValue>
        </PropertyRow>
      ) : (
        <p className="text-sm text-muted-foreground">{none}</p>
      );
    }
    return (
      <Field
        layout={layout}
        editing={editing}
        label={field.label}
        required={field.required}
        error={error}
        hint={field.hint}
      >
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
      <Field layout={layout} editing={editing} label={field.label} error={error} hint={field.hint}>
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
          <p className="text-sm">{name || <EmptyValue />}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "parent") {
    const parent = form.watch("parent");
    const options = subjects.filter(
      (s) => !s.archived && (!field.parentTypes || field.parentTypes.includes(s.type)),
    );
    const selected = subjects.find((s) => s.id === parent);
    return (
      <Field
        layout={layout}
        editing={editing}
        label={field.label}
        required={field.required}
        error={error}
        hint={field.hint}
      >
        {editing ? (
          <SubjectCombobox
            className="w-full"
            options={options}
            value={parent ?? ""}
            onChange={(v) => form.setValue("parent", v, { shouldDirty: true })}
            ariaLabel={field.label}
            placeholder={field.required ? "Choose…" : "Optional"}
            noneLabel={field.required ? undefined : "None"}
            label={
              field.parentTypes && field.parentTypes.length > 1
                ? (s) => `${typeLabel(s.type)}: ${s.title}`
                : undefined
            }
          />
        ) : (
          <p className="text-sm">{selected?.title || <EmptyValue />}</p>
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
      <Field
        wide
        layout={layout}
        editing={editing}
        label={field.label}
        required={field.required}
        error={error}
        hint={field.hint}
      >
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
              .join(", ") || <EmptyValue />}
          </p>
        )}
      </Field>
    );
  }

  if (field.kind === "person") {
    const person = form.watch("person");
    const selected = people.find((p) => p.id === person);
    return (
      <Field layout={layout} editing={editing} label={field.label} error={error} hint={field.hint}>
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
            {selected ? `${selected.title}${selected.personKind === "ai" ? " (AI)" : ""}` : <EmptyValue />}
          </p>
        )}
      </Field>
    );
  }

  if (field.kind === "attendees") {
    const raw = attr;
    const ids = new Set(raw.split(",").map((x) => x.trim()).filter(Boolean));
    return (
      <Field wide layout={layout} editing={editing} label={field.label} hint={field.hint}>
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
              .join(", ") || <EmptyValue />}
          </p>
        )}
      </Field>
    );
  }

  if (field.kind === "recurrence") {
    // A repeating Task that has made a repetition stops by being Cancelled, not by losing its pattern.
    const keepsPattern = !!repeating?.isSeries && !!repeating.latestOccurrence;
    return (
      <Field
        wide
        layout={layout}
        editing={editing}
        label={field.label}
        hint={keepsPattern ? "To stop it repeating, set its status to Cancelled." : field.hint}
      >
        {editing ? (
          <RecurrenceEditor
            value={attr}
            allowNone={type !== "Habit" && !keepsPattern}
            forTask={type === "Task"}
            onChange={(next) => form.setValue(`attrs.${field.key}`, next, { shouldDirty: true })}
          />
        ) : (
          <p className="text-sm">{repeatText(attr) ?? <EmptyValue />}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "toggle") {
    const on = attr === "true";
    return (
      <Field layout={layout} editing={editing} label={field.label} hint={field.hint}>
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
      <Field
        layout={layout}
        editing={editing}
        label={field.label}
        required={field.required}
        error={error}
        hint={field.hint}
      >
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
          <p className="text-sm">{attr || <EmptyValue />}</p>
        )}
      </Field>
    );
  }

  if (field.kind === "textarea") {
    return (
      <Field
        layout={layout}
        editing={editing}
        label={field.label}
        required={field.required}
        error={error}
        hint={field.hint}
      >
        {editing ? (
          <Textarea
            placeholder={field.placeholder}
            aria-label={field.label}
            value={attr}
            onChange={(e) => form.setValue(`attrs.${field.key}`, e.target.value, { shouldDirty: true })}
          />
        ) : field.key === "statement" ? (
          <p
            className={cn(
              "font-heading italic text-foreground/90",
              layout === "block" ? "border-l-2 border-primary/50 pl-3 text-xl leading-snug" : "text-lg",
            )}
          >
            {attr || "No statement yet."}
          </p>
        ) : (
          <p
            className={cn(
              "whitespace-pre-wrap",
              layout === "block" ? "text-[0.9375rem] leading-relaxed text-foreground" : "text-sm text-foreground/85",
            )}
          >
            {attr || <EmptyValue />}
          </p>
        )}
      </Field>
    );
  }

  if (field.kind === "date") {
    const parentId = form.watch("parent");
    const late =
      type === "Goal" && field.key === "target_date"
        ? subGoalDateWarning(attr, subjects.find((s) => s.id === parentId))
        : undefined;
    return (
      <Field
        layout={layout}
        editing={editing}
        label={field.label}
        required={field.required}
        error={error}
        hint={late ?? field.hint}
      >
        {editing ? (
          <Input
            type="date"
            className="w-full max-w-48"
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
            closed={isTerminal(form.watch("status"))}
          />
        ) : (
          <p className="text-sm">
            <EmptyValue />
          </p>
        )}
      </Field>
    );
  }

  if (field.kind === "time") {
    return (
      <Field layout={layout} editing={editing} label={field.label} hint={field.hint}>
        {editing ? (
          <Input
            type="time"
            className="w-full max-w-36"
            aria-label={field.label}
            value={attr}
            onChange={(e) => form.setValue(`attrs.${field.key}`, e.target.value, { shouldDirty: true })}
          />
        ) : (
          <p className="text-sm">{attr || <EmptyValue />}</p>
        )}
      </Field>
    );
  }

  return (
    <Field
      layout={layout}
      editing={editing}
      label={field.label}
      required={field.required}
      error={error}
      hint={field.hint}
    >
      {editing ? (
        <Input
          placeholder={field.placeholder}
          aria-label={field.label}
          value={attr}
          onChange={(e) => form.setValue(`attrs.${field.key}`, e.target.value, { shouldDirty: true })}
        />
      ) : (
        <p className="text-sm">{attr || <EmptyValue />}</p>
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

/**
 * A field's label, its input or value, and its hint. The hint and the required mark say how to
 * fill it in, so they show only while editing.
 */
function Field({
  label,
  required: isRequired,
  error,
  hint: givenHint,
  layout,
  editing,
  wide = false,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  hint?: string;
  layout: FieldLayout;
  editing: boolean;
  /** Its input needs the row's width (a checklist, the repeat pattern), so its label stays above it longer. */
  wide?: boolean;
  children: React.ReactNode;
}) {
  const hint = editing ? givenHint : undefined;
  const required = editing && isRequired;
  if (layout === "row") {
    return (
      <PropertyRow label={label} required={required} hint={hint} error={error} wide={editing && wide}>
        {children}
      </PropertyRow>
    );
  }
  return (
    <div className="space-y-1.5">
      <Label
        className={cn(
          layout === "block" ? "text-[0.8125rem] font-medium text-muted-foreground" : "type-scale-section text-muted-foreground",
        )}
      >
        {label}
        {required ? <span className="text-destructive"> *</span> : null}
      </Label>
      {children}
      {hint ? <p className="text-xs text-muted-foreground">{hint}</p> : null}
      {error ? <p className="text-xs text-destructive">{error}</p> : null}
    </div>
  );
}

function repeatText(raw?: string): string | null {
  const spec = parseRecurrence(raw);
  return spec ? recurrenceLabel(spec) : null;
}

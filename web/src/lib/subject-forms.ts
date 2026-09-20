import { z } from "zod";
import { todayIso } from "@/lib/dates";
import {
  STATUS_BY_TYPE,
  defaultStatus,
  pickApplicableAttrs,
  type SubjectType,
} from "@/lib/production-ui-types";
import type {
  HabitRow,
  SubjectDetail,
  SubjectListItem,
} from "@/lib/production-ui-types";

export const NONE = "__none__";

export type FieldKind =
  | "text"
  | "textarea"
  | "date"
  | "time"
  | "select"
  | "toggle"
  | "status"
  | "area"
  | "parent"
  | "parents"
  | "person"
  | "attendees"
  | "recurrence";

export interface FieldDef {
  key: string;
  label: string;
  kind: FieldKind;
  required?: boolean;
  placeholder?: string;
  hint?: string;
  options?: { value: string; label: string }[];
  parentTypes?: SubjectType[];
  createOnly?: boolean;
  editOnly?: boolean;
  hideWhen?: (values: SubjectFormValues) => boolean;
}

export interface TypeFormConfig {
  type: SubjectType;
  titleLabel: string;
  titlePlaceholder?: string;
  hasStatus: boolean;
  hasArea: boolean;
  canArchive: boolean;
  fields: FieldDef[];
}

export type FormMode = "create" | "edit";

export type SubjectFormValues = {
  mode: FormMode;
  type: SubjectType;
  title: string;
  status: string;
  area: string;
  parent: string;
  parents: string[];
  person: string;
  tags: string;
  attrs: Record<string, string>;
};

export const subjectFormSchema = z
  .object({
    mode: z.enum(["create", "edit"]),
    type: z.string(),
    title: z.string(),
    status: z.string(),
    area: z.string(),
    parent: z.string(),
    parents: z.array(z.string()),
    person: z.string(),
    tags: z.string(),
    attrs: z.record(z.string()),
  })
  .superRefine((val, ctx) => {
    const type = val.type as SubjectType;
    if (!val.title.trim()) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["title"],
        message: `${titleLabelFor(type)} is required`,
      });
    }
    if (type === "Person" && !val.attrs.person_kind) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["attrs", "person_kind"],
        message: "Human or AI is required",
      });
    }
    if (type === "Goal" && val.status === "Active" && !val.attrs.target_date) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["attrs", "target_date"],
        message: "A Goal cannot become Active without a target date.",
      });
    }
    if (type === "Goal" && !val.parent) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["parent"],
        message: "Connect this Goal to an Identity Statement.",
      });
    }
    if (type === "Habit") {
      const n = new Set([val.parent, ...val.parents].filter((id) => id && id !== NONE)).size;
      if (n < 1) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["parents"],
          message: "A Habit needs at least one Goal or Identity Statement.",
        });
      }
    }
  });

export function titleLabelFor(type: SubjectType): string {
  if (type === "Person" || type === "Area") return "Name";
  if (type === "Value") return "Title";
  return "Title";
}

const PRIORITY_OPTIONS = [
  { value: "Low", label: "Low" },
  { value: "Medium", label: "Medium" },
  { value: "High", label: "High" },
];

const PERSON_KIND_OPTIONS = [
  { value: "human", label: "Human" },
  { value: "ai", label: "AI agent" },
];

function statusField(): FieldDef {
  return { key: "status", label: "Status", kind: "status" };
}
function areaField(): FieldDef {
  return { key: "area", label: "Area", kind: "area" };
}

const CONFIG: Partial<Record<SubjectType, TypeFormConfig>> = {
  Goal: {
    type: "Goal",
    titleLabel: "Title",
    hasStatus: true,
    hasArea: true,
    canArchive: true,
    fields: [
      {
        key: "desired_end_state",
        label: "Desired end state",
        kind: "textarea",
        placeholder: "What is true when this is done?",
      },
      { key: "parent", label: "Identity Statement", kind: "parent", parentTypes: ["Value"], required: true },
      { key: "target_date", label: "Target date", kind: "date" },
      areaField(),
      statusField(),
      { key: "description", label: "Description", kind: "textarea" },
      { key: "motivation", label: "Motivation", kind: "textarea" },
    ],
  },
  Project: {
    type: "Project",
    titleLabel: "Title",
    hasStatus: true,
    hasArea: true,
    canArchive: true,
    fields: [
      { key: "description", label: "Description / scope", kind: "textarea" },
      { key: "start_date", label: "Start date", kind: "date" },
      { key: "target_date", label: "Target / due date", kind: "date" },
      areaField(),
      statusField(),
      { key: "notes", label: "Notes", kind: "textarea" },
      {
        key: "parent",
        label: "Parent Goal",
        kind: "parent",
        parentTypes: ["Goal"],
        createOnly: true,
        hint: "Optional. A Project may stand alone.",
      },
    ],
  },
  Task: {
    type: "Task",
    titleLabel: "Title",
    titlePlaceholder: "Title, then Enter",
    hasStatus: true,
    hasArea: true,
    canArchive: true,
    fields: [
      { key: "description", label: "Description", kind: "textarea" },
      statusField(),
      { key: "due", label: "Due date", kind: "date", hint: "A real deadline. Independent of Scheduled." },
      {
        key: "scheduled",
        label: "Scheduled / do date",
        kind: "date",
        hint: "When you intend to work on it. Never copies Due.",
      },
      areaField(),
      {
        key: "priority",
        label: "Priority",
        kind: "select",
        options: PRIORITY_OPTIONS,
        editOnly: true,
      },
      { key: "estimated_duration", label: "Estimated duration", kind: "text", placeholder: "45m" },
      {
        key: "parent",
        label: "Parent Goal / Project",
        kind: "parent",
        parentTypes: ["Project", "Goal"],
        createOnly: true,
        hint: "Optional. Standalone Tasks are allowed.",
      },
    ],
  },
  Value: {
    type: "Value",
    titleLabel: "Title",
    titlePlaceholder: "A name for this identity",
    hasStatus: false,
    hasArea: true,
    canArchive: true,
    fields: [
      {
        key: "statement",
        label: "Statement",
        kind: "textarea",
        placeholder: "I'm the type of person who…",
      },
      { key: "why_it_matters", label: "Why it matters", kind: "textarea" },
      areaField(),
      { key: "notes", label: "Notes", kind: "textarea" },
    ],
  },
  Problem: {
    type: "Problem",
    titleLabel: "Title",
    hasStatus: true,
    hasArea: true,
    canArchive: true,
    fields: [
      { key: "description", label: "Description", kind: "textarea" },
      { key: "date_identified", label: "Date identified", kind: "date" },
      areaField(),
      statusField(),
      { key: "impact", label: "Impact", kind: "textarea", hint: "Free text — not a rating." },
    ],
  },
  Decision: {
    type: "Decision",
    titleLabel: "Title",
    titlePlaceholder: "The conclusion that was reached",
    hasStatus: true,
    hasArea: true,
    canArchive: true,
    fields: [
      { key: "description", label: "Description", kind: "textarea", placeholder: "Context, reasoning, consequences" },
      { key: "decision_date", label: "Decision date", kind: "date", hint: "Does not default to today." },
      areaField(),
      statusField(),
    ],
  },
  Idea: {
    type: "Idea",
    titleLabel: "Title",
    hasStatus: true,
    hasArea: true,
    canArchive: true,
    fields: [
      { key: "description", label: "Text", kind: "textarea", placeholder: "The captured thought" },
      statusField(),
    ],
  },
  Person: {
    type: "Person",
    titleLabel: "Name",
    hasStatus: false,
    hasArea: true,
    canArchive: true,
    fields: [
      {
        key: "person_kind",
        label: "Human / AI",
        kind: "select",
        required: true,
        options: PERSON_KIND_OPTIONS,
      },
      { key: "role", label: "Role or relationship", kind: "text" },
      { key: "contact", label: "Contact / reference", kind: "text" },
      { key: "description", label: "Description", kind: "textarea" },
      { key: "notes", label: "Notes", kind: "textarea" },
    ],
  },
  Area: {
    type: "Area",
    titleLabel: "Name",
    hasStatus: false,
    hasArea: false,
    canArchive: false,
    fields: [
      { key: "description", label: "Description", kind: "textarea" },
      { key: "notes", label: "Notes", kind: "textarea" },
    ],
  },
  Appointment: {
    type: "Appointment",
    titleLabel: "Title",
    hasStatus: true,
    hasArea: true,
    canArchive: true,
    fields: [
      { key: "date", label: "Date", kind: "date" },
      { key: "all_day", label: "All day", kind: "toggle" },
      {
        key: "start",
        label: "Start",
        kind: "time",
        hideWhen: (v) => v.attrs.all_day === "true",
      },
      {
        key: "end",
        label: "End",
        kind: "time",
        hideWhen: (v) => v.attrs.all_day === "true",
      },
      { key: "location", label: "Location", kind: "text" },
      { key: "meeting_link", label: "Meeting link", kind: "text", placeholder: "https://" },
      { key: "attendees", label: "Attendees", kind: "attendees" },
      { key: "recurrence", label: "Recurrence", kind: "recurrence" },
      areaField(),
      statusField(),
      { key: "notes", label: "Notes", kind: "textarea" },
    ],
  },
  Habit: {
    type: "Habit",
    titleLabel: "Title",
    hasStatus: false,
    hasArea: true,
    canArchive: true,
    fields: [
      { key: "cue", label: "Cue", kind: "text", placeholder: "When the kettle boils…" },
      { key: "routine", label: "Routine", kind: "text" },
      { key: "reward", label: "Reward", kind: "text" },
      { key: "recurrence", label: "Recurrence", kind: "recurrence" },
      { key: "start", label: "Start date", kind: "date" },
      { key: "end", label: "End date", kind: "date" },
      { key: "allows_partial", label: "Allow partial credit", kind: "toggle" },
      {
        key: "parents",
        label: "Parents (Goal or Identity Statement)",
        kind: "parents",
        parentTypes: ["Goal", "Value"],
        required: true,
        hint: "A Habit needs at least one parent.",
      },
      areaField(),
    ],
  },
  Commitment: {
    type: "Commitment",
    titleLabel: "Title",
    hasStatus: true,
    hasArea: true,
    canArchive: true,
    fields: [
      { key: "description", label: "Description", kind: "textarea" },
      { key: "person", label: "Person or AI agent", kind: "person" },
      { key: "due", label: "Due date", kind: "date" },
      { key: "recurrence", label: "Recurrence", kind: "recurrence" },
      areaField(),
      statusField(),
    ],
  },
};

const FALLBACK: TypeFormConfig = {
  type: "Constraint",
  titleLabel: "Title",
  hasStatus: false,
  hasArea: true,
  canArchive: true,
  fields: [
    { key: "description", label: "Description", kind: "textarea" },
    { key: "notes", label: "Notes", kind: "textarea" },
    areaField(),
  ],
};

export function formConfig(type: SubjectType): TypeFormConfig {
  return CONFIG[type] ?? { ...FALLBACK, type, hasStatus: (STATUS_BY_TYPE[type]?.length ?? 0) > 0 };
}

export function visibleFields(type: SubjectType, mode: FormMode, values: SubjectFormValues): FieldDef[] {
  return formConfig(type).fields.filter((f) => {
    if (f.createOnly && mode !== "create") return false;
    if (f.editOnly && mode !== "edit") return false;
    if (f.hideWhen?.(values)) return false;
    return true;
  });
}

export function emptyFormValues(type: SubjectType, mode: FormMode = "create"): SubjectFormValues {
  const attrs: Record<string, string> = {};
  if (type === "Task") attrs.priority = "Medium";
  if (type === "Person") attrs.person_kind = "human";
  if (type === "Problem") attrs.date_identified = todayIso();
  if (type === "Habit") {
    attrs.allows_partial = "true";
    attrs.recurrence = JSON.stringify({ kind: "daily" });
  }
  return {
    mode,
    type,
    title: "",
    status: defaultStatus(type),
    area: "",
    parent: "",
    parents: [],
    person: "",
    tags: "",
    attrs,
  };
}

export function parseAttrBag(raw?: string | null): Record<string, string> {
  if (!raw) return {};
  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    const out: Record<string, string> = {};
    for (const [k, v] of Object.entries(parsed)) {
      if (typeof v === "string") out[k] = v;
      else if (typeof v === "boolean" || typeof v === "number") out[k] = String(v);
    }
    return out;
  } catch {
    return {};
  }
}

export function formValuesFromDetail(
  detail: SubjectDetail,
  extra?: {
    list?: SubjectListItem | null;
    habit?: HabitRow | null;
    parentId?: string;
    parents?: string[];
    personId?: string;
    tags?: string;
  },
): SubjectFormValues {
  const attrs = parseAttrBag(detail.attributes);
  if (detail.statement && !attrs.statement) attrs.statement = detail.statement;
  if (detail.type === "Goal") {
    if (!attrs.desired_end_state && detail.scope) attrs.desired_end_state = detail.scope;
    if (!attrs.target_date && detail.targetDate) attrs.target_date = detail.targetDate;
  } else if (detail.scope && !attrs.description) {
    attrs.description = detail.scope;
  }
  if (detail.due && !attrs.due) attrs.due = detail.due;
  if (detail.targetDate && !attrs.target_date) attrs.target_date = detail.targetDate;
  if (extra?.list?.scheduled && !attrs.scheduled) attrs.scheduled = extra.list.scheduled;
  if (extra?.list?.personKind && !attrs.person_kind) attrs.person_kind = extra.list.personKind;
  if (extra?.habit) {
    if (extra.habit.cue && !attrs.cue) attrs.cue = extra.habit.cue;
    if (extra.habit.routine && !attrs.routine) attrs.routine = extra.habit.routine;
    if (extra.habit.reward && !attrs.reward) attrs.reward = extra.habit.reward;
    if (extra.habit.startDate && !attrs.start) attrs.start = extra.habit.startDate;
    if (extra.habit.endDate && !attrs.end) attrs.end = extra.habit.endDate;
    attrs.allows_partial = extra.habit.allowsPartial ? "true" : "false";
    if (extra.habit.recurrence && !attrs.recurrence) attrs.recurrence = extra.habit.recurrence;
  }
  if (detail.type === "Appointment" && detail.due && !attrs.date) attrs.date = detail.due;
  return {
    mode: "edit",
    type: detail.type,
    title: detail.title,
    status: detail.status || defaultStatus(detail.type),
    area: detail.area ?? extra?.list?.area ?? "",
    parent: extra?.parentId ?? "",
    parents: extra?.parents ?? [],
    person: extra?.personId ?? "",
    tags: extra?.tags ?? "",
    attrs,
  };
}

export function attrsToWrite(
  type: SubjectType,
  values: SubjectFormValues,
  opts?: { omitEmpty?: boolean },
): Record<string, string> {
  const merged: Record<string, string> = { ...values.attrs };
  if (values.area && values.area !== NONE) merged.area = values.area;
  else merged.area = "";
  const picked = pickApplicableAttrs(type, merged);
  if (!opts?.omitEmpty) return picked;
  const out: Record<string, string> = {};
  for (const [k, v] of Object.entries(picked)) {
    if (v !== "") out[k] = v;
  }
  return out;
}

export function selectedParentIds(values: SubjectFormValues): string[] {
  const ids = [values.parent, ...values.parents].filter((id) => id && id !== NONE);
  return [...new Set(ids)];
}

export function areaSelectValue(area?: string): string {
  return area && area !== NONE ? area : NONE;
}

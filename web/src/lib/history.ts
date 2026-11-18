/**
 * An item's history in words (agent-access plan §5.6 step 2.4): each rename or field change by its
 * field's label on the item's form, from and to.
 */
import { formatDay } from "@/lib/dates";
import type { FieldChange, SubjectType } from "@/lib/production-ui-types";
import { formConfig, titleLabelFor } from "@/lib/subject-forms";

/** What LifeOS keeps on an item besides its form's fields, by key. */
const KEPT_LABELS: Record<string, string> = {
  recurrence: "Repeat",
  cover: "Cover",
  bounty: "Bounty",
  photo: "Photo",
};

/** A field's label: its form's, for the item's type, or the key in words ("start_date" → "Start date"). */
export function fieldLabel(type: SubjectType | undefined, field: string): string {
  if (field === "title") return type ? titleLabelFor(type) : "Title";
  const fromForm = type ? formConfig(type).fields.find((f) => f.key === field)?.label : undefined;
  if (fromForm) return fromForm;
  if (KEPT_LABELS[field]) return KEPT_LABELS[field];
  const words = field.replace(/_/g, " ").trim();
  return words ? words[0].toUpperCase() + words.slice(1) : field;
}

const DAY = /^\d{4}-\d{2}-\d{2}$/;

function shown(value: string): string {
  return DAY.test(value) ? formatDay(value) : value;
}

/**
 * What a change did, said after its field's label: "Nov 19 → Nov 21", "set to High", "cleared (was
 * High)". A value that isn't plain (a repeat pattern, a cover) only "changed".
 */
export function changeText(change: FieldChange): string {
  if (!change.plain) return "changed";
  const from = change.from ?? null;
  const to = change.to ?? null;
  if (from !== null && to !== null) return `${shown(from)} → ${shown(to)}`;
  if (to !== null) return `set to ${shown(to)}`;
  if (from !== null) return `cleared (was ${shown(from)})`;
  return "changed";
}

import { useAreas, useParentOptions, usePeople } from "@/lib/queries";
import type { SubjectType } from "@/lib/production-ui-types";
import type { FieldDef, SubjectFormValues } from "@/lib/subject-forms";

/** What an item's fields' pickers choose from: the areas, the people, and the parents' types. */
export type FieldOptions = {
  areas: { id: string; urn: string; name: string }[];
  people: { id: string; title: string; personKind?: string | null }[];
  subjects: { id: string; title: string; type: SubjectType; archived: boolean; targetDate?: string | null }[];
};

/**
 * How a field lays out: "stacked" puts its label above it (the New dialog); "row" puts the label
 * in a column beside it (the detail's groups); "block" is free text on the detail, read as prose.
 */
export type FieldLayout = "stacked" | "row" | "block";

/** `self` is the item being edited, which can't be its own parent (a Goal is offered Goals). */
export function useFieldOptions(
  fields: FieldDef[],
  values: Pick<SubjectFormValues, "parent" | "parents">,
  self?: string,
): FieldOptions {
  const { data: areas } = useAreas();
  const { data: people } = usePeople();
  // Only the types its pickers offer, a Task's parents being Goals and Projects, so the form never
  // downloads every Task there is (code review CR76); and only the open ones, with any picked
  // already, not every Goal and Project ever made (code review CR121).
  const picked = [values.parent ?? "", ...(values.parents ?? [])];
  const { data: subjects } = useParentOptions(pickerTypes(fields), picked);
  return {
    areas: areas ?? [],
    people: people ?? [],
    subjects: (subjects ?? []).filter((s) => s.id !== self),
  };
}

/** The types the visible parent pickers offer, each once. */
function pickerTypes(fields: FieldDef[]): SubjectType[] {
  const types = fields.flatMap((f) => (f.kind === "parent" || f.kind === "parents" ? (f.parentTypes ?? []) : []));
  return [...new Set(types)];
}

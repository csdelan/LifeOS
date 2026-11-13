import {
  inferChildRelation,
  type PersonRole,
  type Relation,
  type SubjectType,
} from "@/lib/production-ui-types";
import { selectedParentIds, type SubjectFormValues } from "@/lib/subject-forms";

/**
 * The relationship writes a subject form's save makes (code review CR16), the same for a new
 * item and an edited one so the two can't drift: links to the parents the form picks, and
 * the people it names. For an edit, `before` is what the form showed when it opened, and
 * what was taken away comes back as a removal; a new item has no before.
 */
export type RelationshipWrite =
  | { kind: "link"; from: string; relation: Relation; to: string; remove?: true }
  | { kind: "involve"; subject: string; person: string; role: PersonRole; remove?: true };

export interface SavedRelationships {
  /** The form's values when it opened. */
  values: SubjectFormValues;
  /** Each item the subject links to now, and how: the relation a removal takes away. */
  edges: readonly { subjectId: string; relation: Relation }[];
}

/** A Habit serves each Goal or identity statement it's for (there's no canonical pair). */
const HABIT_PARENT_RELATION: Relation = inferChildRelation("Habit", "Goal") ?? "serves";

/** A Goal serves its identity statement. */
const GOAL_VALUE_RELATION: Relation = inferChildRelation("Goal", "Value") ?? "serves";

/**
 * The writes after the item itself is made or renamed: `alreadyLinked` is the parent a create
 * linked in the same call (GEN-7), so it isn't linked twice.
 */
export function relationshipWrites(
  type: SubjectType,
  subjectId: string,
  values: SubjectFormValues,
  before: SavedRelationships | null,
  alreadyLinked?: string,
): RelationshipWrite[] {
  const writes: RelationshipWrite[] = [];
  const linked = new Set(before?.edges.map((e) => e.subjectId) ?? []);
  if (alreadyLinked) linked.add(alreadyLinked);
  const relationTo = (id: string, fallback: Relation) =>
    before?.edges.find((e) => e.subjectId === id)?.relation ?? fallback;

  if (type === "Habit") {
    const wanted = selectedParentIds(values);
    for (const id of wanted) {
      if (!linked.has(id)) writes.push({ kind: "link", from: subjectId, relation: HABIT_PARENT_RELATION, to: id });
    }
    // An unticked parent stops being one.
    for (const id of before ? selectedParentIds(before.values) : []) {
      if (!wanted.includes(id) && linked.has(id)) {
        writes.push({ kind: "link", from: subjectId, relation: relationTo(id, HABIT_PARENT_RELATION), to: id, remove: true });
      }
    }
  }

  if (type === "Goal") {
    const wanted = values.parent;
    const shown = before?.values.parent;
    if (wanted && !linked.has(wanted)) {
      writes.push({ kind: "link", from: subjectId, relation: GOAL_VALUE_RELATION, to: wanted });
    }
    // Moved to another identity statement, or cleared: the one the form showed goes.
    if (shown && shown !== wanted && linked.has(shown)) {
      writes.push({ kind: "link", from: subjectId, relation: relationTo(shown, GOAL_VALUE_RELATION), to: shown, remove: true });
    }
  }

  if (type === "Commitment") {
    const wanted = values.person;
    const shown = before?.values.person;
    if (wanted && wanted !== shown) writes.push({ kind: "involve", subject: subjectId, person: wanted, role: "involves" });
    if (shown && shown !== wanted) writes.push({ kind: "involve", subject: subjectId, person: shown, role: "involves", remove: true });
  }

  if (type === "Appointment") {
    const wanted = attendeeIds(values.attrs.attendees);
    const shown = before ? attendeeIds(before.values.attrs.attendees) : [];
    for (const id of wanted) {
      if (!shown.includes(id)) writes.push({ kind: "involve", subject: subjectId, person: id, role: "attendee" });
    }
    for (const id of shown) {
      if (!wanted.includes(id)) writes.push({ kind: "involve", subject: subjectId, person: id, role: "attendee", remove: true });
    }
  }

  return writes;
}

function attendeeIds(attendees: string | undefined): string[] {
  return [...new Set((attendees ?? "").split(",").map((id) => id.trim()).filter(Boolean))];
}

/** Sends the writes in order, through the app's write client. */
export async function applyRelationshipWrites(
  writes: readonly RelationshipWrite[],
  client: {
    link(from: string, relation: Relation, to: string, remove?: boolean): Promise<void>;
    involve(subject: string, person: string, role: PersonRole, remove?: boolean): Promise<void>;
  },
): Promise<void> {
  for (const w of writes) {
    if (w.kind === "link") await client.link(w.from, w.relation, w.to, w.remove);
    else await client.involve(w.subject, w.person, w.role, w.remove);
  }
}

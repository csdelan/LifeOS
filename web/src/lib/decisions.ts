import { typeLabel } from "@/lib/production-ui-types";
import type { Relation, RelationEdge, SubjectType } from "@/lib/production-ui-types";

/**
 * Decisions (GEN-13): a Decision records a conclusion already made. It sits outside the
 * Identity Statement → Goal → Project → Task hierarchy, so its links read as two groups of
 * one `results_in` chain split by direction, plus `supersedes` for a reversal:
 *
 *   Problem / Idea / Goal  ─results_in→  Decision  ─results_in→  Goal / Project / Task
 *        (its Context)                                            (its Resulting work)
 *
 *   replacement Decision  ─supersedes→  original Decision (which is then Cancelled)
 *
 * A Decision's status is always moved by hand: nothing here, and no related work, moves it,
 * except the one explicit step of replacing it.
 */

/** What can carry out a Decision: the trackable work "Resulting work" offers to create. */
export const RESULTING_WORK_TYPES: SubjectType[] = [
  "Goal", "Project", "Task", "Habit", "Commitment", "Appointment",
];

/** Types with no "+ Decision": a Decision has its own actions; Areas, people and rewards don't prompt one. */
const NO_DECISION_FROM: ReadonlySet<SubjectType> = new Set<SubjectType>(["Decision", "Area", "Person", "Reward"]);

/** Whether an item's detail offers a new Decision that takes the item as its Context. */
export function canStartDecisionFrom(type: SubjectType): boolean {
  return !NO_DECISION_FROM.has(type);
}

/** The other end of a pending link, as the detail it was started from knows it. */
export type LinkedItem = { id: string; title: string; type: SubjectType; status?: string | null };

/**
 * A link the New dialog saves with the new item and shows before Save (GEN-13):
 * - `resulting-work`: the Decision results in the new item;
 * - `context`: the item results in the new Decision;
 * - `replacement`: the new Decision supersedes the original, which becomes Cancelled.
 */
export type PendingLink =
  | { kind: "resulting-work"; decision: LinkedItem }
  | { kind: "context"; item: LinkedItem }
  | { kind: "replacement"; decision: LinkedItem };

/** The types the New dialog offers for a pending link. */
export function pendingLinkTypes(link: PendingLink): SubjectType[] {
  return link.kind === "resulting-work" ? RESULTING_WORK_TYPES : ["Decision"];
}

function other(link: PendingLink): LinkedItem {
  return link.kind === "context" ? link.item : link.decision;
}

/** What the New dialog shows of a pending link: a heading, the other item, and what saving does. */
export function pendingLinkView(
  link: PendingLink,
  newType: SubjectType,
): { heading: string; item: LinkedItem; note: string } {
  const item = other(link);
  switch (link.kind) {
    case "resulting-work":
      return {
        heading: "Resulting work of",
        item,
        note: `Saved as work that carries out this Decision. The Decision's status stays as it is.`,
      };
    case "context":
      return {
        heading: "Context",
        item,
        note: `Saved as what prompted this ${typeLabel(newType)}.`,
      };
    case "replacement":
      return {
        heading: "Replaces",
        item,
        note: alreadyCancelled(item)
          ? "Saved as its replacement. It stays Cancelled, with its conclusion and history kept."
          : "Saving marks it Cancelled. Its conclusion and history are kept as they are.",
      };
  }
}

function alreadyCancelled(item: LinkedItem): boolean {
  return (item.status ?? "").trim().toLowerCase() === "cancelled";
}

/**
 * The parent the create itself carries, so the link and the item are made together: only a
 * replacement, whose new Decision is the edge's `from` (new supersedes original). The other
 * links run into the new item, so they follow the create (`pendingLinkWrite`).
 */
export function pendingLinkParent(link: PendingLink): { parent: string; relation: Relation } | null {
  return link.kind === "replacement" ? { parent: link.decision.id, relation: "supersedes" } : null;
}

/** The link to write once the item exists, or null when the create carried it. */
export function pendingLinkWrite(
  link: PendingLink,
  createdId: string,
): { from: string; relation: Relation; to: string } | null {
  switch (link.kind) {
    case "resulting-work":
      return { from: link.decision.id, relation: "results_in", to: createdId };
    case "context":
      return { from: link.item.id, relation: "results_in", to: createdId };
    case "replacement":
      return null;
  }
}

/** The status the save also moves: a replaced Decision becomes Cancelled, unless it already is. */
export function pendingLinkStatus(link: PendingLink): { subject: string; status: string } | null {
  if (link.kind !== "replacement" || alreadyCancelled(link.decision)) return null;
  return { subject: link.decision.id, status: "Cancelled" };
}

/** A Decision's links, grouped as the detail shows them. `other` keeps anything else, in words. */
export interface DecisionGroups {
  /** Incoming `results_in`: what prompted or informed it. */
  context: RelationEdge[];
  /** Outgoing `results_in`: the work that carries it out. */
  resultingWork: RelationEdge[];
  /** Outgoing `supersedes`: the Decision it replaced. */
  replaces: RelationEdge[];
  /** Incoming `supersedes`: the Decision that replaced it. */
  replacedBy: RelationEdge[];
  other: { label: string; edge: RelationEdge }[];
}

/**
 * Splits a Decision's relations (`parents` are its outgoing edges, `children` its incoming
 * ones, as `/api/subjects/{id}/relations` gives them) into its groups.
 */
export function decisionGroups(rels: { parents: RelationEdge[]; children: RelationEdge[] }): DecisionGroups {
  const groups: DecisionGroups = { context: [], resultingWork: [], replaces: [], replacedBy: [], other: [] };
  for (const edge of rels.parents) {
    if (edge.relation === "results_in") groups.resultingWork.push(edge);
    else if (edge.relation === "supersedes") groups.replaces.push(edge);
    else groups.other.push({ label: "Serves", edge });
  }
  for (const edge of rels.children) {
    if (edge.relation === "results_in") groups.context.push(edge);
    else if (edge.relation === "supersedes") groups.replacedBy.push(edge);
    else groups.other.push({ label: "Served by", edge });
  }
  return groups;
}

/** Where a link added later on a Decision's Relationships tab goes. */
export type DecisionLinkGroup = "context" | "resulting-work" | "replaces";

export const DECISION_LINK_GROUPS: { value: DecisionLinkGroup; label: string }[] = [
  { value: "context", label: "Context" },
  { value: "resulting-work", label: "Resulting work" },
  { value: "replaces", label: "Replaces" },
];

/**
 * The edge for an existing item added to a Decision's group. Linking an earlier Decision as
 * Replaces is only a link: its status moves by hand, as every Decision's does.
 */
export function decisionLinkWrite(
  group: DecisionLinkGroup,
  decisionId: string,
  otherId: string,
): { from: string; relation: Relation; to: string } {
  switch (group) {
    case "context":
      return { from: otherId, relation: "results_in", to: decisionId };
    case "resulting-work":
      return { from: decisionId, relation: "results_in", to: otherId };
    case "replaces":
      return { from: decisionId, relation: "supersedes", to: otherId };
  }
}

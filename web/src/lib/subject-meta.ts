import type { SubjectType } from "@/lib/production-ui-types";
import { typeLabel } from "@/lib/production-ui-types";

export { typeLabel };

export const TYPE_GLYPH: Record<SubjectType, string> = {
  Value: "◈",
  Goal: "◎",
  Problem: "?",
  Project: "▣",
  Task: "•",
  Commitment: "↔",
  Decision: "⊃",
  Idea: "✧",
  Person: "☺",
  Constraint: "⊓",
  Season: "◐",
  Area: "◻",
  Habit: "↻",
  Appointment: "◷",
};

export const ALIGNMENT_TYPES: SubjectType[] = ["Value", "Goal", "Project", "Task"];

export function typeTone(type: SubjectType): string {
  switch (type) {
    case "Value":
      return "text-[var(--type-value)]";
    case "Goal":
      return "text-[var(--type-goal)]";
    case "Project":
      return "text-[var(--type-project)]";
    case "Task":
      return "text-[var(--type-task)]";
    case "Idea":
      return "text-[var(--type-idea)]";
    case "Problem":
      return "text-[var(--type-problem)]";
    case "Decision":
      return "text-[var(--type-decision)]";
    case "Commitment":
      return "text-[var(--type-commitment)]";
    case "Constraint":
      return "text-[var(--type-constraint)]";
    default:
      return "text-muted-foreground";
  }
}

export function typeSurface(type: SubjectType): string {
  switch (type) {
    case "Value":
      return "bg-[var(--type-value)]/12 text-[var(--type-value)]";
    case "Goal":
      return "bg-[var(--type-goal)]/12 text-[var(--type-goal)]";
    case "Project":
      return "bg-[var(--type-project)]/12 text-[var(--type-project)]";
    case "Task":
      return "bg-[var(--type-task)]/12 text-[var(--type-task)]";
    case "Idea":
      return "bg-[var(--type-idea)]/12 text-[var(--type-idea)]";
    case "Problem":
      return "bg-[var(--type-problem)]/12 text-[var(--type-problem)]";
    case "Decision":
      return "bg-[var(--type-decision)]/12 text-[var(--type-decision)]";
    case "Commitment":
      return "bg-[var(--type-commitment)]/12 text-[var(--type-commitment)]";
    default:
      return "bg-muted text-muted-foreground";
  }
}

/** CSS accent for graph nodes and MiniMap chips. */
export function typeAccent(type: SubjectType): string {
  switch (type) {
    case "Value":
      return "var(--type-value)";
    case "Goal":
      return "var(--type-goal)";
    case "Project":
      return "var(--type-project)";
    case "Task":
      return "var(--type-task)";
    case "Idea":
      return "var(--type-idea)";
    case "Problem":
      return "var(--type-problem)";
    case "Decision":
      return "var(--type-decision)";
    case "Commitment":
      return "var(--type-commitment)";
    case "Constraint":
      return "var(--type-constraint)";
    default:
      return "var(--muted-foreground)";
  }
}

export function statusTone(status?: string | null): "neutral" | "active" | "attention" | "terminal" {
  if (!status) return "neutral";
  const s = status.toLowerCase();
  if (
    ["completed", "done", "resolved", "closed", "fulfilled", "promoted"].includes(s)
  ) {
    return "terminal";
  }
  if (
    ["cancelled", "abandoned", "dropped", "archived", "rejected", "missed"].includes(
      s,
    )
  ) {
    return "terminal";
  }
  if (["active", "in progress", "implementing", "working"].includes(s)) {
    return "active";
  }
  if (["waiting", "open"].includes(s)) return "attention";
  return "neutral";
}

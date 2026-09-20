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
    default:
      return "bg-muted text-muted-foreground";
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

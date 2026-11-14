import type { SubjectType } from "@/lib/production-ui-types";
import { typeLabel } from "@/lib/production-ui-types";

export { typeLabel };

/** Icons live in `components/primitives/type-icon.tsx` (Lucide). */

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
    case "Person":
      return "text-[var(--type-person)]";
    case "Area":
      return "text-[var(--type-area)]";
    case "Season":
      return "text-[var(--type-season)]";
    case "Habit":
      return "text-[var(--type-habit)]";
    case "Appointment":
      return "text-[var(--type-appointment)]";
    case "Reward":
      return "text-[var(--type-reward)]";
    default:
      return "text-muted-foreground";
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
    case "Person":
      return "var(--type-person)";
    case "Area":
      return "var(--type-area)";
    case "Season":
      return "var(--type-season)";
    case "Habit":
      return "var(--type-habit)";
    case "Appointment":
      return "var(--type-appointment)";
    case "Reward":
      return "var(--type-reward)";
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

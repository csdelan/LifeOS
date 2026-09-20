import { isTerminal } from "@/lib/production-ui-types";
import type { SubjectListItem } from "@/lib/production-ui-types";

export const TASK_GROUPS = ["overdue", "today", "upcoming", "unscheduled"] as const;
export type TaskGroup = (typeof TASK_GROUPS)[number];

export const TASK_GROUP_LABEL: Record<TaskGroup, string> = {
  overdue: "Overdue",
  today: "Today",
  upcoming: "Upcoming",
  unscheduled: "Unscheduled",
};

export function taskGroup(task: SubjectListItem, today: string): TaskGroup {
  const due = task.due ?? null;
  const scheduled = task.scheduled ?? null;
  if (due && due < today) return "overdue";
  if (due === today || scheduled === today) return "today";
  if ((due && due > today) || (scheduled && scheduled > today)) return "upcoming";
  return "unscheduled";
}

export function groupTasks(
  tasks: SubjectListItem[],
  today: string,
): Record<TaskGroup, SubjectListItem[]> {
  const out: Record<TaskGroup, SubjectListItem[]> = {
    overdue: [],
    today: [],
    upcoming: [],
    unscheduled: [],
  };
  for (const task of tasks) {
    out[taskGroup(task, today)].push(task);
  }
  for (const key of TASK_GROUPS) {
    out[key].sort((a, b) => {
      const da = a.due ?? a.scheduled ?? "9999";
      const db = b.due ?? b.scheduled ?? "9999";
      return da.localeCompare(db) || a.title.localeCompare(b.title);
    });
  }
  return out;
}

export function isHiddenTaskStatus(status?: string | null): boolean {
  const s = (status ?? "").toLowerCase();
  return s === "completed" || s === "cancelled" || isTerminal(status);
}

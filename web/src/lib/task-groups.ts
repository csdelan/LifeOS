import { isTerminal } from "@/lib/production-ui-types";
import type { SubjectListItem } from "@/lib/production-ui-types";

export const TASK_GROUPS = ["overdue", "today", "upcoming", "unscheduled", "closed"] as const;
export type TaskGroup = (typeof TASK_GROUPS)[number];

export const TASK_GROUP_LABEL: Record<TaskGroup, string> = {
  overdue: "Overdue",
  today: "Today",
  upcoming: "Upcoming",
  unscheduled: "Unscheduled",
  closed: "Closed",
};

/**
 * Where a Task shows on the Tasks page (TASKS-1): Overdue once its due date has passed;
 * Today when it's due today or its do-date has arrived, and it stays there until it's done
 * or its due date passes (a "sometime this week" chore shows all week, and a one-off planned
 * for yesterday doesn't drop out of sight); Upcoming when a date is ahead; else Unscheduled.
 * Those four hold open Tasks only. A Completed, Missed or Cancelled one, shown when the Status
 * filter asks for it, goes under Closed whatever its dates: it isn't overdue.
 */
export function taskGroup(task: SubjectListItem, today: string): TaskGroup {
  if (isHiddenTaskStatus(task.status)) return "closed";
  const due = task.due ?? null;
  const scheduled = task.scheduled ?? null;
  if (due && due < today) return "overdue";
  if (due === today || (scheduled && scheduled <= today)) return "today";
  if ((due && due > today) || (scheduled && scheduled > today)) return "upcoming";
  return "unscheduled";
}

/** An open Task whose due date has passed. */
export function isOverdueTask(task: SubjectListItem, today: string): boolean {
  return taskGroup(task, today) === "overdue";
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
    closed: [],
  };
  for (const task of tasks) {
    out[taskGroup(task, today)].push(task);
  }
  for (const key of TASK_GROUPS) {
    // Open groups soonest first; Closed latest first. Either way, one with no date last.
    const latestFirst = key === "closed";
    const noDate = latestFirst ? "" : "9999";
    out[key].sort((a, b) => {
      const da = a.due ?? a.scheduled ?? noDate;
      const db = b.due ?? b.scheduled ?? noDate;
      return (latestFirst ? db.localeCompare(da) : da.localeCompare(db)) || a.title.localeCompare(b.title);
    });
  }
  return out;
}

export function isHiddenTaskStatus(status?: string | null): boolean {
  const s = (status ?? "").toLowerCase();
  return s === "completed" || s === "cancelled" || isTerminal(status);
}

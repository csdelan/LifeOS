/**
 * The mock's Focus page: a copy of the API's `FocusReader` (code review CR106), over the mock's
 * store, so mock mode shows what the live app does. The live app reads `GET /api/focus`.
 */

import { maintainingHabitRows, todayHabitRows } from "@/lib/habit-days";
import { isTerminal } from "@/lib/production-ui-types";
import type { AlignmentEdge } from "@/lib/derive";
import type { HabitOccurrenceRow, HabitRow, SubjectListItem } from "@/lib/production-ui-types";
import type { DashboardRead } from "@/lib/reads";

export function deriveDashboard(input: {
  subjects: SubjectListItem[];
  edges: AlignmentEdge[];
  inboxCount: number;
  habits: HabitRow[];
  occurrences: HabitOccurrenceRow[];
  today: string;
  primaryFocus?: { title: string; subjectId: string; kind: "goal" };
  objectives?: DashboardRead["objectives"];
}): DashboardRead {
  const { subjects, edges, inboxCount, habits, occurrences, today } = input;
  const openTasks = subjects.filter(
    (x) => x.type === "Task" && !x.archived && !isTerminal(x.status),
  );
  // TASKS-1: due today or late, or its do-date has arrived (the Tasks page's Overdue and Today).
  const dueAndOverdue = openTasks
    .filter((x) => (x.due && x.due <= today) || (x.scheduled && x.scheduled <= today))
    .sort((a, b) =>
      (a.due ?? a.scheduled ?? "").localeCompare(b.due ?? b.scheduled ?? ""),
    );
  const overdueIds = new Set(dueAndOverdue.map((x) => x.id));
  const nextActions = openTasks
    .filter((x) => !overdueIds.has(x.id) && x.status === "In progress")
    .slice(0, 4);

  const activeGoals = subjects.filter(
    (x) => x.type === "Goal" && x.status === "Active" && !x.archived,
  );
  const activeProjects = subjects.filter(
    (x) => x.type === "Project" && x.status === "Active" && !x.archived,
  );

  const primary =
    [...activeGoals].sort(
      (a, b) =>
        (a.targetDate ?? "9999").localeCompare(b.targetDate ?? "9999") ||
        a.title.localeCompare(b.title),
    )[0] ?? null;

  const childProjectIds = new Set(
    edges
      .filter((e) => primary && e.toId === primary.id && e.relation === "results_in")
      .map((e) => e.fromId),
  );
  const fromProjects = activeProjects.filter((p) => childProjectIds.has(p.id));
  const objectives = (fromProjects.length > 0 ? fromProjects : activeProjects)
    .slice(0, 3)
    .map((p) => ({ id: p.id, title: p.title, subjectId: p.id }));

  // The mock lets its seed set the focus by hand, as the Pilot did.
  const focus = input.primaryFocus ?? (primary
    ? { title: primary.title, subjectId: primary.id, kind: "goal" as const }
    : { title: "No active goal yet", subjectId: "", kind: "goal" as const });
  const focusCover = focus.subjectId
    ? (subjects.find((x) => x.id === focus.subjectId)?.cover ?? null)
    : null;

  return {
    primaryFocus: { ...focus, cover: focusCover },
    objectives:
      input.objectives && input.objectives.length > 0 ? input.objectives : objectives,
    dueAndOverdue,
    nextActions,
    inboxCount,
    todayHabits: todayHabitRows(habits, occurrences, today),
    maintainingHabits: maintainingHabitRows(habits, occurrences, today),
    activeGoals,
    activeProjects,
  };
}

/**
 * The mock's Focus page: a copy of the API's `FocusReader` (code review CR106), over the mock's
 * store, so mock mode shows what the live app does. The live app reads `GET /api/focus`.
 */

import { maintainingHabitRows, todayHabitRows } from "@/lib/habit-days";
import { isTerminal } from "@/lib/production-ui-types";
import type { AlignmentEdge } from "@/lib/derive";
import type { HabitOccurrenceRow, HabitRow, SubjectListItem } from "@/lib/production-ui-types";
import type { DashboardRead } from "@/lib/reads";
import { lapsedAs, type FocusChoiceInput } from "@/lib/mock/focus-store";

/** Nearest target date first, undated last, then by title: the API's order. */
function soonest(items: SubjectListItem[]): SubjectListItem[] {
  return [...items].sort(
    (a, b) => (a.targetDate || "9999").localeCompare(b.targetDate || "9999") || a.title.localeCompare(b.title),
  );
}

export function deriveDashboard(input: {
  subjects: SubjectListItem[];
  edges: AlignmentEdge[];
  inboxCount: number;
  habits: HabitRow[];
  occurrences: HabitOccurrenceRow[];
  today: string;
  /** The Primary focus as last chosen (focus plan G1), or null while none has been. */
  focusChoice?: FocusChoiceInput | null;
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

  // The Goal Chris chose leads while it's Active; else the one with the nearest target date,
  // as a suggestion (TODAY-1, D6, focus plan G1).
  const choice = input.focusChoice ?? null;
  const held = choice?.goalId ? (activeGoals.find((g) => g.id === choice.goalId) ?? null) : null;
  const lapsed = choice?.goalId && !held ? subjects.find((x) => x.id === choice.goalId) : undefined;
  const suggested = soonest(activeGoals)[0] ?? null;
  const primary = held ?? suggested;

  // Its Active sub-goals are its milestones, and the projects that result in it or in any of
  // them, at any depth through Active ones, are its objectives (focus plan G2).
  const activeGoalIds = new Set(activeGoals.map((g) => g.id));
  const under = (id: string) =>
    edges
      .filter((e) => e.toId === id && e.relation === "serves" && activeGoalIds.has(e.fromId) && e.fromId !== primary?.id)
      .map((e) => e.fromId);
  const reached = new Set<string>(primary ? [primary.id] : []);
  const next = [...reached];
  for (let id = next.shift(); id; id = next.shift()) {
    for (const sub of under(id)) {
      if (!reached.has(sub)) {
        reached.add(sub);
        next.push(sub);
      }
    }
  }
  const direct = new Set(primary ? under(primary.id) : []);
  const milestones = soonest(activeGoals.filter((g) => direct.has(g.id))).map((g) => ({
    id: g.id,
    title: g.title,
    targetDate: g.targetDate ?? null,
  }));

  const childProjectIds = new Set(
    edges.filter((e) => reached.has(e.toId) && e.relation === "results_in").map((e) => e.fromId),
  );
  const fromProjects = activeProjects.filter((p) => childProjectIds.has(p.id));
  const objectives = soonest(fromProjects.length > 0 ? fromProjects : activeProjects)
    .slice(0, 3)
    .map((p) => ({ id: p.id, title: p.title, subjectId: p.id }));

  return {
    primaryFocus: {
      title: primary?.title ?? "No active goal yet",
      subjectId: primary?.id ?? "",
      kind: "goal",
      cover: primary?.cover ?? null,
      chosen: held !== null,
      chosenAt: held ? choice!.chosenAt : null,
      chosenIn: held ? choice!.chosenIn : null,
      lapsedTitle: lapsed?.title ?? null,
      lapsedAs: lapsed ? lapsedAs(lapsed) : null,
    },
    objectives:
      input.objectives && input.objectives.length > 0 ? input.objectives : objectives,
    milestones,
    dueAndOverdue,
    nextActions,
    inboxCount,
    todayHabits: todayHabitRows(habits, occurrences, today),
    maintainingHabits: maintainingHabitRows(habits, occurrences, today),
    activeGoals,
    activeProjects,
  };
}

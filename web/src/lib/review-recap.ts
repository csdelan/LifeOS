import { localDateFromTimestamp } from "@/lib/dates";
import type {
  HabitOccurrenceRow,
  InboxItem,
  StatusHistoryEntry,
  SubjectListItem,
} from "@/lib/production-ui-types";

export interface RecapItem {
  kind: string;
  at: string;
  title: string;
  detail?: string;
  subjectId?: string;
}

export interface RecapSection {
  id: string;
  label: string;
  items: RecapItem[];
}

export function composeRecap(input: {
  start: string;
  end: string;
  asOf: string;
  subjects: SubjectListItem[];
  history: Record<string, StatusHistoryEntry[]>;
  occurrences: HabitOccurrenceRow[];
  inbox: InboxItem[];
}): RecapSection[] {
  const { start, end, asOf, subjects, history, occurrences, inbox } = input;
  const byId = new Map(subjects.map((s) => [s.id, s]));

  const statusChanges: RecapItem[] = [];
  for (const [id, entries] of Object.entries(history)) {
    const subject = byId.get(id);
    for (const e of entries) {
      const day = localDateFromTimestamp(e.occurredAt);
      if (day < start || day > end) continue;
      statusChanges.push({
        kind: "status",
        at: e.occurredAt,
        title: subject?.title ?? id,
        detail: e.status,
        subjectId: id,
      });
    }
  }
  statusChanges.sort((a, b) => b.at.localeCompare(a.at));

  const completed: RecapItem[] = [];
  const overdue: RecapItem[] = [];
  for (const s of subjects) {
    if (s.type !== "Task" || s.archived) continue;
    const hist = history[s.id] ?? [];
    const completedIn = hist.find((e) => {
      const day = localDateFromTimestamp(e.occurredAt);
      return e.status === "Completed" && day >= start && day <= end;
    });
    if (completedIn) {
      completed.push({
        kind: "task-completed",
        at: completedIn.occurredAt,
        title: s.title,
        subjectId: s.id,
      });
      continue;
    }
    const status = (s.status ?? "").toLowerCase();
    if (status === "completed" || status === "cancelled") continue;
    if (s.due && s.due < asOf) {
      overdue.push({
        kind: "task-overdue",
        at: s.due,
        title: s.title,
        detail: `Due ${s.due}`,
        subjectId: s.id,
      });
    }
  }

  const habits: RecapItem[] = occurrences
    .filter((o) => o.occurrenceDate >= start && o.occurrenceDate <= end)
    .sort((a, b) => b.occurrenceDate.localeCompare(a.occurrenceDate))
    .map((o) => ({
      kind: "habit",
      at: o.occurrenceDate,
      title: o.habitName,
      detail: o.state.replace("_", " "),
      subjectId: o.habitId,
    }));

  const appointments: RecapItem[] = subjects
    .filter((s) => {
      if (s.type !== "Appointment" || s.archived) return false;
      const day = s.due ?? s.scheduled;
      return !!day && day >= start && day <= end;
    })
    .map((s) => ({
      kind: "appointment",
      at: s.due ?? s.scheduled ?? "",
      title: s.title,
      detail: s.status ?? undefined,
      subjectId: s.id,
    }));

  const inboxItems: RecapItem[] = inbox
    .filter((i) => {
      const day = localDateFromTimestamp(i.triagedAt);
      return day >= start && day <= end;
    })
    .map((i) => ({
      kind: "inbox",
      at: i.triagedAt,
      title:
        i.itemKind === "subject"
          ? (i.subjectTitle ?? "Inbox subject")
          : (i.eventContent ?? "Capture"),
      detail: i.itemKind === "event" ? i.eventKind ?? "note" : i.subjectType ?? "subject",
    }));

  return [
    { id: "status", label: "Status changes", items: statusChanges },
    { id: "completed", label: "Completed tasks", items: completed },
    { id: "overdue", label: "Overdue tasks", items: overdue },
    { id: "habits", label: "Habit adherence", items: habits },
    { id: "appointments", label: "Appointments", items: appointments },
    { id: "inbox", label: "Inbox history", items: inboxItems },
  ];
}

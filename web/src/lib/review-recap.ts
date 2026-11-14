import { periodReading, seriesInPeriod } from "@/lib/task-series";
import type { SeriesRepetitions } from "@/lib/production-ui-types";

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

/** The review's section for repeating Tasks (TASKS-3): each one's repetitions due in the period. */
export function repeatingSection(
  histories: SeriesRepetitions[],
  start: string,
  end: string,
  asOf: string,
): RecapSection {
  return {
    id: "repeating",
    label: "Repeating tasks",
    items: seriesInPeriod(histories, start, end, asOf).map((row) => ({
      kind: "task-series",
      at: row.lastDue,
      title: row.title,
      detail: periodReading(row),
      subjectId: row.seriesId,
    })),
  };
}

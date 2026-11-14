import { useState } from "react";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { AdherenceButtons } from "@/components/habits/adherence-buttons";
import { HABIT_STATE_ROW } from "@/components/habits/habit-state-styles";
import { useHabits, useOccurrencesBetween, useRecap, useWrites } from "@/lib/queries";
import { addDays, formatTimestamp } from "@/lib/dates";
import type { RecapSection } from "@/lib/review-recap";
import { reviewDay, reviewHabitDays, type ReviewPeriod } from "@/lib/reviews";
import type { ReviewDoc } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";
import { useWriteAction } from "@/lib/write-action";
import { ReadFailed } from "@/components/primitives/read-state";

/** What was recorded over the days the review looks back on (REVIEW-1, REVIEW-2). */
export function ReviewRecap({ period, today }: { period: ReviewPeriod; today: string }) {
  const asOf = today < period.end ? today : period.end;
  const recap = useRecap(period.start, period.end, asOf);
  const { data, isLoading, isError } = recap;
  // Habit days have their own check-offs below, so the recap leaves them out.
  const sections = (data ?? []).filter((s) => s.id !== "habits");
  return (
    <section className="mt-8">
      <h2 className="font-heading text-xl">Recap</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Recorded activity for this {period.kind === "daily" ? "day" : "week"}, not only what you
        remembered to write.
      </p>
      {isLoading ? <ListSkeleton rows={4} /> : null}
      <ReadFailed read={recap} what="The recap" notEmpty="an empty recap" size="page" className="py-6" />
      {!isLoading && !isError ? <RecapList sections={sections} /> : null}
    </section>
  );
}

/**
 * The habit days the review looks back on, with the same buttons as Focus (REVIEW-5). A week
 * picks one day at a time. An unrecorded day stays unrecorded, never a failure.
 */
export function HabitCheckOffs({ period, today }: { period: ReviewPeriod; today: string }) {
  const checking = useWriteAction({ report: "toast", fallback: "That habit day didn't save." });
  const last = today < period.end ? today : period.end;
  const habits = useHabits();
  const recorded = useOccurrencesBetween(period.start, last);
  const writes = useWrites();
  const days = reviewHabitDays(habits.data ?? [], recorded.data ?? [], period.start, period.end, today);
  const [picked, setPicked] = useState<string | null>(null);
  const day = days.find((d) => d.day === picked) ?? days[days.length - 1];

  return (
    <section className="mt-8">
      <h2 className="font-heading text-xl">Habits</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Record or correct each day here, the same as on Focus.
      </p>
      {habits.isLoading || recorded.isLoading ? <ListSkeleton rows={3} /> : null}
      <ReadFailed read={[habits, recorded]} what="Habits" notEmpty="an empty list" size="page" className="py-6" />
      {days.length > 1 ? (
        <div className="mt-3 flex flex-wrap gap-1" role="group" aria-label="Habit day">
          {days.map((d) => {
            const done = d.rows.filter((r) => r.state !== "unrecorded").length;
            return (
              <button
                key={d.day}
                type="button"
                aria-pressed={d.day === day?.day}
                className={cn(
                  "min-h-11 rounded-md px-2.5 py-1 text-left text-xs md:min-h-0",
                  d.day === day?.day ? "bg-primary/10 text-foreground" : "text-muted-foreground hover:bg-muted",
                )}
                onClick={() => setPicked(d.day)}
              >
                <span className="block font-medium">{reviewDay(d.day)}</span>
                <span className="block">
                  {done} of {d.rows.length} recorded
                </span>
              </button>
            );
          })}
        </div>
      ) : null}
      {day && !habits.isLoading && !recorded.isLoading ? (
        day.rows.length === 0 ? (
          <p className="mt-3 text-sm text-muted-foreground">No habits were being tracked on {reviewDay(day.day)}.</p>
        ) : (
          <ul className="mt-3 space-y-2" aria-label={`Habits on ${reviewDay(day.day)}`}>
            {day.rows.map((h) => (
              <li
                key={`${h.habitId}-${h.occurrenceDate}`}
                className={cn(
                  "flex items-center justify-between rounded-lg px-2 py-1.5",
                  HABIT_STATE_ROW[h.state],
                )}
              >
                <div>
                  <p className="text-sm font-medium">{h.habitName}</p>
                  <p className="text-[0.6875rem] text-muted-foreground">
                    {h.state === "unrecorded" ? "Unrecorded" : h.state.replace("_", " ")}
                  </p>
                </div>
                <AdherenceButtons
                  state={h.state}
                  allowsPartial={h.allowsPartial}
                  onPick={(s) =>
                    void checking.run(() =>
                      writes.adhere(h.habitId, s === "not_followed" ? "missed" : s, { on: h.occurrenceDate }),
                    )
                  }
                />
              </li>
            ))}
          </ul>
        )
      ) : null}
    </section>
  );
}

/** The week's daily reviews, Monday to Sunday, each with where it stands. */
export function WeekDailies({
  period,
  today,
  dailies,
  onOpenDaily,
}: {
  period: ReviewPeriod;
  today: string;
  dailies: ReviewDoc[];
  onOpenDaily: (day: string) => void;
}) {
  const days = Array.from({ length: 7 }, (_, i) => addDays(period.start, i));
  return (
    <section className="mt-8 rounded-xl bg-muted/40 p-4">
      <h3 className="type-scale-section text-muted-foreground">Daily reviews this week</h3>
      <ul className="mt-2 space-y-1">
        {days.map((day) => {
          const daily = dailies.find((d) => d.date === day);
          return (
            <li key={day}>
              <button
                type="button"
                className="min-h-11 text-sm text-primary hover:underline md:min-h-0"
                onClick={() => onOpenDaily(day)}
              >
                {reviewDay(day)}
              </button>
              <span className="text-sm text-muted-foreground"> · {dailyStanding(daily, day, today)}</span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function dailyStanding(daily: ReviewDoc | undefined, day: string, today: string): string {
  if (daily) {
    if (daily.status === "Completed") return daily.completedLate ? "completed late" : "completed";
    return daily.status === "Missed" ? "missed" : "open";
  }
  if (day < today) return "missed, not written";
  return day === today ? "not opened yet" : "opens that day";
}

function RecapList({ sections }: { sections: RecapSection[] }) {
  return (
    <div className="mt-4 space-y-4">
      {sections.map((section) => (
        <div key={section.id} className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
          <h3 className="type-scale-section text-muted-foreground">{section.label}</h3>
          {section.items.length === 0 ? (
            <p className="mt-2 text-sm text-muted-foreground">Nothing recorded.</p>
          ) : (
            <ul className="mt-2 space-y-1">
              {section.items.map((item, i) => (
                <li key={`${item.at}-${i}`} className="text-sm">
                  <span className="text-muted-foreground">
                    {item.at.length > 10 ? formatTimestamp(item.at) : item.at}
                  </span>
                  {" · "}
                  {item.title}
                  {item.detail ? (
                    <span className="text-muted-foreground"> — {item.detail}</span>
                  ) : null}
                </li>
              ))}
            </ul>
          )}
        </div>
      ))}
    </div>
  );
}

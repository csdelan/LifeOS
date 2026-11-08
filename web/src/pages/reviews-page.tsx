import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { ClipboardCheckIcon } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { AdherenceButtons } from "@/components/habits/adherence-buttons";
import { HABIT_STATE_ROW } from "@/components/habits/habit-state-styles";
import { useDirty } from "@/components/shell/app-shell";
import {
  useDashboard,
  useHabits,
  useOccurrencesBetween,
  useRecap,
  useReviews,
  useWrites,
} from "@/lib/queries";
import {
  addDays,
  endOfWeekSunday,
  formatDayFull,
  formatTimestamp,
  startOfWeekMonday,
  todayIso,
} from "@/lib/dates";
import type { RecapSection } from "@/lib/review-recap";
import {
  reviewDay,
  reviewHabitDays,
  reviewPeriod,
  reviewSpan,
  reviewStanding,
  stepReview,
  whyNotOpen,
  type ReviewPeriod,
} from "@/lib/reviews";
import type { ReviewBody, ReviewDoc, ReviewKind } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

function errorText(e: unknown, fallback: string): string {
  return e instanceof Error && e.message ? e.message : fallback;
}

/**
 * Daily and weekly reviews (REVIEW-0, REVIEW-1, REVIEW-2, REVIEW-5; reviews plan §3). Weeks run
 * Monday to Sunday and a weekly review is named by its Sunday. Today's review, and on a Sunday
 * the week's, opens as soon as it's looked at; a past one that was never written shows as
 * missed and can be written late; neither opens ahead of its day.
 */
export function ReviewsPage() {
  const search = useSearch({ from: "/reviews" });
  const navigate = useNavigate({ from: "/reviews" });
  const kind: ReviewKind = search.kind === "weekly" ? "weekly" : "daily";
  const today = todayIso();
  const period = reviewPeriod(kind, search.date ?? today);
  // Both kinds read the Monday-to-Sunday week holding the review: its dailies and its weekly.
  const weekStart = startOfWeekMonday(period.date);
  const weekEnd = endOfWeekSunday(period.date);
  const { data, isLoading, isError } = useReviews(weekStart, weekEnd);
  const writes = useWrites();
  const { setDirty } = useDirty();
  const docs = data ?? [];
  const doc = docs.find((r) => r.kind === kind && r.date === period.date);
  const weekDoc = docs.find((r) => r.kind === "weekly" && r.date === weekEnd);
  const notYet = whyNotOpen(period, today);
  const lookedBack = period.start <= today;

  const [opening, setOpening] = useState(false);
  const [openError, setOpenError] = useState<string | null>(null);
  const day = period.date;
  const open = useCallback(() => {
    setOpening(true);
    setOpenError(null);
    writes
      .openReview(kind, day)
      .catch((e: unknown) => setOpenError(errorText(e, "The review didn't open.")))
      .finally(() => setOpening(false));
  }, [writes, kind, day]);

  // Today's review opens the first time it's looked at (reviews plan §1), once per visit.
  const tried = useRef(new Set<string>());
  const openNow = !isLoading && !isError && !doc && day === today;
  useEffect(() => {
    const key = `${kind}:${day}`;
    if (!openNow || tried.current.has(key)) return;
    tried.current.add(key);
    open();
  }, [openNow, kind, day, open]);

  function go(nextKind: ReviewKind, nextDate: string) {
    void navigate({ search: { kind: nextKind, date: nextDate } });
  }

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-3xl px-4 py-8 md:px-6">
        <p className="type-scale-section text-primary">Reviews</p>
        <h1 className="type-scale-display mt-1">Look back, then look ahead</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          One editable review per day and per week. A weekly review is written on its Sunday,
          looks back on Monday to Sunday and plans the week after. Complete it whenever it feels
          complete; it stays editable after.
        </p>

        <div className="mt-6 flex flex-wrap items-center gap-2">
          <div className="flex rounded-lg bg-muted p-0.5" role="group" aria-label="Review kind">
            <button
              type="button"
              aria-pressed={kind === "daily"}
              className={cn(
                "min-h-11 rounded-md px-3 py-1 text-sm md:min-h-0",
                kind === "daily" ? "bg-background shadow-sm" : "text-muted-foreground",
              )}
              onClick={() => go("daily", today)}
            >
              Daily
            </button>
            <button
              type="button"
              aria-pressed={kind === "weekly"}
              className={cn(
                "min-h-11 rounded-md px-3 py-1 text-sm md:min-h-0",
                kind === "weekly" ? "bg-background shadow-sm" : "text-muted-foreground",
              )}
              onClick={() => go("weekly", endOfWeekSunday(today))}
            >
              Weekly
            </button>
          </div>
          <Button variant="outline" size="sm" onClick={() => go(kind, stepReview(period, -1))}>
            Previous
          </Button>
          <span className="text-sm font-medium">
            {kind === "daily" ? formatDayFull(period.date) : reviewSpan(period.start, period.end)}
          </span>
          <Button variant="outline" size="sm" onClick={() => go(kind, stepReview(period, 1))}>
            Next
          </Button>
        </div>

        <h2 className="mt-8 font-heading text-2xl">
          {kind === "daily" ? "Daily review" : "Weekly review"}, {reviewDay(period.date)}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {kind === "daily"
            ? `Looks back on the day and plans ${reviewDay(period.planStart)}.`
            : `Looks back on ${reviewSpan(period.start, period.end)} and plans ${reviewSpan(period.planStart, period.planEnd)}.`}
        </p>

        <div className="mt-4">
          {isLoading ? <ListSkeleton rows={2} /> : null}
          {isError ? (
            <EmptyState
              tone="error"
              icon={ClipboardCheckIcon}
              title="Reviews failed to load"
              description="This is not an empty review: the reader never returned."
            />
          ) : null}
          {!isLoading && !isError && !doc ? (
            <ReviewNotOpen
              period={period}
              today={today}
              notYet={notYet}
              opening={opening}
              openError={openError}
              onOpen={open}
            />
          ) : null}
          {doc ? (
            <p
              className={cn(
                "text-sm",
                doc.status === "Completed" && !doc.completedLate && "text-success",
                doc.missed && "text-attention",
                !doc.missed && doc.status !== "Completed" && "text-muted-foreground",
              )}
            >
              {reviewStanding(doc, today)}
            </p>
          ) : null}
        </div>

        {lookedBack ? (
          <>
            <ReviewRecap period={period} today={today} />
            <HabitCheckOffs period={period} today={today} />
          </>
        ) : null}

        {kind === "weekly" ? (
          <WeekDailies
            period={period}
            today={today}
            dailies={docs.filter((r) => r.kind === "daily")}
            onOpenDaily={(d) => go("daily", d)}
          />
        ) : (
          <p className="mt-8 text-sm">
            This day is in the week reviewed on {reviewDay(weekEnd)}.{" "}
            <button
              type="button"
              className="min-h-11 text-primary hover:underline md:min-h-0"
              onClick={() => go("weekly", weekEnd)}
            >
              {weekDoc ? "Open the weekly review" : "See that week"}
            </button>
          </p>
        )}

        {doc ? (
          <ReviewEditor
            key={doc.id}
            doc={doc}
            onDirty={setDirty}
            onSave={(body) => writes.saveReview(doc.id, body)}
            onComplete={() => writes.completeReview(doc.id)}
          />
        ) : null}
      </div>
    </div>
  );
}

/** A review with no document yet: ahead of its day, opening now, or missed and writable late. */
function ReviewNotOpen({
  period,
  today,
  notYet,
  opening,
  openError,
  onOpen,
}: {
  period: ReviewPeriod;
  today: string;
  notYet: string | null;
  opening: boolean;
  openError: string | null;
  onOpen: () => void;
}) {
  if (notYet) {
    return <EmptyState icon={ClipboardCheckIcon} title="Not open yet" description={notYet} className="py-8" />;
  }
  if (openError) {
    return (
      <EmptyState
        tone="error"
        icon={ClipboardCheckIcon}
        title="The review didn't open"
        description={openError}
        action={
          <Button variant="outline" onClick={onOpen}>
            Try again
          </Button>
        }
        className="py-8"
      />
    );
  }
  if (period.date === today || opening) return <ListSkeleton rows={2} />;
  return (
    <div className="rounded-xl bg-attention/8 p-4">
      <p className="text-sm text-attention">
        Missed: no {period.kind} review was written, and its day ended on {reviewDay(period.date)}.
      </p>
      <p className="mt-1 text-sm text-muted-foreground">
        Writing it late still counts it as missed, and keeps what you write.
      </p>
      <Button className="mt-3" variant="outline" onClick={onOpen}>
        Write it late
      </Button>
    </div>
  );
}

/** What was recorded over the days the review looks back on (REVIEW-1, REVIEW-2). */
function ReviewRecap({ period, today }: { period: ReviewPeriod; today: string }) {
  const asOf = today < period.end ? today : period.end;
  const { data, isLoading, isError } = useRecap(period.start, period.end, asOf);
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
      {isError ? (
        <EmptyState
          tone="error"
          title="The recap failed to load"
          description="This is not an empty recap: the reader never returned."
          className="py-6"
        />
      ) : null}
      {!isLoading && !isError ? <RecapList sections={sections} /> : null}
    </section>
  );
}

/**
 * The habit days the review looks back on, with the same buttons as Focus (REVIEW-5). A week
 * picks one day at a time. An unrecorded day stays unrecorded, never a failure.
 */
function HabitCheckOffs({ period, today }: { period: ReviewPeriod; today: string }) {
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
      {habits.isError || recorded.isError ? (
        <EmptyState
          tone="error"
          title="Habits failed to load"
          description="This is not an empty list: the reader never returned."
          className="py-6"
        />
      ) : null}
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
                    void writes
                      .adhere(h.habitId, s === "not_followed" ? "missed" : s, { on: h.occurrenceDate })
                      .catch((e: unknown) => toast.error(errorText(e, "That habit day didn't save.")))
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
function WeekDailies({
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

function ReviewEditor({
  doc,
  onDirty,
  onSave,
  onComplete,
}: {
  doc: ReviewDoc;
  onDirty: (v: boolean) => void;
  onSave: (body: ReviewBody) => Promise<void>;
  onComplete: () => Promise<void>;
}) {
  const [workedWell, setWorkedWell] = useState(doc.workedWell);
  const [differently, setDifferently] = useState(doc.differently);
  const [planning, setPlanning] = useState(doc.planning);
  const [keepFocus, setKeepFocus] = useState(doc.keepFocus);
  const [busy, setBusy] = useState(false);
  const { data: dash } = useDashboard();
  // The page keys this by the review, so another review starts from its own answers.
  const answers = doc.canWriteAnswers;

  const dirty =
    workedWell !== doc.workedWell ||
    differently !== doc.differently ||
    planning !== doc.planning ||
    keepFocus !== doc.keepFocus;

  useEffect(() => {
    onDirty(dirty);
    return () => onDirty(false);
  }, [dirty, onDirty]);

  const body: ReviewBody = { workedWell, differently, planning, keepFocus };

  function run(steps: () => Promise<void>, failure: string) {
    setBusy(true);
    void steps()
      .then(() => onDirty(false))
      .catch((e: unknown) => toast.error(errorText(e, failure)))
      .finally(() => setBusy(false));
  }

  return (
    <div className="mt-8 space-y-8">
      <section className="space-y-4">
        <h2 className="font-heading text-xl">Reflection</h2>
        {answers ? null : (
          <p className="rounded-lg bg-muted/60 p-3 text-sm text-muted-foreground">
            Answers aren't saved on the live site yet: they'll be kept in SuperJournal. Keep focus,
            the habits and completing the review work now.
          </p>
        )}
        <div className="space-y-1.5">
          <Label htmlFor="review-worked-well">What worked well?</Label>
          <Textarea
            id="review-worked-well"
            value={workedWell}
            onChange={(e) => setWorkedWell(e.target.value)}
            disabled={!answers}
            rows={4}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="review-differently">What do I want to do differently?</Label>
          <Textarea
            id="review-differently"
            value={differently}
            onChange={(e) => setDifferently(e.target.value)}
            disabled={!answers}
            rows={4}
          />
        </div>
      </section>

      <section className="space-y-4">
        <h2 className="font-heading text-xl">Planning {reviewSpan(doc.planStart, doc.planEnd)}</h2>
        <div className="flex items-center justify-between rounded-xl bg-card p-4 ring-1 ring-foreground/10">
          <div>
            <p className="type-scale-section text-attention">Primary focus</p>
            <p className="font-heading text-xl">{dash?.primaryFocus.title ?? "—"}</p>
            <p className="text-xs text-muted-foreground">Keeping it is an explicit choice.</p>
          </div>
          <label className="flex min-h-11 items-center gap-2 text-sm md:min-h-0">
            <Switch
              id="keep-focus"
              checked={keepFocus}
              onCheckedChange={setKeepFocus}
              aria-label="Keep primary focus"
            />
            Keep focus
          </label>
        </div>
        {dash?.objectives.length ? (
          <ul className="space-y-1 text-sm">
            {dash.objectives.map((o) => (
              <li key={o.id}>· {o.title}</li>
            ))}
          </ul>
        ) : null}
        <div className="space-y-1.5">
          <Label htmlFor="review-planning">Next projects / tasks</Label>
          <Textarea
            id="review-planning"
            value={planning}
            onChange={(e) => setPlanning(e.target.value)}
            disabled={!answers}
            rows={5}
            placeholder="Reassess objectives. List the next Projects and Tasks. Habits and appointments can live here too."
          />
        </div>
      </section>

      <div className="flex gap-2 pb-10">
        <Button
          variant="outline"
          disabled={!dirty || busy}
          onClick={() => run(() => onSave(body), "The review didn't save.")}
        >
          Save
        </Button>
        {doc.status === "Completed" ? null : (
          <Button
            disabled={busy}
            onClick={() =>
              run(
                async () => {
                  if (dirty) await onSave(body);
                  await onComplete();
                },
                "The review didn't complete.",
              )
            }
          >
            {doc.missed ? "Complete it late" : "Complete review"}
          </Button>
        )}
      </div>
    </div>
  );
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

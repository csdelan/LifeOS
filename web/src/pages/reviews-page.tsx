import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { ClipboardCheckIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { useDirty } from "@/components/shell/unsaved-edits";
import { useReviews, useWrites } from "@/lib/queries";
import { endOfWeekSunday, formatDayFull, startOfWeekMonday, todayIso } from "@/lib/dates";
import {
  reviewDay,
  reviewPeriod,
  reviewSpan,
  reviewStanding,
  stepReview,
  whyNotOpen,
  type ReviewPeriod,
} from "@/lib/reviews";
import type { ReviewKind } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";
import { errorText } from "@/lib/write-action";
import { ReadFailed } from "@/components/primitives/read-state";
import { ReviewEditor } from "@/components/reviews/review-editor";
import { HabitCheckOffs, ReviewRecap, WeekDailies } from "@/components/reviews/review-looking-back";

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
  const reviews = useReviews(weekStart, weekEnd);
  const { data, isLoading, isError } = reviews;
  const writes = useWrites();
  const { setDirty, discards } = useDirty();
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
          <ReadFailed
            read={reviews}
            what="Reviews"
            notEmpty="an empty review"
            size="page"
            icon={ClipboardCheckIcon}
          />
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
            // By Discard too, so the choice it threw away doesn't stay on screen (code review CR92).
            key={`${doc.id}:${discards}`}
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

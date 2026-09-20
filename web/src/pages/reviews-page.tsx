import { useEffect, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { ClipboardCheckIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { useDirty } from "@/components/shell/app-shell";
import { useDashboard, useRecap, useReviews, useWrites } from "@/lib/queries";
import {
  addDays,
  endOfWeekSaturday,
  formatDayFull,
  formatTimestamp,
  startOfWeekSunday,
  todayIso,
} from "@/lib/dates";
import type { RecapSection } from "@/lib/review-recap";
import type { ReviewBody, ReviewDoc, ReviewKind } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

export function ReviewsPage() {
  const search = useSearch({ from: "/reviews" });
  const navigate = useNavigate({ from: "/reviews" });
  const kind: ReviewKind = search.kind === "weekly" ? "weekly" : "daily";
  const today = todayIso();
  const date = search.date ?? (kind === "weekly" ? startOfWeekSunday(today) : today);
  const { data, isLoading, isError } = useReviews(kind, date);
  const writes = useWrites();
  const { setDirty } = useDirty();

  const doc = (data ?? []).find((r) =>
    kind === "daily" ? r.kind === "daily" && r.date === date : r.kind === "weekly" && r.weekStart === date,
  );

  const weekStart = startOfWeekSunday(date);
  const weekEnd = endOfWeekSaturday(weekStart);
  const weekDoc = (data ?? []).find((r) => r.kind === "weekly" && r.weekStart === weekStart);
  const dailies = (data ?? []).filter(
    (r) => r.kind === "daily" && r.date >= weekStart && r.date <= weekEnd,
  );

  function go(nextKind: ReviewKind, nextDate: string) {
    void navigate({ search: { kind: nextKind, date: nextDate } });
  }

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-3xl px-4 py-8 md:px-6">
        <p className="type-scale-section text-primary">Reviews</p>
        <h1 className="type-scale-display mt-1">Look back, then look ahead</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          One editable document per day and per week — not an append-only journal. Complete
          whenever it feels complete; it stays editable after.
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
              onClick={() => go("weekly", startOfWeekSunday(today))}
            >
              Weekly
            </button>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() =>
              go(kind, kind === "daily" ? addDays(date, -1) : addDays(date, -7))
            }
          >
            Previous
          </Button>
          <span className="text-sm font-medium">{formatDayFull(date)}</span>
          <Button
            variant="outline"
            size="sm"
            onClick={() =>
              go(kind, kind === "daily" ? addDays(date, 1) : addDays(date, 7))
            }
          >
            Next
          </Button>
        </div>

        {isLoading ? <ListSkeleton rows={8} /> : null}
        {isError ? (
          <EmptyState
            tone="error"
            icon={ClipboardCheckIcon}
            title="Reviews failed to load"
            description="This is not an empty review — the reader never returned."
          />
        ) : null}
        {!isLoading && !doc ? (
          <EmptyState
            icon={ClipboardCheckIcon}
            title="No review document"
            description="The mock lazily creates a daily and weekly review as you open them."
          />
        ) : null}
        {doc ? (
          <ReviewEditor
            doc={doc}
            weekDoc={weekDoc}
            dailies={dailies}
            onDirty={setDirty}
            onSave={(body) => writes.saveReview(doc.id, body)}
            onComplete={() => writes.completeReview(doc.id)}
            onOpenDaily={(d) => go("daily", d)}
            onOpenWeek={() => go("weekly", weekStart)}
          />
        ) : null}
      </div>
    </div>
  );
}

function ReviewEditor({
  doc,
  weekDoc,
  dailies,
  onDirty,
  onSave,
  onComplete,
  onOpenDaily,
  onOpenWeek,
}: {
  doc: ReviewDoc;
  weekDoc?: ReviewDoc;
  dailies: ReviewDoc[];
  onDirty: (v: boolean) => void;
  onSave: (body: ReviewBody) => Promise<void>;
  onComplete: () => Promise<void>;
  onOpenDaily: (date: string) => void;
  onOpenWeek: () => void;
}) {
  const [workedWell, setWorkedWell] = useState(doc.workedWell);
  const [differently, setDifferently] = useState(doc.differently);
  const [planning, setPlanning] = useState(doc.planning);
  const [keepFocus, setKeepFocus] = useState(doc.keepFocus);
  const start = doc.kind === "daily" ? doc.date : doc.weekStart;
  const end = doc.kind === "daily" ? doc.date : doc.weekEnd;
  const today = todayIso();
  const asOf = today < end ? today : end;
  const { data: recap, isLoading: recapLoading } = useRecap(start, end, asOf);
  const { data: dash } = useDashboard();

  useEffect(() => {
    setWorkedWell(doc.workedWell);
    setDifferently(doc.differently);
    setPlanning(doc.planning);
    setKeepFocus(doc.keepFocus);
  }, [doc.id, doc.workedWell, doc.differently, doc.planning, doc.keepFocus]);

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

  return (
    <div className="mt-8 space-y-8">
      {doc.completedAt ? (
        <p className="text-sm text-success">
          Completed {formatTimestamp(doc.completedAt)}. Still editable.
        </p>
      ) : (
        <p className="text-sm text-muted-foreground">In progress — complete when it feels done.</p>
      )}

      <section>
        <h2 className="font-heading text-xl">Recap</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Recorded activity for this {doc.kind === "daily" ? "day" : "week"}, not only what you
          remembered to write.
        </p>
        {recapLoading ? <ListSkeleton rows={4} /> : <RecapList sections={recap ?? []} />}
      </section>

      {doc.kind === "weekly" ? (
        <section className="rounded-xl bg-muted/40 p-4">
          <h3 className="type-scale-section text-muted-foreground">Daily reviews this week</h3>
          <ul className="mt-2 space-y-1">
            {dailies.length === 0 ? (
              <li className="text-sm text-muted-foreground">None written yet.</li>
            ) : (
              dailies.map((d) => (
                <li key={d.id}>
                  <button
                    type="button"
                    className="min-h-11 text-sm text-primary hover:underline"
                    onClick={() => onOpenDaily(d.date)}
                  >
                    {formatDayFull(d.date)}
                    {d.completedAt ? " · completed" : " · open"}
                  </button>
                </li>
              ))
            )}
          </ul>
        </section>
      ) : (
        <p className="text-sm">
          This day belongs to the week of {formatDayFull(doc.weekStart)}.{" "}
          <button type="button" className="text-primary hover:underline" onClick={onOpenWeek}>
            {weekDoc ? "Open weekly review" : "Open this week"}
          </button>
        </p>
      )}

      <section className="space-y-4">
        <h2 className="font-heading text-xl">Reflection</h2>
        <div className="space-y-1.5">
          <Label htmlFor="review-worked-well">What worked well?</Label>
          <Textarea
            id="review-worked-well"
            value={workedWell}
            onChange={(e) => setWorkedWell(e.target.value)}
            rows={4}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="review-differently">What do I want to do differently?</Label>
          <Textarea
            id="review-differently"
            value={differently}
            onChange={(e) => setDifferently(e.target.value)}
            rows={4}
          />
        </div>
      </section>

      <section className="space-y-4">
        <h2 className="font-heading text-xl">Planning</h2>
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
            rows={5}
            placeholder="Reassess objectives. List the next Projects and Tasks. Habits and appointments can live here too."
          />
        </div>
      </section>

      <div className="flex gap-2 pb-10">
        <Button
          variant="outline"
          disabled={!dirty}
          onClick={() => {
            void onSave(body).then(() => onDirty(false));
          }}
        >
          Save
        </Button>
        <Button
          onClick={() => {
            void onSave(body)
              .then(() => onComplete())
              .then(() => onDirty(false));
          }}
        >
          Complete review
        </Button>
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

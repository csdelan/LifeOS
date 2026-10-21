import { useEffect, useState } from "react";
import { HourglassIcon, PencilIcon } from "lucide-react";
import { FastDialog } from "@/components/food/fast-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { DIET_RULES, fastingFor, formatDuration, roundHalfUp } from "@/lib/diet-score";
import { fastEarningText, formatClock, formatClockOn, fromLocalInput, toLocalInput } from "@/lib/food";
import type { FastRow, FoodDayRead } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";

const HOUR = 3600;

/** Ticks every 30 seconds, so a running fast's counter moves. */
function useNow(): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 30_000);
    return () => window.clearInterval(id);
  }, []);
  return now;
}

/**
 * The fasting card (FOOD-2): a running fast's live counter with End fast, or Begin fast set to
 * the last entry's time; the fasts that ended on the day with what they pay; and Record a past
 * fast. Begin and End only show on today.
 */
export function FastingCard({ day }: { day: FoodDayRead }) {
  const isToday = day.day === day.today;
  const [dialog, setDialog] = useState<{ open: boolean; fast: FastRow | null }>({ open: false, fast: null });
  const longest = day.fasts.reduce<FastRow | null>(
    (best, f) => ((f.seconds ?? 0) > (best?.seconds ?? -1) ? f : best),
    null,
  );

  return (
    <section aria-labelledby="fasting-title" className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <h2 id="fasting-title" className="type-scale-section mb-3 flex items-center gap-1.5 text-muted-foreground">
        <HourglassIcon className="size-3.5" aria-hidden />
        Fasting
      </h2>

      {isToday ? day.running ? <RunningFast fast={day.running} /> : <BeginFast day={day} /> : null}

      {day.fasts.length > 0 ? (
        <ul aria-label="Fasts that ended this day" className="mt-3 space-y-1.5">
          {day.fasts.map((f) => (
            <li key={f.id} className="flex items-center justify-between gap-2 rounded-lg bg-muted/50 px-3 py-2 text-sm">
              <span className="min-w-0">
                <span className="font-medium">{formatDuration(f.seconds ?? 0)}</span>
                <span className="text-muted-foreground">
                  {" "}
                  · {formatClockOn(f.startedAt, day.day)} to {formatClock(f.endedAt!)}
                </span>
                <span className="block text-xs text-muted-foreground">
                  {f.id === longest?.id
                    ? `${roundHalfUp(fastingFor(f.seconds ?? 0))} base XP`
                    : "Only the day's longest fast counts"}
                  {f.mealsDuring > 0 ? ` · ${f.mealsDuring} entr${f.mealsDuring === 1 ? "y" : "ies"} eaten during it` : ""}
                </span>
              </span>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`Change the fast that ended at ${formatClock(f.endedAt!)}`}
                onClick={() => setDialog({ open: true, fast: f })}
              >
                <PencilIcon />
              </Button>
            </li>
          ))}
        </ul>
      ) : !isToday ? (
        <p className="text-sm text-muted-foreground">No fast ended this day.</p>
      ) : null}

      <Button variant="outline" size="sm" className="mt-3" onClick={() => setDialog({ open: true, fast: null })}>
        Record a past fast
      </Button>
      {day.running && isToday ? (
        <Button
          variant="link"
          size="sm"
          className="mt-3 ml-1"
          onClick={() => setDialog({ open: true, fast: day.running })}
        >
          Change the running fast
        </Button>
      ) : null}

      <FastDialog
        open={dialog.open}
        onOpenChange={(open) => setDialog((d) => ({ ...d, open }))}
        fast={dialog.fast}
        defaultTo={isToday ? undefined : `${day.day}T12:00`}
        defaultFrom={isToday ? undefined : new Date(Date.parse(`${day.day}T12:00`) - 16 * HOUR * 1000).toISOString()}
      />
    </section>
  );
}

function FastRing({ seconds }: { seconds: number }) {
  const full = DIET_RULES.fastFullAtHours * HOUR;
  const fraction = Math.min(seconds / full, 1);
  const r = 26;
  const c = 2 * Math.PI * r;
  return (
    <svg
      viewBox="0 0 64 64"
      className="size-16 shrink-0 -rotate-90"
      role="img"
      aria-label={`${formatDuration(seconds)} of ${DIET_RULES.fastFullAtHours} h`}
    >
      <circle cx="32" cy="32" r={r} fill="none" strokeWidth="6" className="stroke-muted" />
      <circle
        cx="32"
        cy="32"
        r={r}
        fill="none"
        strokeWidth="6"
        strokeLinecap="round"
        className="stroke-chart-earned"
        strokeDasharray={`${c * fraction} ${c}`}
      />
    </svg>
  );
}

function RunningFast({ fast }: { fast: FastRow }) {
  const writes = useWrites();
  const now = useNow();
  const [endAt, setEndAt] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const seconds = Math.max(0, Math.floor((now - Date.parse(fast.startedAt)) / 1000));
  const shown = endAt ?? toLocalInput(now);

  async function end() {
    const at = endAt ? fromLocalInput(endAt) : undefined;
    if (at === null) return setError("Say when the fast ended.");
    setError(null);
    setPending(true);
    try {
      await writes.fast({ action: "end", at });
      setEndAt(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setPending(false);
    }
  }

  return (
    <div>
      <div className="flex items-center gap-4">
        <FastRing seconds={seconds} />
        <div>
          <p className="font-heading text-2xl">Fasting {formatDuration(seconds)}</p>
          <p className="text-sm text-muted-foreground">{fastEarningText(seconds)}</p>
          <p className="text-xs text-muted-foreground">Since {formatClockOn(fast.startedAt, toLocalInput(now).slice(0, 10))}</p>
        </div>
      </div>
      <form
        className="mt-3 flex flex-wrap items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          void end();
        }}
      >
        <div className="space-y-1">
          <Label htmlFor="fast-end-at">Ends at</Label>
          <Input
            id="fast-end-at"
            type="datetime-local"
            value={shown}
            className="w-auto"
            onChange={(e) => setEndAt(e.target.value)}
          />
        </div>
        <Button type="submit" disabled={pending} className="min-h-11 md:min-h-0">
          {pending ? "Ending…" : "End fast"}
        </Button>
      </form>
      {error ? (
        <p role="alert" className="mt-2 text-xs text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}

function BeginFast({ day }: { day: FoodDayRead }) {
  const writes = useWrites();
  const now = useNow();
  // Begin fast offers the last entry's time, when it was within the last day.
  const last = day.lastMeal && now - Date.parse(day.lastMeal.eatenAt) < 24 * HOUR * 1000 ? day.lastMeal : null;
  const [beginAt, setBeginAt] = useState(() => toLocalInput(last?.eatenAt ?? now));
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function begin() {
    const at = fromLocalInput(beginAt);
    if (!at) return setError("Say when the fast began.");
    setError(null);
    setPending(true);
    try {
      await writes.fast({ action: "begin", at });
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setPending(false);
    }
  }

  return (
    <div>
      <p className="text-sm text-muted-foreground">
        Not fasting.{last ? ` Your last entry was ${last.name} at ${formatClock(last.eatenAt)}.` : ""}
      </p>
      <form
        className="mt-3 flex flex-wrap items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          void begin();
        }}
      >
        <div className="space-y-1">
          <Label htmlFor="fast-begin-at">Began at</Label>
          <Input
            id="fast-begin-at"
            type="datetime-local"
            value={beginAt}
            className="w-auto"
            onChange={(e) => setBeginAt(e.target.value)}
          />
        </div>
        <Button type="submit" disabled={pending} className="min-h-11 md:min-h-0">
          {pending ? "Beginning…" : "Begin fast"}
        </Button>
      </form>
      {error ? (
        <p role="alert" className="mt-2 text-xs text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}

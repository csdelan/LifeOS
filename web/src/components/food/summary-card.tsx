import { useState } from "react";
import { BeerIcon, LockIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DIET_RULES, drinksLine, formatPart } from "@/lib/diet-score";
import { formatClock, summaryHeadline } from "@/lib/food";
import type { FoodDaySummary } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";

function Part({ label, value, max, detail }: { label: string; value: number; max: number; detail?: string }) {
  const share = max > 0 ? Math.min(value / max, 1) : 0;
  return (
    <div>
      <p className="flex items-baseline justify-between gap-2 text-sm">
        <span>{label}</span>
        <span className="tabular-nums text-muted-foreground">
          {formatPart(value)} / {max}
        </span>
      </p>
      <div
        role="meter"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={max}
        aria-valuenow={Math.round(value * 10) / 10}
        aria-valuetext={`${formatPart(value)} of ${max}${detail ? `, ${detail}` : ""}`}
        className="mt-1 h-2 overflow-hidden rounded-full bg-muted"
      >
        <div className="h-full rounded-full bg-chart-earned" style={{ width: `${share * 100}%` }} />
      </div>
      {detail ? <p className="mt-0.5 text-xs text-muted-foreground">{detail}</p> : null}
    </div>
  );
}

/**
 * The day's diet score (FOOD-3): before the close, what it's on track for, with its three
 * parts as meters, its calories and average score; Close day once it has an entry. After the
 * close, what it paid, at the day's multiplier. A day's drinks show under the parts, with the
 * cut drinks take (FOOD-6); a fast paid in full is re-scored when the day closes.
 */
export function SummaryCard({ summary, isToday }: { summary: FoodDaySummary; isToday: boolean }) {
  const writes = useWrites();
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const canClose = isToday && !summary.closedByHand && summary.meals > 0;

  async function close() {
    setError(null);
    setPending(true);
    try {
      await writes.closeFoodDay(summary.day);
    } catch (e) {
      setError(e instanceof Error ? e.message : "The day didn't close. Nothing was written.");
    } finally {
      setPending(false);
    }
  }

  const status = summary.closedByHand && summary.closedAt
    ? `Closed at ${formatClock(summary.closedAt)}`
    : summary.closed
      ? "Closed at midnight"
      : summary.paid.fast
        ? `Fasting paid ${summary.paid.fast.paid} Health XP; the food pays when you close the day`
        : "Pays when you close the day";

  return (
    <section aria-labelledby="diet-title" className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <h2 id="diet-title" className="type-scale-section text-muted-foreground">
        Diet XP
      </h2>
      <p className="mt-1 font-heading text-2xl" aria-live="polite">
        {summaryHeadline(summary)}
      </p>
      <p className="flex items-center gap-1 text-xs text-muted-foreground">
        {summary.closed ? <LockIcon className="size-3" aria-hidden /> : null}
        {status}
      </p>

      <div className="mt-4 space-y-3">
        <Part
          label="Quality"
          value={summary.quality}
          max={DIET_RULES.qualityMax}
          detail={summary.averageScore !== null ? `Average score ${formatPart(summary.averageScore)}` : undefined}
        />
        <Part
          label="Calories"
          value={summary.caloriesPart}
          max={DIET_RULES.caloriesMax}
          detail={`${summary.calories.toLocaleString()} cal · full from 500 to 2,000`}
        />
        <Part label="Fasting" value={summary.fasting} max={DIET_RULES.fastingMax} />
      </div>

      {summary.drinks > 0 ? (
        <p className="mt-3 flex items-start gap-1.5 text-xs">
          <BeerIcon className="mt-px size-3.5 shrink-0" aria-hidden />
          <span>
            {drinksLine(summary.drinks)}
            {summary.drinkFactor < 1 && !summary.closed && summary.paid.fast
              ? "; the fast's XP is re-scored with it when the day closes"
              : ""}
          </span>
        </p>
      ) : null}

      <p className="mt-3 text-xs text-muted-foreground">
        {summary.meals === 0
          ? "Nothing logged this day."
          : `${summary.meals} entr${summary.meals === 1 ? "y" : "ies"} · food ${summary.foodAward} + fasting ${summary.fastAward} = ${summary.total} base XP`}
      </p>

      {canClose ? (
        <Button className="mt-3 min-h-11 w-full md:min-h-0" disabled={pending} onClick={() => void close()}>
          {pending ? "Closing…" : "Close day"}
        </Button>
      ) : null}
      {error ? (
        <p role="alert" className="mt-2 text-xs text-destructive">
          {error}
        </p>
      ) : null}
    </section>
  );
}

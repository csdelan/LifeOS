import { useId } from "react";
import { Button } from "@/components/ui/button";
import { HOME_PARTS, PART_LABEL, ratedAgo, shownScore } from "@/lib/environment";
import { SCORE_FILL } from "@/lib/food";
import type { HomePartRow, HomeSpaceRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/** Points as a rating's colour on the 1–5 ramp: 72.0 is a 3.5's. */
function fillFor(points: number): string {
  const rating = Math.min(5, Math.max(1, Math.floor((1 + points / 25) * 2) / 2));
  return SCORE_FILL[rating];
}

/** A part's bar: its points now, with what it was rated as a ghost behind, so the fade shows. */
function PartBar({ label, part, today }: { label: string; part: HomePartRow; today: string }) {
  const ratedPoints = part.rated != null ? (part.rated - 1) * 25 : null;
  const words =
    part.rated == null || part.now == null
      ? `${label}: not rated yet`
      : `${label}: rated ${part.rated} ${ratedAgo(part.ratedOn, today).replace(/^rated /, "")}, now ${shownScore(part.now)} of ${shownScore(ratedPoints)}`;
  return (
    // The label and points above, the bar the card's full width below, so it stays readable
    // when three cards share a row.
    <div className="space-y-1 text-xs">
      <span className="sr-only">{words}</span>
      <div aria-hidden className="flex items-baseline justify-between gap-2">
        <span className="text-muted-foreground">{label}</span>
        <span className="tabular-nums">{part.now != null ? shownScore(part.now) : "—"}</span>
      </div>
      <div aria-hidden className="relative h-2 overflow-hidden rounded-full bg-muted" data-testid="part-bar">
        {ratedPoints != null ? (
          <div
            className={cn("absolute inset-y-0 left-0 rounded-full opacity-30", SCORE_FILL[part.rated!])}
            style={{ width: `${ratedPoints}%` }}
          />
        ) : null}
        {part.now != null ? (
          <div
            className={cn("absolute inset-y-0 left-0 rounded-full", fillFor(part.now))}
            style={{ width: `${Math.max(part.now, 1)}%` }}
          />
        ) : null}
      </div>
    </div>
  );
}

/**
 * One space today (HOME-3): its name and score, when it was last rated, and a bar for each part
 * — its points now, faded, with the rated value as a ghost behind it. Rate opens a quick check
 * of it.
 */
export function SpaceCard({
  space,
  today,
  onRate,
}: {
  space: HomeSpaceRow;
  today: string;
  onRate: () => void;
}) {
  const titleId = useId();
  return (
    <article
      aria-labelledby={titleId}
      className="space-y-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10"
      data-testid="space-card"
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 id={titleId} className="truncate font-medium">
            {space.name}
          </h3>
          <p className="text-xs text-muted-foreground">
            {ratedAgo(space.lastRatedOn, today)}
            {space.share != null ? ` · ${space.share}% of the score` : ""}
          </p>
        </div>
        <p className="text-right">
          <span className="block font-heading text-2xl leading-none tabular-nums" data-testid="space-score">
            {shownScore(space.score)}
          </span>
          {space.score == null && space.lastRatedOn != null ? (
            <span className="block text-[0.65rem] text-muted-foreground">rate all three parts</span>
          ) : null}
        </p>
      </div>
      <div className="space-y-1.5">
        {HOME_PARTS.map((p) => (
          <PartBar key={p} label={PART_LABEL[p]} part={space.parts[p]} today={today} />
        ))}
      </div>
      <Button
        type="button"
        variant="outline"
        size="sm"
        className="min-h-11 w-full md:min-h-0"
        aria-label={`Rate ${space.name}`}
        onClick={onRate}
      >
        Rate
      </Button>
    </article>
  );
}

import { useId } from "react";
import { WrenchIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { barPlus } from "@/lib/domains";
import { dragText, shownScore } from "@/lib/environment";
import type { EnvironmentRead } from "@/lib/production-ui-types";

/**
 * Today's bar for the level going into today (environment plan §2.3): "68.2+ keeps level 70 on
 * track". The bar is shown rounded up, so a score at the shown bar always reaches it.
 */
export interface LevelBar {
  /** The Home Score today must reach, in points. */
  score: number;
  level: number;
}

/**
 * The Home Score today (HOME-3), floored to a tenth, with how many spaces count, and Fix next:
 * the three parts that cost the score most, each opening a quick check of its space.
 */
export function HomeScoreCard({
  home,
  bar,
  onRate,
}: {
  home: EnvironmentRead;
  /** Today's bar for the level going in, said beside the score. */
  bar?: LevelBar;
  onRate: (spaceId: string) => void;
}) {
  const titleId = useId();
  const fixId = useId();
  const counted = home.spaces.filter((s) => s.score != null).length;

  return (
    <section aria-labelledby={titleId} className="space-y-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <div>
        <h2 id={titleId} className="type-scale-section text-muted-foreground">
          Home Score
        </h2>
        <p className="flex flex-wrap items-baseline gap-x-2">
          <span className="font-heading text-5xl tabular-nums" data-testid="home-score">
            {shownScore(home.homeScore)}
          </span>
          {bar ? (
            <span className="text-sm text-muted-foreground" data-testid="home-score-bar">
              · {barPlus(bar.score)} keeps level {bar.level} on track
            </span>
          ) : null}
        </p>
        <p className="text-xs text-muted-foreground">
          {home.homeScore == null
            ? "No score yet: rate all three parts of a space to start one."
            : `${counted} of ${home.spaces.length} space${home.spaces.length === 1 ? "" : "s"} counted, out of 100`}
        </p>
      </div>

      {home.fixNext.length > 0 ? (
        <div className="space-y-2">
          <h3 id={fixId} className="flex items-center gap-1.5 text-sm font-medium">
            <WrenchIcon aria-hidden className="size-4 text-muted-foreground" />
            Fix next
          </h3>
          <ol aria-labelledby={fixId} className="space-y-1.5" data-testid="fix-next">
            {home.fixNext.map((row) => (
              <li key={`${row.spaceId}-${row.part}`} className="flex items-center justify-between gap-2">
                <span className="text-sm">
                  {row.space} {row.part}{" "}
                  <span className="font-semibold tabular-nums">{dragText(row.drag)}</span>
                </span>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="min-h-11 md:min-h-0"
                  aria-label={`Rate ${row.space} ${row.part}`}
                  onClick={() => onRate(row.spaceId)}
                >
                  Rate
                </Button>
              </li>
            ))}
          </ol>
        </div>
      ) : null}
    </section>
  );
}

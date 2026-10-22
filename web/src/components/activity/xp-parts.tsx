import { XP_PARTS, dayXpWords, partXpText, partXpWords, type DayXp, type PartXp } from "@/lib/activity-xp";
import { cn } from "@/lib/utils";

/** "×1.3". */
function times(multiplier: number): string {
  return `×${multiplier.toFixed(1)}`;
}

/** One part's figure: what it paid, what it's projected to pay, or a dash. */
function PartFigure({ part }: { part: PartXp }) {
  const max = XP_PARTS.find((p) => p.part === part.part)!.max;
  if (part.state === "none") return <span className="text-muted-foreground">—</span>;
  return (
    <span className="tabular-nums">
      <span className={cn("font-semibold", part.state === "projected" && "text-muted-foreground")}>{part.xp}</span>
      <span className="text-muted-foreground">
        {part.state === "paid"
          ? part.multiplier
            ? ` earned (${part.base} ${times(part.multiplier)})`
            : ` of ${max} earned`
          : ` of ${max} projected`}
      </span>
    </span>
  );
}

/**
 * A day's activity XP part by part (ACTIVITY-5, activity plan §3.8): steps, exercise and sleep,
 * each earned (on the ledger, multiplier included) or projected (not paid yet, never shown as
 * earned), with when it pays or why it doesn't, and the day's total in words.
 */
export function ActivityXpList({ xp, title }: { xp: DayXp; title: string }) {
  return (
    <section aria-label={title} className="space-y-2">
      <div className="flex items-baseline justify-between gap-2">
        <h3 className="text-sm font-medium">{title}</h3>
        <p className="text-xs text-muted-foreground" data-testid="activity-xp-total">
          {dayXpWords(xp)}
        </p>
      </div>
      {xp.pays ? (
        <dl className="space-y-1.5">
          {xp.parts.map((p) => (
            <div
              key={p.part}
              data-testid={`activity-xp-${p.part}`}
              title={partXpWords(p)}
              className="grid grid-cols-[1fr_auto] items-baseline gap-x-2 text-sm"
            >
              <dt>{p.label}</dt>
              <dd className="text-right">
                <PartFigure part={p} />
              </dd>
              {p.why ? <dd className="col-span-2 text-xs text-muted-foreground">{p.why}</dd> : null}
            </div>
          ))}
        </dl>
      ) : null}
    </section>
  );
}

/** A part's XP in a table cell: what it paid, or what it's projected to pay, muted. */
export function XpCell({ part }: { part: PartXp }) {
  if (part.state === "none") return <span className="text-muted-foreground">—</span>;
  return (
    <span className={cn(part.state === "projected" && "text-muted-foreground")} title={partXpWords(part)}>
      {partXpText(part)}
    </span>
  );
}

import type { LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";

export type ActivityTone = "today" | "steps" | "sleep" | "exercise" | "tags";

// Each class written out in full, so Tailwind finds it.
const TONE: Record<ActivityTone, { cap: string; tile: string }> = {
  today: { cap: "border-t-activity-today", tile: "bg-activity-today/12 text-activity-today" },
  steps: { cap: "border-t-activity-steps", tile: "bg-activity-steps/12 text-activity-steps" },
  sleep: { cap: "border-t-activity-sleep", tile: "bg-activity-sleep/12 text-activity-sleep" },
  exercise: { cap: "border-t-activity-exercise", tile: "bg-activity-exercise/12 text-activity-exercise" },
  tags: { cap: "border-t-activity-tags", tile: "bg-activity-tags/12 text-activity-tags" },
};

/**
 * One of the Activity page's cards: a region named by its title, with the card's colour along
 * its top so the cards tell apart while scrolling.
 */
export function ActivityCard({
  tone,
  titleId,
  className,
  children,
  ...rest
}: Omit<React.ComponentProps<"section">, "aria-labelledby"> & { tone: ActivityTone; titleId: string }) {
  return (
    <section
      aria-labelledby={titleId}
      data-tone={tone}
      className={cn("space-y-4 rounded-xl border-t-4 bg-card p-4 ring-1 ring-foreground/10", TONE[tone].cap, className)}
      {...rest}
    >
      {children}
    </section>
  );
}

/**
 * A card's title: its icon on a tile of the card's colour, the name large, an optional line
 * under it, and whatever sits beside it (a range switch, the day's arrows). `live` reads a
 * changing title aloud, as the Today card's does when it steps to another day.
 */
export function ActivityCardTitle({
  id,
  tone,
  icon: Icon,
  title,
  description,
  live = false,
  children,
}: {
  id: string;
  tone: ActivityTone;
  icon: LucideIcon;
  title: string;
  description?: string;
  live?: boolean;
  children?: React.ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
      <div className="flex min-w-0 items-center gap-3">
        <span aria-hidden className={cn("grid size-10 shrink-0 place-items-center rounded-lg", TONE[tone].tile)}>
          <Icon className="size-5" />
        </span>
        <div className="min-w-0">
          <h2 id={id} aria-live={live ? "polite" : undefined} className="font-heading text-2xl leading-tight">
            {title}
          </h2>
          {description ? <p className="text-xs text-muted-foreground">{description}</p> : null}
        </div>
      </div>
      {children}
    </div>
  );
}

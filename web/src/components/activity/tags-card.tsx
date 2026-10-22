import { useId, useState } from "react";
import { ChevronDownIcon, TagIcon } from "lucide-react";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { Button } from "@/components/ui/button";
import {
  dayName,
  METRIC_RANGES,
  tagCounts,
  tagDays,
  tagRangeWords,
  tagsFrom,
  tagWhen,
  type MetricRange,
} from "@/lib/activity";
import type { ActivitySummaryRead, ActivityTagRow } from "@/lib/production-ui-types";
import { useActivityTags } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

const VIEW = "activity.tags";

/** How many days with tags show before "Show more days". */
export const TAG_DAYS_SHOWN = 7;

/** One tag: its name, when (a time, or start–end with its length), and its comment on its own line. */
export function TagLine({ tag }: { tag: ActivityTagRow }) {
  return (
    <span className="flex items-start gap-3">
      <TagIcon className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
          <span className="font-medium">{tag.name}</span>
          <span className="text-xs text-muted-foreground tabular-nums">{tagWhen(tag)}</span>
        </span>
        {tag.comment ? (
          <span data-testid="tag-comment" className="mt-0.5 block text-sm text-foreground/90">
            {tag.comment}
          </span>
        ) : null}
      </span>
    </span>
  );
}

/**
 * Tags (ACTIVITY-6, activity plan §3.9): what Chris tagged in the Oura app over 30 days, 90
 * days or a year: how often each tag came up, most used first, then the tags newest first by
 * the day they start. A day's heading opens the day's sheet, next to that day's sleep,
 * readiness and steps. Read-only: tags are added in the Oura app.
 */
export function TagsCard({
  summary,
  onOpenDay,
}: {
  summary: ActivitySummaryRead;
  onOpenDay: (day: string) => void;
}) {
  const [range, setRangeState] = useState<MetricRange>(() => loadView<{ range: MetricRange }>(VIEW, { range: "30" }).range);
  const [showAll, setShowAll] = useState(false);
  const titleId = useId();
  const listId = useId();
  const today = summary.today;
  const tags = useActivityTags(tagsFrom(range, today), today);
  const notAllowed = summary.oura.missingScopes.some((s) => s.trim().toLowerCase() === "tag");

  const setRange = (next: MetricRange) => {
    setRangeState(next);
    setShowAll(false);
    try {
      saveView(VIEW, { range: next });
    } catch {
      // Storage can be full or blocked; the range still applies to this view.
    }
  };

  const all = tags.data?.tags ?? [];
  const counts = tagCounts(all);
  const days = tagDays(all);
  const visible = showAll ? days : days.slice(0, TAG_DAYS_SHOWN);
  const hidden = days.length - visible.length;

  return (
    <section
      aria-labelledby={titleId}
      data-testid="tags-card"
      className="space-y-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id={titleId} className="type-scale-section text-muted-foreground">
          Tags
        </h2>
        <ToggleGroup label="Range" value={range} onChange={setRange} options={[...METRIC_RANGES]} />
      </div>

      {tags.isLoading ? <ListSkeleton rows={3} /> : null}
      {tags.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The tags failed to load.
        </p>
      ) : null}

      {tags.data ? (
        <div
          aria-busy={tags.isPlaceholderData || undefined}
          className={cn("space-y-4 transition-opacity", tags.isPlaceholderData && "opacity-60")}
        >
          {all.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {notAllowed
                ? "Oura didn't allow tags, so LifeOS can't read them. Reconnect Oura and allow tags to see them here."
                : `No tags in ${tagRangeWords(range)}. Tags you add in the Oura app, like a coffee, a drink or the sauna, show here after a sync.`}
            </p>
          ) : (
            <>
              {notAllowed ? (
                <p className="text-sm text-muted-foreground">
                  Oura didn't allow tags, so new ones won't show until you reconnect Oura and allow them.
                </p>
              ) : null}
              <p data-testid="tag-counts" className="text-sm">
                <span className="sr-only">How often each tag came up in {tagRangeWords(range)}: </span>
                {counts.map((c, i) => (
                  <span key={c.name}>
                    {i > 0 ? <span className="text-muted-foreground"> · </span> : null}
                    {c.name} <span className="font-medium tabular-nums">{c.count}</span>
                  </span>
                ))}
              </p>

              <div id={listId} className="space-y-4">
                {visible.map(({ day, tags: dayTags }) => (
                  <div key={day} data-testid="tag-day">
                    <h3 className="flex items-baseline justify-between gap-2 text-sm font-medium">
                      <button
                        type="button"
                        onClick={() => onOpenDay(day)}
                        className="-mx-1 min-h-11 rounded px-1 text-left underline-offset-4 hover:underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                      >
                        {dayName(day, today)}
                        <span className="sr-only">, open the day</span>
                      </button>
                      <span className="text-xs font-normal text-muted-foreground tabular-nums">
                        {dayTags.length} tag{dayTags.length === 1 ? "" : "s"}
                      </span>
                    </h3>
                    <ul className="space-y-2">
                      {dayTags.map((t) => (
                        <li key={t.id} data-testid="tag-row">
                          <TagLine tag={t} />
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>

              {days.length > TAG_DAYS_SHOWN ? (
                <Button
                  variant="ghost"
                  size="sm"
                  aria-expanded={showAll}
                  aria-controls={listId}
                  onClick={() => setShowAll((s) => !s)}
                >
                  <ChevronDownIcon className={cn("transition-transform", showAll && "rotate-180")} aria-hidden />
                  {showAll ? "Show fewer days" : `Show ${hidden} more day${hidden === 1 ? "" : "s"}`}
                </Button>
              ) : null}
            </>
          )}
        </div>
      ) : null}
    </section>
  );
}

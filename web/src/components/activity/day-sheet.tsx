import { useRef } from "react";
import { MoonIcon } from "lucide-react";
import { ContributorMeter, NightFacts, ScoreFigure, StageBar } from "@/components/activity/sleep-parts";
import { TagLine } from "@/components/activity/tags-card";
import { ActivityXpList } from "@/components/activity/xp-parts";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import {
  bedToWake,
  dayName,
  formatDuration,
  formatTemperature,
  orderedContributors,
  READINESS_CONTRIBUTORS,
  SLEEP_CONTRIBUTORS,
} from "@/lib/activity";
import { dayXp } from "@/lib/activity-xp";
import { useIsDesktop } from "@/lib/media-query";
import type { ActivityDayDetailRead } from "@/lib/production-ui-types";
import { useActivityDay } from "@/lib/queries";
import { cn } from "@/lib/utils";

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-medium">{title}</h3>
      {children}
    </section>
  );
}

function Contributors({ label, values, order }: { label: string; values: Record<string, number | null>; order: readonly string[] }) {
  const rows = orderedContributors(values, order);
  if (rows.length === 0) return <p className="text-sm text-muted-foreground">Oura gave no contributors.</p>;
  return (
    <ul aria-label={label} className="space-y-2.5">
      {rows.map((c) => (
        <ContributorMeter key={c.key} name={c.name} value={c.value} />
      ))}
    </ul>
  );
}

function DayDetail({ detail }: { detail: ActivityDayDetailRead }) {
  const { summary: day, night, naps } = detail;
  const nothing = day.sleepScore == null && day.readinessScore == null && night == null && naps.length === 0;
  const xp = dayXp(day, detail.workouts);
  const health = xp.pays ? <ActivityXpList xp={xp} title="Health XP" /> : null;
  // Tags from the Oura app starting this day, earliest first (activity plan §3.9).
  const tags =
    detail.tags.length > 0 ? (
      <Section title={detail.tags.length === 1 ? "A tag" : `${detail.tags.length} tags`}>
        <ul aria-label="Tags from the Oura app" className="space-y-2 text-sm">
          {detail.tags.map((t) => (
            <li key={t.id} data-testid="day-tag">
              <TagLine tag={t} />
            </li>
          ))}
        </ul>
      </Section>
    ) : null;
  if (nothing) {
    return (
      <div className="space-y-6">
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <MoonIcon className="size-4" aria-hidden />
          {detail.day === detail.today
            ? "Last night hasn't reached LifeOS yet: open the Oura app to sync it."
            : "Oura has nothing for this night."}
        </p>
        {tags}
        {health}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <dl className="grid grid-cols-2 gap-3">
        <ScoreFigure label="Sleep score" value={day.sleepScore} large />
        <ScoreFigure label="Readiness" value={day.readinessScore} large />
      </dl>

      <Section title="The night">
        {night ? (
          <div className="space-y-3">
            <StageBar period={night} />
            <NightFacts night={night} />
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">Oura has no night for this day.</p>
        )}
      </Section>

      {day.sleepScore != null ? (
        <Section title="What made the sleep score">
          <Contributors label="Sleep score contributors" values={detail.sleepContributors} order={SLEEP_CONTRIBUTORS} />
        </Section>
      ) : null}

      {day.readinessScore != null ? (
        <Section title="What made readiness">
          <Contributors
            label="Readiness contributors"
            values={detail.readinessContributors}
            order={READINESS_CONTRIBUTORS}
          />
          {detail.temperatureDeviation != null ? (
            <p className="text-sm text-muted-foreground">
              Body temperature {formatTemperature(detail.temperatureDeviation)} from your usual
              {detail.temperatureTrendDeviation != null
                ? `; the trend ${formatTemperature(detail.temperatureTrendDeviation)}`
                : ""}
              .
            </p>
          ) : null}
        </Section>
      ) : null}

      {naps.length > 0 ? (
        <Section title={naps.length === 1 ? "A nap" : `${naps.length} naps`}>
          <ul className="divide-y text-sm">
            {naps.map((n) => (
              <li key={n.id} data-testid="nap" className="flex justify-between gap-4 py-1.5">
                <span>{bedToWake(n)}</span>
                <span className="tabular-nums text-muted-foreground">
                  {n.asleepS != null ? `${formatDuration(n.asleepS)} asleep` : "—"}
                </span>
              </li>
            ))}
          </ul>
        </Section>
      ) : null}

      {tags}

      {health}

      <p className="text-xs text-muted-foreground">
        A night counts on the morning it ends. Scores and contributors are Oura's: 85 and up is optimal, 70 good.
      </p>
    </div>
  );
}

/**
 * One day in full (activity plan §3.7): both scores, the night ending that morning with its
 * stages and heart, each score's contributors as small meters, the temperature, the naps, the
 * tags from the Oura app (A10), and the day's Health XP from steps, exercise and sleep, earned
 * or projected (A9).
 */
export function DaySheet({
  day,
  today,
  open,
  onOpenChange,
}: {
  day: string | null;
  today: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const desktop = useIsDesktop();
  const content = useRef<HTMLDivElement>(null);
  const detail = useActivityDay(day, open);
  const night = detail.data?.day === day ? detail.data?.night : null;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        ref={content}
        side={desktop ? "right" : "bottom"}
        className={cn("gap-0 overflow-y-auto", desktop ? "w-full sm:max-w-md" : "max-h-[92dvh] rounded-t-2xl")}
        onOpenAutoFocus={(e) => {
          e.preventDefault();
          content.current?.focus();
        }}
      >
        <SheetHeader className="pb-2">
          <SheetTitle>{day ? dayName(day, today) : "A day"}</SheetTitle>
          <SheetDescription>
            {day === today ? "Last night, ending this morning" : "The night ending that morning"}
            {night ? `: ${bedToWake(night)}` : ""}
          </SheetDescription>
        </SheetHeader>
        <div className="px-4 pb-6">
          {detail.isLoading ? <ListSkeleton rows={4} /> : null}
          {detail.isError ? (
            <p role="alert" className="text-sm text-destructive">
              This day failed to load.
            </p>
          ) : null}
          {detail.data && detail.data.day === day ? <DayDetail key={day} detail={detail.data} /> : null}
        </div>
      </SheetContent>
    </Sheet>
  );
}

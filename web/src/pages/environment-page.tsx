import { useState } from "react";
import { HouseIcon, ListChecksIcon } from "lucide-react";
import { HomeScoreCard, type LevelBar } from "@/components/environment/home-score-card";
import { HomeScoreChart, type LevelBarLine } from "@/components/environment/home-score-chart";
import { SpaceCard } from "@/components/environment/space-card";
import { SpacesEditor } from "@/components/environment/spaces-editor";
import { WalkThroughSheet } from "@/components/environment/walk-through-sheet";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import type { DomainDaysRead, DomainRow } from "@/lib/production-ui-types";
import { useDomainDays, useDomains, useEnvironment } from "@/lib/queries";

/** The chart's longest range: a year of Environment's days carries the bar for every range. */
const BAR_DAYS = 365;

/** The shown level's bar (environment plan §2.3): the Home Score that keeps it on track. */
function shownBar(row: DomainRow | undefined): LevelBar | undefined {
  const shown = row?.shown;
  return shown?.bar != null ? { score: shown.bar, level: shown.level } : undefined;
}

/** Each day's bar, set by its level going in: the score that day had to reach to be kept up. */
function barLine(read: DomainDaysRead | undefined): LevelBarLine | undefined {
  if (!read || read.kind !== "home") return undefined;
  const byDay: Record<string, number> = {};
  for (const d of read.days) if (d.bar != null) byDay[d.day] = d.bar;
  return Object.keys(byDay).length > 0 ? { label: "The bar for your level", byDay } : undefined;
}

/**
 * The Environment page (HOME-1..3, D18, environment plan §4.8), phone-first:
 *
 * - The Home Score today, and Fix next: the three parts that cost it most.
 * - A card for each space: its score, a bar for each part (faded, with the rated value as a
 *   ghost), when it was last rated, and Rate, a quick check of that space alone.
 * - Walk-through opens a sheet with every space; one Save writes them all.
 * - The Home Score by day, with exception days hatched, and the spaces editor.
 *
 * Environment's level is held by the home (E3), so the bar a day must reach to keep the level
 * on track is said beside the score and drawn as a dashed step line on the chart.
 */
export function EnvironmentPage() {
  const home = useEnvironment();
  const bar = shownBar(useDomains().data?.domains.find((d) => d.key === "environment_xp"));
  const line = barLine(useDomainDays("environment_xp", BAR_DAYS).data);
  const [sheet, setSheet] = useState<{ open: boolean; only: string | null }>({ open: false, only: null });

  const data = home.data;
  const hasSpaces = (data?.spaces.length ?? 0) > 0;
  const walkThrough = () => setSheet({ open: true, only: null });
  const rate = (spaceId: string) => setSheet({ open: true, only: spaceId });

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="shrink-0 border-b px-4 py-4 md:px-6">
        <p className="type-scale-section text-primary">Environment</p>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="font-heading text-2xl">Your home</h1>
          <Button className="hidden md:inline-flex" disabled={!hasSpaces} onClick={walkThrough}>
            <ListChecksIcon />
            Walk-through
          </Button>
        </div>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-5 md:px-6">
        {home.isLoading ? (
          <div className="grid gap-4 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)]">
            <CardSkeleton />
            <CardSkeleton />
          </div>
        ) : null}
        {home.isError ? (
          <EmptyState
            tone="error"
            icon={HouseIcon}
            title="The home failed to load"
            description="This is not an empty home — the reader never returned."
          />
        ) : null}

        {data && !hasSpaces ? (
          <div className="mx-auto max-w-xl space-y-4">
            <EmptyState
              icon={HouseIcon}
              title="No spaces yet"
              description="Add the home's spaces, each with a weight and a pace, then do a walk-through to start the Home Score."
            />
            <SpacesEditor />
          </div>
        ) : null}

        {data && hasSpaces ? (
          // On a desktop the score and the editor share the left column; on a phone the editor
          // comes last, after the spaces and the chart.
          <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)] lg:grid-rows-[auto_1fr]">
            <HomeScoreCard home={data} bar={bar} onRate={rate} />
            <div className="min-w-0 space-y-4 lg:col-start-2 lg:row-span-2 lg:row-start-1">
              <section aria-labelledby="spaces-title">
                <h2 id="spaces-title" className="type-scale-section mb-3 text-muted-foreground">
                  Spaces
                </h2>
                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                  {data.spaces.map((space) => (
                    <SpaceCard key={space.id} space={space} today={data.today} onRate={() => rate(space.id)} />
                  ))}
                </div>
              </section>
              <HomeScoreChart levelBar={line} />
            </div>
            <div className="lg:col-start-1 lg:row-start-2">
              <SpacesEditor />
            </div>
          </div>
        ) : null}
      </div>

      <div className="shrink-0 border-t bg-background px-4 py-3 md:hidden">
        <Button className="min-h-11 w-full" disabled={!hasSpaces} onClick={walkThrough}>
          <ListChecksIcon />
          Walk-through
        </Button>
      </div>

      {data ? (
        <WalkThroughSheet
          open={sheet.open}
          onOpenChange={(open) => setSheet((s) => ({ ...s, open }))}
          spaces={data.spaces}
          today={data.today}
          only={sheet.only}
        />
      ) : null}
    </div>
  );
}

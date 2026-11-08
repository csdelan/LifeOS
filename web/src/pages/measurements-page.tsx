import { useId, useState } from "react";
import { PlusIcon, WeightIcon } from "lucide-react";
import { AddMetricDialog } from "@/components/measurements/add-metric-dialog";
import { LogReadingSheet } from "@/components/measurements/log-reading-sheet";
import { MetricCard } from "@/components/measurements/metric-card";
import { ArchivedMetrics, MetricListRow } from "@/components/measurements/metric-list";
import { StartTrackingCard } from "@/components/measurements/start-tracking-card";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { todayIso } from "@/lib/dates";
import {
  WEIGHT,
  findCatalog,
  groupMetrics,
  singleGroup,
  type MetricGroup,
  type ReadingSet,
} from "@/lib/measurements";
import { useMetrics } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";

const OPEN_VIEW = "measurements.open";

/**
 * The Measurements page (MEASURE-1 to MEASURE-3, D20, measurements plan §4.7–§4.8), phone-first.
 * Weight's card is first: Start tracking weight until there's a Weight metric, then the trend,
 * the goal date and the chart, with Log weight in the header on a desktop and at the bottom on a
 * phone. Every other metric is a row under Your metrics, in starter-list order then his own by
 * name, blood pressure's two as one; a row opens its card in place and has its own Log. Add a
 * metric starts another, and archived ones wait at the foot with Restore. An archived Weight
 * shows as not tracked: Start tracking makes a new one.
 */
export function MeasurementsPage() {
  const metrics = useMetrics();
  const yoursId = useId();
  const [sheet, setSheet] = useState<{ group: MetricGroup; readings: ReadingSet | null } | null>(null);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [adding, setAdding] = useState(false);
  const [open, setOpenState] = useState<Set<string>>(
    () => new Set(loadView<{ keys: string[] }>(OPEN_VIEW, { keys: [] }).keys),
  );

  const all = metrics.data ?? [];
  const weight = findCatalog(all, WEIGHT.key);
  const tracking = weight != null && weight.archivedAt == null;
  const weightGroup = tracking ? singleGroup(weight) : null;
  const groups = groupMetrics(all.filter((m) => m.archivedAt == null && m.id !== weight?.id));
  const archived = groupMetrics(all.filter((m) => m.archivedAt != null));

  const log = (group: MetricGroup, readings: ReadingSet | null = null) => {
    setSheet({ group, readings });
    setSheetOpen(true);
  };
  const setOpen = (key: string, on: boolean) => {
    setOpenState((now) => {
      const next = new Set(now);
      if (on) next.add(key);
      else next.delete(key);
      try {
        saveView(OPEN_VIEW, { keys: [...next] });
      } catch {
        // Storage can be full or blocked; the row still opens.
      }
      return next;
    });
  };

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="shrink-0 border-b px-4 py-4 md:px-6">
        <p className="type-scale-section text-primary">Measurements</p>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="font-heading text-2xl">Your measurements</h1>
          {weightGroup ? (
            <Button className="hidden md:inline-flex" onClick={() => log(weightGroup)}>
              <PlusIcon />
              Log weight
            </Button>
          ) : null}
        </div>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-5 md:px-6">
        <div className="mx-auto max-w-4xl space-y-6">
          {metrics.isLoading ? <CardSkeleton /> : null}
          {metrics.isError ? (
            <EmptyState
              tone="error"
              icon={WeightIcon}
              title="Measurements failed to load"
              description="This is not an empty list — the reader never returned."
            />
          ) : null}
          {metrics.data && !tracking ? <StartTrackingCard /> : null}
          {weightGroup ? (
            <MetricCard
              group={weightGroup}
              layout="card"
              onLog={() => log(weightGroup)}
              onChange={(set) => log(weightGroup, set)}
            />
          ) : null}

          {metrics.data ? (
            <section aria-labelledby={yoursId} className="space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 id={yoursId} className="font-heading text-xl">
                  Your metrics
                </h2>
                <Button type="button" variant="outline" className="min-h-11 md:min-h-0" onClick={() => setAdding(true)}>
                  <PlusIcon />
                  Add a metric
                </Button>
              </div>
              {groups.length > 0 ? (
                <ul className="space-y-2" aria-label="Your metrics">
                  {groups.map((group) => (
                    <MetricListRow
                      key={group.key}
                      group={group}
                      open={open.has(group.key)}
                      onToggle={() => setOpen(group.key, !open.has(group.key))}
                      onLog={() => log(group)}
                      onChange={(set) => log(group, set)}
                    />
                  ))}
                </ul>
              ) : (
                <p className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">
                  Track blood pressure, your waist, body fat, a lab VO2 max, or anything of your own: each gets its
                  trend, a goal if you want one, and a normal range that marks a reading High or Low.
                </p>
              )}
            </section>
          ) : null}

          {archived.length > 0 ? <ArchivedMetrics groups={archived} /> : null}
        </div>
      </div>

      {weightGroup ? (
        <div className="shrink-0 border-t bg-background px-4 py-3 md:hidden">
          <Button className="min-h-11 w-full" onClick={() => log(weightGroup)}>
            <PlusIcon />
            Log weight
          </Button>
        </div>
      ) : null}

      {sheet ? (
        <LogReadingSheet
          open={sheetOpen}
          onOpenChange={setSheetOpen}
          group={sheet.group}
          readings={sheet.readings}
          today={todayIso()}
        />
      ) : null}
      <AddMetricDialog
        open={adding}
        onOpenChange={setAdding}
        metrics={all}
        onAdded={(key) => setOpen(key, true)}
      />
    </div>
  );
}

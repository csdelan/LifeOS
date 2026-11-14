import { useId, useState } from "react";
import { PlusIcon, WeightIcon } from "lucide-react";
import { AddMetricDialog } from "@/components/measurements/add-metric-dialog";
import { LabResults } from "@/components/measurements/lab-results";
import { LabVisitSheet } from "@/components/measurements/lab-visit-sheet";
import { LogReadingSheet } from "@/components/measurements/log-reading-sheet";
import { MetricCard } from "@/components/measurements/metric-card";
import { ArchivedMetrics, MetricListRow } from "@/components/measurements/metric-list";
import { StartTrackingCard } from "@/components/measurements/start-tracking-card";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { todayIso } from "@/lib/dates";
import {
  WEIGHT_KEY,
  findCatalog,
  groupMetrics,
  singleGroup,
  templateOf,
  type MetricGroup,
  type ReadingSet,
} from "@/lib/features/measurements/measurements";
import type { LabVisitRow } from "@/lib/features/measurements/types";
import { useLabVisits, useMetricCatalog, useMetrics } from "@/lib/features/measurements/queries";
import { loadView, saveView } from "@/lib/view-state";
import { ReadFailed } from "@/components/primitives/read-state";

const OPEN_VIEW = "measurements.open";

/**
 * The Measurements page (MEASURE-1 to MEASURE-4, D20, measurements plan §4.7–§4.9), phone-first.
 * Weight's card is first: Start tracking weight until there's a Weight metric, then the trend,
 * the goal date and the chart, with Log weight in the header on a desktop and at the bottom on a
 * phone. Every other metric is a row under Your metrics, in starter-list order then his own by
 * name, blood pressure's two as one; a row opens its card in place and has its own Log. Add a
 * metric starts another. Lab results follow (§4.9): the visits and the lab tests by panel, which
 * stay out of Your metrics. Archived metrics wait at the foot with Restore. An archived Weight
 * shows as not tracked: Start tracking makes a new one. The starter list comes from the API (code
 * review CR73), and the page waits for it with the metrics: it orders the rows, fills in Add a
 * metric and the lab visit sheet, and names Weight's unit.
 */
export function MeasurementsPage() {
  const metrics = useMetrics();
  const catalog = useMetricCatalog();
  const visits = useLabVisits();
  const yoursId = useId();
  const [sheet, setSheet] = useState<{ group: MetricGroup; readings: ReadingSet | null } | null>(null);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [adding, setAdding] = useState(false);
  const [visitSheet, setVisitSheet] = useState<{ visit: LabVisitRow | null } | null>(null);
  const [visitOpen, setVisitOpen] = useState(false);
  const [open, setOpenState] = useState<Set<string>>(
    () => new Set(loadView<{ keys: string[] }>(OPEN_VIEW, { keys: [] }).keys),
  );

  const all = metrics.data ?? [];
  const templates = catalog.data ?? [];
  const ready = metrics.data != null && catalog.data != null;
  const weightTemplate = templateOf(templates, WEIGHT_KEY);
  const weight = findCatalog(all, WEIGHT_KEY);
  const tracking = weight != null && weight.archivedAt == null;
  const weightGroup = tracking ? singleGroup(weight) : null;
  const groups = groupMetrics(
    all.filter((m) => m.archivedAt == null && m.id !== weight?.id && m.panel == null),
    templates,
  );
  const archived = groupMetrics(
    all.filter((m) => m.archivedAt != null),
    templates,
  );

  const log = (group: MetricGroup, readings: ReadingSet | null = null) => {
    setSheet({ group, readings });
    setSheetOpen(true);
  };
  const openVisit = (visit: LabVisitRow | null) => {
    setVisitSheet({ visit });
    setVisitOpen(true);
  };
  const setOpen = (key: string, on: boolean) => {
    setOpenState((now) => {
      const next = new Set(now);
      if (on) next.add(key);
      else next.delete(key);
      saveView(OPEN_VIEW, { keys: [...next] });
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
          {metrics.isLoading || catalog.isLoading ? <CardSkeleton /> : null}
          <ReadFailed
            read={[metrics, catalog]}
            what="Measurements"
            notEmpty="an empty list"
            size="page"
            icon={WeightIcon}
          />
          {ready && !tracking && weightTemplate ? <StartTrackingCard weight={weightTemplate} /> : null}
          {weightGroup ? (
            <MetricCard
              group={weightGroup}
              layout="card"
              onLog={() => log(weightGroup)}
              onChange={(set) => log(weightGroup, set)}
            />
          ) : null}

          {ready ? (
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

          {ready ? (
            <LabResults
              metrics={all}
              catalog={templates}
              isOpen={(key) => open.has(key)}
              onToggle={(key) => setOpen(key, !open.has(key))}
              onNewVisit={() => openVisit(null)}
              onEditVisit={openVisit}
            />
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
      {visitSheet ? (
        <LabVisitSheet
          open={visitOpen}
          onOpenChange={setVisitOpen}
          visit={visitSheet.visit}
          visits={visits.data?.visits ?? []}
          metrics={all}
          catalog={templates}
          today={visits.data?.today ?? todayIso()}
        />
      ) : null}
      <AddMetricDialog
        open={adding}
        onOpenChange={setAdding}
        metrics={all}
        catalog={templates}
        onAdded={(key) => setOpen(key, true)}
      />
    </div>
  );
}

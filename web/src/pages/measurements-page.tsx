import { useState } from "react";
import { PlusIcon, WeightIcon } from "lucide-react";
import { LogReadingSheet } from "@/components/measurements/log-reading-sheet";
import { StartTrackingCard } from "@/components/measurements/start-tracking-card";
import { WeightCard } from "@/components/measurements/weight-card";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { todayIso } from "@/lib/dates";
import { WEIGHT, findCatalog } from "@/lib/measurements";
import type { MetricReadingRow } from "@/lib/production-ui-types";
import { useMetrics } from "@/lib/queries";

/**
 * The Measurements page (MEASURE-1, MEASURE-2, D20, measurements plan §4.7), phone-first. Its
 * first card is Weight: Start tracking weight until there's a Weight metric, then the trend,
 * the goal date and the chart. Log weight sits in the header on a desktop and at the bottom on
 * a phone, and opens the sheet; the table's Change opens the same sheet on a weigh-in. An
 * archived Weight shows as not tracked: Start tracking makes a new one.
 */
export function MeasurementsPage() {
  const metrics = useMetrics();
  const [sheet, setSheet] = useState<{ open: boolean; reading: MetricReadingRow | null }>({ open: false, reading: null });

  const weight = metrics.data ? findCatalog(metrics.data, WEIGHT.key) : undefined;
  const tracking = weight != null && weight.archivedAt == null;
  const log = () => setSheet({ open: true, reading: null });

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="shrink-0 border-b px-4 py-4 md:px-6">
        <p className="type-scale-section text-primary">Measurements</p>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="font-heading text-2xl">Your measurements</h1>
          {tracking ? (
            <Button className="hidden md:inline-flex" onClick={log}>
              <PlusIcon />
              Log weight
            </Button>
          ) : null}
        </div>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-5 md:px-6">
        <div className="mx-auto max-w-4xl space-y-4">
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
          {weight && tracking ? (
            <WeightCard metric={weight} onLog={log} onChange={(reading) => setSheet({ open: true, reading })} />
          ) : null}
        </div>
      </div>

      {tracking ? (
        <div className="shrink-0 border-t bg-background px-4 py-3 md:hidden">
          <Button className="min-h-11 w-full" onClick={log}>
            <PlusIcon />
            Log weight
          </Button>
        </div>
      ) : null}

      {weight && tracking ? (
        <LogReadingSheet
          open={sheet.open}
          onOpenChange={(open) => setSheet((s) => ({ ...s, open }))}
          metric={weight}
          reading={sheet.reading}
          today={todayIso()}
          noun="weigh-in"
        />
      ) : null}
    </div>
  );
}

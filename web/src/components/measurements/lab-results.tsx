import { useId } from "react";
import { ChevronRightIcon, ExternalLinkIcon, FileTextIcon, PencilIcon, PlusIcon } from "lucide-react";
import { MetricListRow } from "@/components/measurements/metric-list";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { openArtifactUrl } from "@/lib/artifacts";
import {
  resultRange,
  resultsByPanel,
  testsByPanel,
  visitSummary,
  visitTitle,
} from "@/lib/features/measurements/labs";
import {
  flagWord,
  formatValue,
  type MetricGroup,
  type ReadingSet,
} from "@/lib/features/measurements/measurements";
import type { LabVisitRow, MetricRow, MetricTemplate } from "@/lib/features/measurements/types";
import { useArtifactBytesUrl } from "@/lib/queries";
import { useLabVisits } from "@/lib/features/measurements/queries";
import { cn } from "@/lib/utils";
import { ReadFailed } from "@/components/primitives/read-state";

/** A visit's key in the page's remembered open rows. */
function visitKey(id: string): string {
  return `visit:${id}`;
}

/**
 * Lab results (MEASURE-4, measurements plan §4.9), under Your metrics: Log a lab visit; the
 * visits newest first, each opening in place to its results by panel (test, result, the range
 * printed beside it, High or Low) with Edit visit and its report; then the tests by panel, each a
 * row as in Your metrics but with no Log, opening its card. A result's Change opens its visit.
 */
export function LabResults({
  metrics,
  catalog,
  isOpen,
  onToggle,
  onNewVisit,
  onEditVisit,
}: {
  metrics: readonly MetricRow[];
  /** The starter list (`GET /api/measurements/catalog`), whose order a report prints its tests in. */
  catalog: readonly MetricTemplate[];
  isOpen: (key: string) => boolean;
  onToggle: (key: string) => void;
  onNewVisit: () => void;
  onEditVisit: (visit: LabVisitRow) => void;
}) {
  const titleId = useId();
  const visitsId = useId();
  const read = useLabVisits();
  const visits = read.data?.visits ?? [];
  const panels = testsByPanel(metrics, catalog);

  const changeResult = (set: ReadingSet) => {
    const visit = visits.find((v) => v.id === set.find((r) => r != null)?.visitId);
    if (visit) onEditVisit(visit);
    else onNewVisit();
  };

  return (
    <section aria-labelledby={titleId} className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id={titleId} className="font-heading text-xl">
          Lab results
        </h2>
        <Button type="button" variant="outline" className="min-h-11 md:min-h-0" onClick={onNewVisit}>
          <PlusIcon />
          Log a lab visit
        </Button>
      </div>

      {read.isLoading ? <ListSkeleton rows={2} /> : null}
      <ReadFailed read={read} what="Lab visits" />
      {read.data && visits.length === 0 ? (
        <p className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">
          Log a lab visit when you have blood drawn: the day, the lab, each result with the range printed beside it, and
          the report's PDF. The CMP, the lipid panel and A1c are ready to fill in. Each test gets a chart, and a result
          outside its range is marked High or Low.
        </p>
      ) : null}

      {visits.length > 0 ? (
        <div className="space-y-2">
          <h3 id={visitsId} className="text-sm font-medium text-muted-foreground">
            Visits
          </h3>
          <ul className="space-y-2" aria-labelledby={visitsId}>
            {visits.map((visit) => (
              <LabVisitItem
                key={visit.id}
                visit={visit}
                metrics={metrics}
                catalog={catalog}
                open={isOpen(visitKey(visit.id))}
                onToggle={() => onToggle(visitKey(visit.id))}
                onEdit={() => onEditVisit(visit)}
              />
            ))}
          </ul>
        </div>
      ) : null}

      {panels.map(({ panel, groups }) => (
        <PanelTests
          key={panel.key}
          name={panel.name}
          groups={groups}
          isOpen={isOpen}
          onToggle={onToggle}
          onNewVisit={onNewVisit}
          onChange={changeResult}
        />
      ))}
    </section>
  );
}

function PanelTests({
  name,
  groups,
  isOpen,
  onToggle,
  onNewVisit,
  onChange,
}: {
  name: string;
  groups: MetricGroup[];
  isOpen: (key: string) => boolean;
  onToggle: (key: string) => void;
  onNewVisit: () => void;
  onChange: (set: ReadingSet) => void;
}) {
  const headingId = useId();
  return (
    <div className="space-y-2">
      <h3 id={headingId} className="text-sm font-medium text-muted-foreground">
        {name}
      </h3>
      <ul className="space-y-2" aria-labelledby={headingId}>
        {groups.map((group) => (
          <MetricListRow
            key={group.key}
            group={group}
            heading="h4"
            showLog={false}
            open={isOpen(group.key)}
            onToggle={() => onToggle(group.key)}
            onLog={onNewVisit}
            onChange={onChange}
          />
        ))}
      </ul>
    </div>
  );
}

/**
 * One visit (§4.9): its day and lab, how many results and how many are outside their range, and
 * whether it has a report. It opens in place to its results by panel, with Edit visit and the
 * report to open.
 */
function LabVisitItem({
  visit,
  metrics,
  catalog,
  open,
  onToggle,
  onEdit,
}: {
  visit: LabVisitRow;
  metrics: readonly MetricRow[];
  catalog: readonly MetricTemplate[];
  open: boolean;
  onToggle: () => void;
  onEdit: () => void;
}) {
  const bodyId = useId();
  const title = visitTitle(visit);
  const summary = visitSummary(visit);
  const spoken = `${title}: ${summary.toLowerCase()}${visit.report ? ", with its report" : ""}`;
  return (
    <li className="rounded-xl bg-card ring-1 ring-foreground/10" data-testid="lab-visit">
      <h4 className="px-1.5 py-1.5">
        <button
          type="button"
          aria-label={spoken}
          aria-expanded={open}
          aria-controls={bodyId}
          onClick={onToggle}
          className="flex min-h-11 w-full items-center gap-2 rounded-lg px-1.5 py-1 text-left hover:bg-muted/60 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <ChevronRightIcon
            className={cn("size-4 shrink-0 text-muted-foreground transition-transform", open && "rotate-90")}
            aria-hidden
          />
          <span className="min-w-0 flex-1">
            <span className="block font-medium [overflow-wrap:anywhere]">{title}</span>
            <span className="block text-xs font-normal text-muted-foreground" data-testid="lab-visit-summary">
              {summary}
            </span>
          </span>
          {visit.report ? <FileTextIcon className="size-4 shrink-0 text-muted-foreground" aria-hidden /> : null}
        </button>
      </h4>
      {open ? (
        <div id={bodyId} className="space-y-3 border-t px-4 py-4">
          {visit.note ? <p className="text-sm text-muted-foreground">{visit.note}</p> : null}
          {visit.results.length > 0 ? (
            <VisitResultsTable visit={visit} title={title} metrics={metrics} catalog={catalog} />
          ) : (
            <p className="text-sm text-muted-foreground">No results yet: add them with Edit visit when they come back.</p>
          )}
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" size="sm" className="min-h-11 md:min-h-0" onClick={onEdit}>
              <PencilIcon />
              Edit visit
            </Button>
            {visit.report ? <OpenReport visit={visit} /> : null}
          </div>
        </div>
      ) : null}
    </li>
  );
}

/** A visit's results as its report lays them out: a heading row for each panel, then Test, Result, Range and Flag. */
function VisitResultsTable({
  visit,
  title,
  metrics,
  catalog,
}: {
  visit: LabVisitRow;
  title: string;
  metrics: readonly MetricRow[];
  catalog: readonly MetricTemplate[];
}) {
  const panels = resultsByPanel(visit, metrics, catalog);
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm" data-testid="lab-visit-results">
        <caption className="sr-only">Results from {title}</caption>
        <thead>
          <tr className="text-left text-xs text-muted-foreground">
            <th scope="col" className="py-1 pr-1.5 sm:pr-2 font-medium">
              Test
            </th>
            <th scope="col" className="py-1 pl-1.5 sm:pl-2 text-right font-medium">
              Result
            </th>
            <th scope="col" className="py-1 pl-1.5 sm:pl-2 font-medium">
              Range
            </th>
            <th scope="col" className="py-1 pl-1.5 sm:pl-2 font-medium">
              Flag
            </th>
          </tr>
        </thead>
        {panels.map(({ panel, results }) => (
          <tbody key={panel.key} className="divide-y">
            <tr>
              <th scope="colgroup" colSpan={4} className="pt-3 pb-1 text-left text-xs font-medium text-primary">
                {panel.name}
              </th>
            </tr>
            {results.map(({ reading, metric }) => (
              <tr key={reading.id}>
                <th scope="row" className="py-1.5 pr-1.5 sm:pr-2 text-left font-normal break-words">
                  {metric.name}
                </th>
                {/* The unit set small after its number; a long one (eGFR's) wraps under it on a phone. */}
                <td className="py-1.5 pl-1.5 sm:pl-2 text-right tabular-nums">
                  {formatValue(reading.value, metric.decimals)}
                  {reading.unit ? <span className="text-xs text-muted-foreground"> {reading.unit}</span> : null}
                </td>
                <td className="py-1.5 pl-1.5 sm:pl-2 text-muted-foreground tabular-nums">
                  {unbroken(resultRange({ ...reading, unit: "" }, metric.decimals)) ?? "—"}
                </td>
                <td className="py-1.5 pl-1.5 sm:pl-2">
                  {reading.flag ? <Badge variant="outline">{flagWord(reading.flag)}</Badge> : null}
                </td>
              </tr>
            ))}
          </tbody>
        ))}
      </table>
    </div>
  );
}

/** A range that wraps only between words, never at its dash: "135–146" stays on one line. */
function unbroken(text: string | null): string | null {
  return text?.replace("–", "\u2060–\u2060") ?? null;
}

/** Opens a visit's report in a new tab: its bytes are fetched once the visit is opened. */
function OpenReport({ visit }: { visit: LabVisitRow }) {
  const report = visit.report!;
  const url = useArtifactBytesUrl(report.id);
  const name = report.filename ?? "the report";
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      className="min-h-11 md:min-h-0"
      disabled={!url.data && !url.isError}
      aria-label={url.isError ? `Retry loading the report, ${name}` : `Open the report, ${name}`}
      onClick={() => (url.isError ? void url.refetch() : openArtifactUrl(url.data))}
    >
      <ExternalLinkIcon />
      {url.isError ? "Retry the report" : "Open report"}
    </Button>
  );
}

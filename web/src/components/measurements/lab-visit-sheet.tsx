import { useId, useState } from "react";
import { FileTextIcon, PlusIcon, XIcon } from "lucide-react";
import { toast } from "sonner";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { formatBytes } from "@/lib/artifacts";
import {
  LAB_MAX,
  PANELS,
  REPORT_TYPES,
  addedRow,
  checkRows,
  collectedInstant,
  initialPanels,
  labsUsed,
  reportProblem,
  visitMessage,
  visitProblem,
  visitRows,
  visitSummary,
  visitTitle,
  type VisitTestRow,
} from "@/lib/features/measurements/labs";
import { NAME_MAX, NOTE_MAX, SKEW_MS, UNIT_MAX } from "@/lib/features/measurements/measurements";
import { useIsDesktop } from "@/lib/media-query";
import type {
  LabPanel,
  LabVisitRequest,
  LabVisitRow,
  MetricRow,
  MetricTemplate,
} from "@/lib/features/measurements/types";
import { useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { errorText, useWriteAction } from "@/lib/write-action";

/**
 * The lab visit sheet (MEASURE-4, measurements plan §4.9), for a new visit and for an edit: a
 * bottom sheet on a phone, a side panel on a desktop. When the sample was collected (the day; the
 * time is optional), the lab (suggesting ones used before) and a note; the panels as toggles,
 * each listing its tests with a result and the range printed beside it, filled in from the last
 * report; the tests tracked under Other tests; Add a test for anything else. A blank result is
 * left out, so the visit can be logged before its results come back. The report (a PDF or a
 * photo) uploads once the visit saves. An edit also has Delete visit; the record keeps every
 * version.
 */
export function LabVisitSheet({
  open,
  onOpenChange,
  visit,
  visits,
  metrics,
  catalog,
  today,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The visit to change; null for a new one. */
  visit: LabVisitRow | null;
  /** Every visit, newest first: the labs to suggest, and the panels a new visit starts with. */
  visits: readonly LabVisitRow[];
  metrics: readonly MetricRow[];
  /** The starter list (`GET /api/measurements/catalog`): each panel's tests and the ranges a first visit starts from. */
  catalog: readonly MetricTemplate[];
  today: string;
}) {
  const desktop = useIsDesktop();
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side={desktop ? "right" : "bottom"}
        className={cn("gap-0", desktop ? "w-full data-[side=right]:sm:max-w-xl" : "max-h-[92dvh] rounded-t-2xl")}
      >
        {open ? (
          <VisitForm
            key={visit?.id ?? "new"}
            visit={visit}
            visits={visits}
            metrics={metrics}
            catalog={catalog}
            today={today}
            onDone={() => onOpenChange(false)}
          />
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

const ROW_GRID = "grid grid-cols-[minmax(0,1fr)_4.75rem_3.75rem_3.75rem] items-center gap-x-2 sm:grid-cols-[minmax(0,1fr)_6rem_5rem_5rem]";

function VisitForm({
  visit,
  visits,
  metrics,
  catalog,
  today,
  onDone,
}: {
  visit: LabVisitRow | null;
  visits: readonly LabVisitRow[];
  metrics: readonly MetricRow[];
  catalog: readonly MetricTemplate[];
  today: string;
  onDone: () => void;
}) {
  const writes = useWrites();
  const ids = useId();
  const [openedAt] = useState(() => Date.now());
  const startDay = visit ? visit.collectedOn : today;
  const startTime = visit ? visit.collectedLocal.slice(11, 16) : "";
  const [day, setDay] = useState(startDay);
  const [time, setTime] = useState(startTime);
  const [lab, setLab] = useState(visit?.lab ?? "");
  const [note, setNote] = useState(visit?.note ?? "");
  const [rows, setRows] = useState<VisitTestRow[]>(() => visitRows(metrics, visit, catalog));
  const [panels, setPanels] = useState<Set<LabPanel>>(() =>
    initialPanels(rows, visit != null, visits.find((v) => v.id !== visit?.id) ?? null, metrics),
  );
  const [added, setAdded] = useState(0);
  const [file, setFile] = useState<File | null>(null);
  const [removeReport, setRemoveReport] = useState(false);
  const { run, pending, error } = useWriteAction({ onDone });
  const [confirmDelete, setConfirmDelete] = useState(false);

  const shown = rows.filter((r) => r.added || panels.has(r.panel));
  const toggles = PANELS.filter((p) => rows.some((r) => !r.added && r.panel === p.key));
  const moved = day !== startDay || time !== startTime;
  const instant = collectedInstant(day, time, openedAt);
  // Against when the sheet opened, so the check holds still while it's open; the kernel checks
  // again against its own clock.
  const future = instant != null && Date.parse(instant) > openedAt + SKEW_MS;
  const checked = checkRows(shown);
  const fileProblem = file ? reportProblem(file) : null;
  const problem =
    instant == null
      ? "Say which day the sample was collected."
      : future
        ? "A lab visit is collected now or earlier, not in the future."
        : (visitProblem(lab, note) ?? ("problem" in checked ? checked.problem : fileProblem));
  const suggestions = labsUsed(visits);

  const setRow = (key: string, change: Partial<VisitTestRow>) =>
    setRows((now) => now.map((r) => (r.key === key ? { ...r, ...change } : r)));
  const togglePanel = (key: LabPanel) =>
    setPanels((now) => {
      const next = new Set(now);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });

  /** An edit sends what changed: a lab or note left as it was is kept, and a blank one clears it. */
  function changed(text: string, was: string | null): string | undefined {
    const now = text.trim();
    if (!visit) return now || undefined;
    return now === (was ?? "") ? undefined : now;
  }

  function submit() {
    if (problem || pending || "problem" in checked) return;
    const req: LabVisitRequest = {
      collectedAt: !visit || moved ? instant! : undefined,
      lab: changed(lab, visit?.lab ?? null),
      note: changed(note, visit?.note ?? null),
      results: checked.results,
    };
    void run(async () => {
      const saved = visit ? await writes.saveLabVisit(visit.id, req) : await writes.logLabVisit(req);
      let final = saved.visit!;
      try {
        if (file) final = (await writes.attachLabReport(final.id, file, file.name)).visit ?? final;
        else if (removeReport && visit?.report) final = (await writes.removeLabReport(final.id)).visit ?? final;
      } catch (e) {
        toast.success(visitMessage("saved", final, today));
        toast.error(`The visit is saved, but its report isn't: ${errorText(e, "it didn't upload")}`);
        return;
      }
      toast.success(visitMessage("saved", final, today));
    });
  }

  return (
    <form
      className="flex min-h-0 flex-1 flex-col"
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <SheetHeader className="pb-2">
        <SheetTitle>{visit ? "Edit lab visit" : "Log a lab visit"}</SheetTitle>
        <SheetDescription>
          {visit
            ? `${visitTitle(visit)}: ${visitSummary(visit).toLowerCase()}. A result left blank, or on a panel turned off, is deleted; the record keeps every version.`
            : "Type each result and the range printed beside it. A result left blank is left out, so you can log the visit now and add results when they come back."}
        </SheetDescription>
      </SheetHeader>

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-4 pb-4">
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label htmlFor={`${ids}-day`}>Collected on</Label>
            <Input
              id={`${ids}-day`}
              type="date"
              value={day}
              max={today}
              disabled={pending}
              onChange={(e) => setDay(e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor={`${ids}-time`}>Time (optional)</Label>
            <Input
              id={`${ids}-time`}
              type="time"
              value={time}
              disabled={pending}
              onChange={(e) => setTime(e.target.value)}
            />
          </div>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-lab`}>Lab (optional)</Label>
          <Input
            id={`${ids}-lab`}
            value={lab}
            maxLength={LAB_MAX}
            disabled={pending}
            list={suggestions.length > 0 ? `${ids}-labs` : undefined}
            placeholder="Quest Diagnostics, LabCorp…"
            autoComplete="off"
            onChange={(e) => setLab(e.target.value)}
          />
          {suggestions.length > 0 ? (
            <datalist id={`${ids}-labs`}>
              {suggestions.map((s) => (
                <option key={s} value={s} />
              ))}
            </datalist>
          ) : null}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-note`}>Note (optional)</Label>
          <Input
            id={`${ids}-note`}
            value={note}
            maxLength={NOTE_MAX}
            disabled={pending}
            placeholder="Fasting, annual physical…"
            onChange={(e) => setNote(e.target.value)}
          />
        </div>

        {toggles.length > 0 ? (
          <div className="space-y-1.5">
            <p className="text-sm font-medium" id={`${ids}-panels`}>
              Panels
            </p>
            <div role="group" aria-labelledby={`${ids}-panels`} className="flex flex-wrap gap-1.5">
              {toggles.map((p) => (
                <Button
                  key={p.key}
                  type="button"
                  variant={panels.has(p.key) ? "default" : "outline"}
                  size="sm"
                  className="min-h-11 md:min-h-0"
                  aria-pressed={panels.has(p.key)}
                  disabled={pending}
                  onClick={() => togglePanel(p.key)}
                >
                  {p.name}
                </Button>
              ))}
            </div>
          </div>
        ) : null}

        {PANELS.filter((p) => panels.has(p.key)).map((p) => {
          const tests = rows.filter((r) => !r.added && r.panel === p.key);
          if (tests.length === 0) return null;
          return (
            <fieldset key={p.key} className="space-y-1.5" disabled={pending}>
              <legend className="mb-1 text-sm font-medium">{p.name}</legend>
              <ColumnHeads />
              {tests.map((row) => (
                <TestRow key={row.key} row={row} ids={ids} onChange={(change) => setRow(row.key, change)} />
              ))}
            </fieldset>
          );
        })}

        {rows.some((r) => r.added) ? (
          <fieldset className="space-y-3" disabled={pending}>
            <legend className="mb-1 text-sm font-medium">Added tests</legend>
            {rows
              .filter((r) => r.added)
              .map((row, i) => (
                <AddedTestRow
                  key={row.key}
                  row={row}
                  n={i + 1}
                  ids={ids}
                  onChange={(change) => setRow(row.key, change)}
                  onRemove={() => setRows((now) => now.filter((r) => r.key !== row.key))}
                />
              ))}
          </fieldset>
        ) : null}

        <Button
          type="button"
          variant="outline"
          size="sm"
          className="min-h-11 md:min-h-0"
          disabled={pending}
          onClick={() => {
            setRows((now) => [...now, addedRow(added + 1)]);
            setAdded((n) => n + 1);
          }}
        >
          <PlusIcon />
          Add a test
        </Button>

        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-report`}>Report (optional)</Label>
          {visit?.report && !removeReport && !file ? (
            <div className="flex items-center gap-2 rounded-lg border px-3 py-2 text-sm">
              <FileTextIcon className="size-4 shrink-0 text-muted-foreground" aria-hidden />
              <span className="min-w-0 flex-1 truncate">
                {visit.report.filename ?? "Report"}{" "}
                <span className="text-xs text-muted-foreground">{formatBytes(visit.report.byteSize)}</span>
              </span>
              <Button type="button" variant="ghost" size="sm" disabled={pending} onClick={() => setRemoveReport(true)}>
                Remove
              </Button>
            </div>
          ) : null}
          {visit?.report && removeReport && !file ? (
            <p className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              The report comes off when you save.
              <Button type="button" variant="link" size="sm" className="h-auto p-0" onClick={() => setRemoveReport(false)}>
                Keep it
              </Button>
            </p>
          ) : null}
          <Input
            id={`${ids}-report`}
            type="file"
            accept={REPORT_TYPES.join(",")}
            disabled={pending}
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
          <p className="text-xs text-muted-foreground">
            A PDF, or a photo of the paper, up to 10 MB. {visit?.report ? "A new one replaces the one there. " : ""}It
            uploads once the visit saves.
          </p>
        </div>

        {problem ? (
          <p className="text-xs text-muted-foreground" aria-live="polite">
            {problem}
          </p>
        ) : null}
        {error ? (
          <p role="alert" className="text-xs text-destructive">
            {error}
          </p>
        ) : null}
      </div>

      <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t px-4 py-3">
        {visit ? (
          <Button type="button" variant="destructive" disabled={pending} onClick={() => setConfirmDelete(true)}>
            Delete visit
          </Button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <Button type="button" variant="outline" className="min-h-11 md:min-h-0" onClick={onDone}>
            Cancel
          </Button>
          <Button type="submit" className="min-h-11 md:min-h-0" disabled={!!problem || pending}>
            {pending ? "Saving…" : "Save visit"}
          </Button>
        </div>
      </div>

      {visit ? (
        <AlertDialog open={confirmDelete} onOpenChange={setConfirmDelete}>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Delete this lab visit?</AlertDialogTitle>
              <AlertDialogDescription>
                {visitTitle(visit)}: {visitSummary(visit).toLowerCase()}. Its results go with it. The record keeps what
                they were, and each test's range goes back to the report before.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Keep it</AlertDialogCancel>
              <AlertDialogAction
                variant="destructive"
                onClick={() =>
                  void run(async () => {
                    await writes.deleteLabVisit(visit.id);
                    toast.success(visitMessage("deleted", null, today));
                  })
                }
              >
                Delete visit
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      ) : null}
    </form>
  );
}

/** The columns over a panel's tests; each input has its own label, so these are for the eye. */
function ColumnHeads() {
  return (
    <div className={cn(ROW_GRID, "text-xs text-muted-foreground")} aria-hidden>
      <span>Test</span>
      <span>Result</span>
      <span>Low</span>
      <span>High</span>
    </div>
  );
}

function TestRow({
  row,
  ids,
  onChange,
}: {
  row: VisitTestRow;
  ids: string;
  onChange: (change: Partial<VisitTestRow>) => void;
}) {
  const id = `${ids}-${row.key}`;
  return (
    <div className={ROW_GRID} data-testid="lab-test-row">
      <label htmlFor={`${id}-value`} className="min-w-0 text-sm leading-tight break-words">
        {row.name}
        {row.unit ? <span className="block text-xs text-muted-foreground">{row.unit}</span> : null}
      </label>
      <Input
        id={`${id}-value`}
        inputMode="decimal"
        autoComplete="off"
        className="tabular-nums"
        value={row.value}
        onChange={(e) => onChange({ value: e.target.value })}
      />
      <Input
        aria-label={`${row.name}: the range's low end`}
        inputMode="decimal"
        autoComplete="off"
        className="tabular-nums"
        value={row.low}
        onChange={(e) => onChange({ low: e.target.value })}
      />
      <Input
        aria-label={`${row.name}: the range's high end`}
        inputMode="decimal"
        autoComplete="off"
        className="tabular-nums"
        value={row.high}
        onChange={(e) => onChange({ high: e.target.value })}
      />
    </div>
  );
}

function AddedTestRow({
  row,
  n,
  ids,
  onChange,
  onRemove,
}: {
  row: VisitTestRow;
  n: number;
  ids: string;
  onChange: (change: Partial<VisitTestRow>) => void;
  onRemove: () => void;
}) {
  const id = `${ids}-${row.key}`;
  const what = row.name.trim() || `added test ${n}`;
  return (
    <div className="space-y-2 rounded-lg border p-2" data-testid="lab-added-row">
      <div className="flex items-end gap-2">
        <div className="min-w-0 flex-1 space-y-1">
          <Label htmlFor={`${id}-name`} className="text-xs">
            Test
          </Label>
          <Input
            id={`${id}-name`}
            value={row.name}
            maxLength={NAME_MAX}
            placeholder="TSH"
            autoComplete="off"
            onChange={(e) => onChange({ name: e.target.value })}
          />
        </div>
        <div className="w-24 space-y-1">
          <Label htmlFor={`${id}-unit`} className="text-xs">
            Unit
          </Label>
          <Input
            id={`${id}-unit`}
            value={row.unit}
            maxLength={UNIT_MAX}
            placeholder="mIU/L"
            autoComplete="off"
            onChange={(e) => onChange({ unit: e.target.value })}
          />
        </div>
        <Button type="button" variant="ghost" size="icon" aria-label={`Remove ${what}`} onClick={onRemove}>
          <XIcon />
        </Button>
      </div>
      <div className="grid grid-cols-3 gap-2">
        {(
          [
            ["value", "Result"],
            ["low", "Low"],
            ["high", "High"],
          ] as const
        ).map(([field, label]) => (
          <div key={field} className="space-y-1">
            <Label htmlFor={`${id}-${field}`} className="text-xs">
              {label}
            </Label>
            <Input
              id={`${id}-${field}`}
              inputMode="decimal"
              autoComplete="off"
              className="tabular-nums"
              value={row[field]}
              onChange={(e) => onChange({ [field]: e.target.value })}
            />
          </div>
        ))}
      </div>
    </div>
  );
}

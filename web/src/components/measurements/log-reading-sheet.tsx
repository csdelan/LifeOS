import { useId, useState } from "react";
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
import { fromLocalInput, toLocalInput } from "@/lib/food";
import { NOTE_MAX, SKEW_MS, parseValue, readingMessage, readingWhen, withUnit } from "@/lib/measurements";
import { useIsDesktop } from "@/lib/media-query";
import type { MetricReadingRow, MetricRow } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

/**
 * The Log weight sheet (MEASURE-1, measurements plan §4.7): a bottom sheet on a phone, a side
 * panel on a desktop. The value (a number pad on a phone), when it was taken (now unless
 * changed, any past time), and a note. Saving closes it, and the chart and goal date are worked
 * out again. Opened on a reading, it changes it and offers Delete; the record keeps every
 * version. `noun` names the reading in the words: "weigh-in".
 */
export function LogReadingSheet({
  open,
  onOpenChange,
  metric,
  reading,
  today,
  noun = "reading",
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  metric: MetricRow;
  /** Null: a new reading. */
  reading: MetricReadingRow | null;
  today: string;
  noun?: string;
}) {
  const desktop = useIsDesktop();
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side={desktop ? "right" : "bottom"}
        className={cn("gap-0", desktop ? "w-full sm:max-w-md" : "max-h-[92dvh] rounded-t-2xl")}
      >
        {open ? (
          <ReadingForm
            key={reading?.id ?? "new"}
            metric={metric}
            reading={reading}
            today={today}
            noun={noun}
            onDone={() => onOpenChange(false)}
          />
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

function ReadingForm({
  metric,
  reading,
  today,
  noun,
  onDone,
}: {
  metric: MetricRow;
  reading: MetricReadingRow | null;
  today: string;
  noun: string;
  onDone: () => void;
}) {
  const writes = useWrites();
  const ids = useId();
  // The time the sheet opened with: sent only once it's changed, so an untouched "now" is the
  // kernel's own now and an untouched edit keeps the reading's exact time.
  const [openedAt] = useState(() => Date.now());
  const [startAt] = useState(() => toLocalInput(reading ? reading.takenLocal : openedAt));
  const [value, setValue] = useState(reading ? reading.value.toFixed(metric.decimals) : "");
  const [takenAt, setTakenAt] = useState(startAt);
  const [note, setNote] = useState(reading?.note ?? "");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const parsed = parseValue(value, metric.decimals, metric.name);
  const at = fromLocalInput(takenAt);
  // Against when the sheet opened, so the check holds still while it's open; the kernel checks
  // again against its own clock.
  const future = at != null && Date.parse(at) > openedAt + SKEW_MS;
  const problem =
    "problem" in parsed
      ? parsed.problem
      : at == null
        ? "Say when it was taken."
        : future
          ? "A reading is taken now or earlier, not in the future."
          : null;
  const unit = metric.unit ? ` (${metric.unit})` : "";
  const what = metric.name.toLowerCase();

  async function run(write: () => Promise<void>) {
    setError(null);
    setPending(true);
    try {
      await write();
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setPending(false);
    }
  }

  function submit() {
    if (problem || pending || !("value" in parsed)) return;
    const moved = takenAt !== startAt ? (at ?? undefined) : undefined;
    const trimmed = note.trim();
    if (reading) {
      void run(async () => {
        const result = await writes.editReading(reading.id, {
          value: parsed.value,
          takenAt: moved,
          note: trimmed === (reading.note ?? "") ? undefined : trimmed,
        });
        toast.success(readingMessage("saved", result.reading, metric.decimals));
      });
      return;
    }
    void run(async () => {
      const result = await writes.logReading({
        metric: metric.id,
        value: parsed.value,
        takenAt: moved,
        note: trimmed || undefined,
      });
      toast.success(readingMessage("logged", result.reading, metric.decimals));
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
        <SheetTitle>{reading ? `Change ${noun}` : `Log ${what}`}</SheetTitle>
        <SheetDescription>
          {reading
            ? `${withUnit(reading.value, metric)} · ${readingWhen(reading, today)}. Changes keep the record: the ${noun} gets a newer version.`
            : `Now, or any time before. The first ${noun} of a day is that day's number.`}
        </SheetDescription>
      </SheetHeader>

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-4 pb-4">
        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-value`}>
            {metric.name}
            {unit}
          </Label>
          <Input
            id={`${ids}-value`}
            type="text"
            inputMode="decimal"
            autoComplete="off"
            autoFocus
            value={value}
            disabled={pending}
            placeholder={metric.decimals === 0 ? "70" : "192.4"}
            className="text-lg tabular-nums"
            onChange={(e) => setValue(e.target.value)}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-time`}>Taken at</Label>
          <Input
            id={`${ids}-time`}
            type="datetime-local"
            value={takenAt}
            max={toLocalInput(openedAt + SKEW_MS)}
            disabled={pending}
            onChange={(e) => setTakenAt(e.target.value)}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-note`}>Note (optional)</Label>
          <Input
            id={`${ids}-note`}
            value={note}
            maxLength={NOTE_MAX}
            disabled={pending}
            placeholder="after a run, new scale…"
            onChange={(e) => setNote(e.target.value)}
          />
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
        {reading ? (
          <Button type="button" variant="destructive" disabled={pending} onClick={() => setConfirmDelete(true)}>
            Delete
          </Button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <Button type="button" variant="outline" className="min-h-11 md:min-h-0" onClick={onDone}>
            Cancel
          </Button>
          <Button type="submit" className="min-h-11 md:min-h-0" disabled={!!problem || pending}>
            {pending ? "Saving…" : reading ? "Save" : "Log"}
          </Button>
        </div>
      </div>

      {reading ? (
        <DeleteReadingDialog
          open={confirmDelete}
          onOpenChange={setConfirmDelete}
          what={`${withUnit(reading.value, metric)} · ${readingWhen(reading, today)}`}
          noun={noun}
          onDelete={() =>
            void run(async () => {
              await writes.deleteReading(reading.id);
              toast.success(readingMessage("deleted", null, metric.decimals));
            })
          }
        />
      ) : null}
    </form>
  );
}

/** Asks before a reading is deleted: the trend and goal date are worked out without it. */
export function DeleteReadingDialog({
  open,
  onOpenChange,
  what,
  noun,
  onDelete,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** "192.4 lb · Today, 7:10 AM". */
  what: string;
  noun: string;
  onDelete: () => void;
}) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete this {noun}?</AlertDialogTitle>
          <AlertDialogDescription>
            {what}. The record keeps what it was; the trend and the goal date are worked out without it.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep it</AlertDialogCancel>
          <AlertDialogAction variant="destructive" onClick={onDelete}>
            Delete
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

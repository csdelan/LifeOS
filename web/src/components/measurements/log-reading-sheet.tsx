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
import {
  NOTE_MAX,
  SKEW_MS,
  groupValue,
  nameInText,
  parseValue,
  readingMessage,
  readingWhen,
  readingsMessage,
  type MetricGroup,
} from "@/lib/features/measurements/measurements";
import { useIsDesktop } from "@/lib/media-query";
import type { MetricReadingRow } from "@/lib/features/measurements/types";
import { useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { useWriteAction } from "@/lib/write-action";

/**
 * The Log sheet (MEASURE-1, MEASURE-3, measurements plan §4.7–§4.8): a bottom sheet on a phone,
 * a side panel on a desktop. A value for each of the group's metrics (blood pressure's two,
 * logged together in one write), each with a number pad on a phone; when it was taken (now
 * unless changed, any past time); and a note. Saving closes it, and the chart and goal date are
 * worked out again. Opened on a set of readings, it changes them and offers Delete; the record
 * keeps every version.
 */
export function LogReadingSheet({
  open,
  onOpenChange,
  group,
  readings,
  today,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  group: MetricGroup;
  /** The set to change, in the group's order; null for a new one. */
  readings: (MetricReadingRow | null)[] | null;
  today: string;
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
            key={readings?.map((r) => r?.id ?? "-").join() ?? `new-${group.key}`}
            group={group}
            readings={readings}
            today={today}
            onDone={() => onOpenChange(false)}
          />
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

function ReadingForm({
  group,
  readings,
  today,
  onDone,
}: {
  group: MetricGroup;
  readings: (MetricReadingRow | null)[] | null;
  today: string;
  onDone: () => void;
}) {
  const writes = useWrites();
  const ids = useId();
  const { metrics, noun } = group;
  const taken = readings?.find((r) => r != null) ?? null;
  // The time the sheet opened with: sent only once it's changed, so an untouched "now" is the
  // kernel's own now and an untouched edit keeps the readings' exact time.
  const [openedAt] = useState(() => Date.now());
  const [startAt] = useState(() => toLocalInput(taken ? taken.takenLocal : openedAt));
  const [values, setValues] = useState(() =>
    metrics.map((m, i) => {
      const r = readings?.[i];
      return r ? r.value.toFixed(m.decimals) : "";
    }),
  );
  const [takenAt, setTakenAt] = useState(startAt);
  const [note, setNote] = useState(taken?.note ?? "");
  const { run, pending, error } = useWriteAction({ onDone });
  const [confirmDelete, setConfirmDelete] = useState(false);

  const single = metrics.length === 1;
  // A half a set doesn't have can stay blank; every other value is needed.
  const examples = metrics.map((m, i) => (m.decimals === 0 ? (single ? "70" : i === 0 ? "118" : "76") : "192.4"));
  const parsed = metrics.map((m, i) =>
    readings && readings[i] == null && values[i].trim() === ""
      ? null
      : parseValue(values[i], m.decimals, m.name, examples[i]),
  );
  const at = fromLocalInput(takenAt);
  // Against when the sheet opened, so the check holds still while it's open; the kernel checks
  // again against its own clock.
  const future = at != null && Date.parse(at) > openedAt + SKEW_MS;
  const valueProblem = parsed.find((p) => p != null && "problem" in p) as { problem: string } | undefined;
  const problem = valueProblem
    ? valueProblem.problem
    : at == null
      ? "Say when it was taken."
      : future
        ? "A reading is taken now or earlier, not in the future."
        : null;
  const numbers = parsed.map((p) => (p != null && "value" in p ? p.value : null));

  function submit() {
    if (problem || pending) return;
    const moved = takenAt !== startAt ? (at ?? undefined) : undefined;
    const trimmed = note.trim();
    if (readings) {
      void run(async () => {
        const saved: (MetricReadingRow | null)[] = [];
        for (const [i, r] of readings.entries()) {
          const value = numbers[i];
          if (r) {
            const result = await writes.editReading(r.id, {
              value: value ?? undefined,
              takenAt: moved,
              note: trimmed === (r.note ?? "") ? undefined : trimmed,
            });
            saved.push(result.reading);
          } else if (value != null) {
            // The half the set was missing, logged at the set's time.
            const result = await writes.logReading({
              metric: metrics[i].id,
              value,
              takenAt: moved ?? taken!.takenAt,
              note: trimmed || undefined,
            });
            saved.push(result.reading);
          } else {
            saved.push(null);
          }
        }
        toast.success(
          single
            ? readingMessage("saved", saved[0], metrics[0].decimals)
            : readingsMessage("saved", group, saved.map((r) => r?.value ?? null)),
        );
      });
      return;
    }
    void run(async () => {
      if (single) {
        const result = await writes.logReading({
          metric: metrics[0].id,
          value: numbers[0]!,
          takenAt: moved,
          note: trimmed || undefined,
        });
        toast.success(readingMessage("logged", result.reading, metrics[0].decimals));
        return;
      }
      const result = await writes.logReadings({
        readings: metrics.map((m, i) => ({ metric: m.id, value: numbers[i]! })),
        takenAt: moved,
        note: trimmed || undefined,
      });
      toast.success(readingsMessage("logged", group, result.readings.map((r) => r.value)));
    });
  }

  const what = readings ? `${groupValue(group, readings.map((r) => r?.value ?? null))} · ${readingWhen(taken!, today)}` : "";

  return (
    <form
      className="flex min-h-0 flex-1 flex-col"
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <SheetHeader className="pb-2">
        <SheetTitle>{readings ? `Change ${noun}` : `Log ${nameInText(group.name)}`}</SheetTitle>
        <SheetDescription>
          {readings
            ? `${what}. Changes keep the record: the ${noun} gets a newer version.`
            : single
              ? `Now, or any time before. The first ${noun} of a day is that day's number.`
              : `Now, or any time before. Both are logged together; the first of a day is that day's number.`}
        </SheetDescription>
      </SheetHeader>

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-4 pb-4">
        <div className={cn("grid gap-4", !single && "grid-cols-2")}>
          {metrics.map((m, i) => (
            <div key={m.id} className="space-y-1.5">
              <Label htmlFor={`${ids}-value-${i}`}>
                {group.labels[i]}
                {m.unit ? ` (${m.unit})` : ""}
              </Label>
              <Input
                id={`${ids}-value-${i}`}
                type="text"
                inputMode="decimal"
                autoComplete="off"
                autoFocus={i === 0}
                value={values[i]}
                disabled={pending}
                placeholder={examples[i]}
                className="text-lg tabular-nums"
                onChange={(e) => setValues((vs) => vs.map((v, j) => (j === i ? e.target.value : v)))}
              />
            </div>
          ))}
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
            placeholder={single ? "after a run, new scale…" : "after coffee, left arm…"}
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
        {readings ? (
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
            {pending ? "Saving…" : readings ? "Save" : "Log"}
          </Button>
        </div>
      </div>

      {readings ? (
        <DeleteReadingDialog
          open={confirmDelete}
          onOpenChange={setConfirmDelete}
          what={what}
          noun={noun}
          onDelete={() =>
            void run(async () => {
              for (const r of readings) if (r) await writes.deleteReading(r.id);
              toast.success(readingMessage("deleted", null, metrics[0].decimals));
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

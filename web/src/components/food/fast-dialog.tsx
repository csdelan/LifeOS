import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formatDuration } from "@/lib/diet-score";
import { fromLocalInput, toLocalInput } from "@/lib/food";
import type { FastRow } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";

/**
 * Record a past fast, or change or delete one (FOOD-2). The kernel refuses an overlap, a
 * future time or an end before the start, in words; the refusal shows here and nothing is
 * written. A running fast keeps running unless it's given an end.
 */
export function FastDialog({
  open,
  onOpenChange,
  fast,
  defaultFrom,
  defaultTo,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Null: record a new past fast. */
  fast: FastRow | null;
  defaultFrom?: string;
  defaultTo?: string;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {open ? (
          <FastForm
            key={fast?.id ?? "new"}
            fast={fast}
            defaultFrom={defaultFrom}
            defaultTo={defaultTo}
            onDone={() => onOpenChange(false)}
          />
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

function FastForm({
  fast,
  defaultFrom,
  defaultTo,
  onDone,
}: {
  fast: FastRow | null;
  defaultFrom?: string;
  defaultTo?: string;
  onDone: () => void;
}) {
  const writes = useWrites();
  const [from, setFrom] = useState(() =>
    toLocalInput(fast?.startedAt ?? defaultFrom ?? Date.now() - 16 * 3_600_000),
  );
  const [to, setTo] = useState(() =>
    fast?.running ? "" : toLocalInput(fast?.endedAt ?? defaultTo ?? Date.now()),
  );
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const start = fromLocalInput(from);
  const end = to ? fromLocalInput(to) : null;
  const seconds = start && end ? (Date.parse(end) - Date.parse(start)) / 1000 : null;
  const problem = !start
    ? "Say when the fast began."
    : !fast?.running && !end
      ? "Say when it ended."
      : seconds !== null && seconds <= 0
        ? "A fast ends after it starts."
        : null;

  async function run(write: () => Promise<unknown>) {
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
    if (problem || pending || !start) return;
    void run(() =>
      fast
        ? writes.fast({ action: "edit", fast: fast.id, from: start, to: end })
        : writes.fast({ action: "record", from: start, to: end! }),
    );
  }

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <DialogHeader>
        <DialogTitle>{fast ? "Change this fast" : "Record a past fast"}</DialogTitle>
        <DialogDescription>
          A fast counts on the day it ends. It pays from 12 hours, up to 8 base Health XP at 24.
        </DialogDescription>
      </DialogHeader>

      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1">
          <Label htmlFor="fast-from">Began</Label>
          <Input
            id="fast-from"
            type="datetime-local"
            value={from}
            disabled={pending}
            onChange={(e) => setFrom(e.target.value)}
          />
        </div>
        <div className="space-y-1">
          <Label htmlFor="fast-to">
            Ended{" "}
            {fast?.running ? <span className="font-normal text-muted-foreground">(blank: still running)</span> : null}
          </Label>
          <Input
            id="fast-to"
            type="datetime-local"
            value={to}
            disabled={pending}
            onChange={(e) => setTo(e.target.value)}
          />
        </div>
      </div>

      <p className="text-sm" aria-live="polite">
        {problem ? (
          <span className="text-muted-foreground">{problem}</span>
        ) : seconds !== null ? (
          <>
            A fast of <span className="font-medium">{formatDuration(seconds)}</span>
          </>
        ) : (
          "Still running"
        )}
      </p>
      {error ? (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      ) : null}

      <div className="flex flex-wrap justify-between gap-2">
        {fast ? (
          <Button
            type="button"
            variant="destructive"
            disabled={pending}
            onClick={() => void run(() => writes.fast({ action: "delete", fast: fast.id }))}
          >
            Delete fast
          </Button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <Button type="button" variant="outline" onClick={onDone}>
            Cancel
          </Button>
          <Button type="submit" disabled={!!problem || pending}>
            {pending ? "Saving…" : "Save fast"}
          </Button>
        </div>
      </div>
    </form>
  );
}

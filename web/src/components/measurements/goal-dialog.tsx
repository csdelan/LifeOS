import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { parseValue } from "@/lib/measurements";
import type { MetricRow } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";

/**
 * Change or remove a metric's goal (MEASURE-2): the dashed line on its chart and the date the
 * trend is heading for it. The goal takes as many decimal places as a reading does.
 */
export function GoalDialog({
  open,
  onOpenChange,
  metric,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  metric: MetricRow;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {open ? <GoalForm metric={metric} onDone={() => onOpenChange(false)} /> : null}
      </DialogContent>
    </Dialog>
  );
}

function GoalForm({ metric, onDone }: { metric: MetricRow; onDone: () => void }) {
  const writes = useWrites();
  const id = useId();
  const [value, setValue] = useState(metric.target != null ? metric.target.toFixed(metric.decimals) : "");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const parsed = parseValue(value, metric.decimals, "The goal");
  const unit = metric.unit ? ` (${metric.unit})` : "";

  async function save(req: { target: number } | { clearTarget: true }) {
    setError(null);
    setPending(true);
    try {
      await writes.setMetric(metric.id, req);
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setPending(false);
    }
  }

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        if ("value" in parsed && !pending) void save({ target: parsed.value });
      }}
    >
      <DialogHeader>
        <DialogTitle>{metric.name} goal</DialogTitle>
        <DialogDescription>
          Drawn as a dashed line on the chart, with the date your trend is heading for it.
        </DialogDescription>
      </DialogHeader>
      <div className="space-y-1.5">
        <Label htmlFor={`${id}-goal`}>
          Goal {metric.name.toLowerCase()}
          {unit}
        </Label>
        <Input
          id={`${id}-goal`}
          type="text"
          inputMode="decimal"
          autoComplete="off"
          value={value}
          disabled={pending}
          placeholder="185"
          onChange={(e) => setValue(e.target.value)}
        />
        {"problem" in parsed && value.trim() !== "" ? (
          <p className="text-xs text-muted-foreground" aria-live="polite">
            {parsed.problem}
          </p>
        ) : null}
        {error ? (
          <p role="alert" className="text-xs text-destructive">
            {error}
          </p>
        ) : null}
      </div>
      <DialogFooter>
        {metric.target != null ? (
          <Button
            type="button"
            variant="ghost"
            className="sm:mr-auto"
            disabled={pending}
            onClick={() => void save({ clearTarget: true })}
          >
            Remove goal
          </Button>
        ) : null}
        <Button type="button" variant="outline" disabled={pending} onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" disabled={pending || !("value" in parsed)}>
          {pending ? "Saving…" : "Save"}
        </Button>
      </DialogFooter>
    </form>
  );
}

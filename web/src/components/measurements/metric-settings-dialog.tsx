import { useId, useState } from "react";
import { ArchiveIcon } from "lucide-react";
import { ToggleGroup } from "@/components/primitives/toggle-group";
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
import {
  DECIMALS_MAX,
  NAME_MAX,
  UNIT_MAX,
  nameInText,
  parseValue,
  rangeProblem,
  type MetricGroup,
} from "@/lib/measurements";
import type { MetricBetter, MetricRequest, MetricRow } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";
import { useWriteAction } from "@/lib/write-action";

/**
 * Edit a metric (MEASURE-1 to MEASURE-3, measurements plan §4.8): its name, its unit (only while
 * it has no readings: they keep theirs), decimal places, goal, which way is better and normal
 * range, or archive it, which stops new readings and keeps every one it has (Restore brings it
 * back). Blood pressure picks systolic or diastolic to edit, and archives as one.
 */
export function MetricSettingsDialog({
  open,
  onOpenChange,
  group,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  group: MetricGroup;
}) {
  const [which, setWhich] = useState(0);
  const metric = group.metrics[Math.min(which, group.metrics.length - 1)];
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[92dvh] overflow-y-auto sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Edit {nameInText(group.name)}</DialogTitle>
          <DialogDescription>
            The goal draws a dashed line and a date your trend is heading for it; a reading outside the normal range is
            marked High or Low.
          </DialogDescription>
        </DialogHeader>
        {group.metrics.length > 1 ? (
          <ToggleGroup
            label="Edit"
            value={String(which)}
            onChange={(id) => setWhich(Number(id))}
            options={group.metrics.map((_, i) => ({ id: String(i), label: group.labels[i] }))}
          />
        ) : null}
        {open ? (
          <SettingsForm key={metric.id} group={group} metric={metric} onDone={() => onOpenChange(false)} />
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

/** A typed optional number: null when blank, else the value or the problem. */
function optional(raw: string, decimals: number, what: string) {
  return raw.trim() === "" ? { value: null } : parseValue(raw, decimals, what);
}

function SettingsForm({ group, metric, onDone }: { group: MetricGroup; metric: MetricRow; onDone: () => void }) {
  const writes = useWrites();
  const id = useId();
  const show = (n: number | null, places: number) => (n == null ? "" : n.toFixed(places));
  const [name, setName] = useState(metric.name);
  const [unit, setUnit] = useState(metric.unit);
  const [decimals, setDecimals] = useState(metric.decimals);
  const [goal, setGoal] = useState(show(metric.target, metric.decimals));
  const [better, setBetter] = useState<MetricBetter | null>(metric.better);
  const [low, setLow] = useState(show(metric.low, metric.decimals));
  const [high, setHigh] = useState(show(metric.high, metric.decimals));
  const { run, pending, error } = useWriteAction({ onDone });

  const locked = metric.readings > 0;
  const unitText = unit.trim() ? ` (${unit.trim()})` : "";
  const goalValue = optional(goal, decimals, "The goal");
  const lowValue = optional(low, decimals, "The normal range's low end");
  const highValue = optional(high, decimals, "The normal range's high end");
  const problem =
    name.trim() === ""
      ? "Give the metric a name, e.g. Weight."
      : unit.trim() === ""
        ? "Give the metric a unit, e.g. lb, in, % or mg/dL."
        : "problem" in goalValue
          ? goalValue.problem
          : "problem" in lowValue
            ? lowValue.problem
            : "problem" in highValue
              ? highValue.problem
              : rangeProblem(lowValue.value, highValue.value, decimals);

  /** Only what changed, so an untouched field writes nothing. */
  function changes(): MetricRequest {
    const req: MetricRequest = {};
    if (name.trim() !== metric.name) req.name = name.trim();
    if (!locked && unit.trim() !== metric.unit) req.unit = unit.trim();
    if (decimals !== metric.decimals) req.decimals = decimals;
    if (better != null && better !== metric.better) req.better = better;
    const ends: [number | null, number | null, "target" | "low" | "high"][] = [
      ["value" in goalValue ? goalValue.value : null, metric.target, "target"],
      ["value" in lowValue ? lowValue.value : null, metric.low, "low"],
      ["value" in highValue ? highValue.value : null, metric.high, "high"],
    ];
    for (const [next, was, key] of ends) {
      if (next === was) continue;
      if (next == null) {
        if (key === "target") req.clearTarget = true;
        else if (key === "low") req.clearLow = true;
        else req.clearHigh = true;
      } else {
        req[key] = next;
      }
    }
    return req;
  }

  function save() {
    if (problem || pending) return;
    const req = changes();
    if (Object.keys(req).length === 0) {
      onDone();
      return;
    }
    void run(() => writes.setMetric(metric.id, req));
  }

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        save();
      }}
    >
      <div className="grid gap-4 sm:grid-cols-[1fr_8rem]">
        <div className="space-y-1.5">
          <Label htmlFor={`${id}-name`}>Name</Label>
          <Input
            id={`${id}-name`}
            value={name}
            maxLength={NAME_MAX}
            disabled={pending}
            autoComplete="off"
            onChange={(e) => setName(e.target.value)}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor={`${id}-unit`}>Unit</Label>
          <Input
            id={`${id}-unit`}
            value={unit}
            maxLength={UNIT_MAX}
            disabled={pending || locked}
            autoComplete="off"
            aria-describedby={locked ? `${id}-unit-why` : undefined}
            onChange={(e) => setUnit(e.target.value)}
          />
        </div>
      </div>
      {locked ? (
        <p id={`${id}-unit-why`} className="-mt-2 text-xs text-muted-foreground">
          The unit stays: {metric.readings === 1 ? "its reading is" : `its ${metric.readings} readings are`} in{" "}
          {metric.unit}. For another unit, add a new metric.
        </p>
      ) : null}

      <ToggleGroup
        label="Decimal places"
        value={String(decimals)}
        onChange={(d) => setDecimals(Number(d))}
        options={Array.from({ length: DECIMALS_MAX + 1 }, (_, d) => ({ id: String(d), label: String(d) }))}
      />

      <div className="space-y-1.5">
        <Label htmlFor={`${id}-goal`}>Goal{unitText}, optional</Label>
        <Input
          id={`${id}-goal`}
          type="text"
          inputMode="decimal"
          autoComplete="off"
          value={goal}
          disabled={pending}
          placeholder="Leave blank for none"
          onChange={(e) => setGoal(e.target.value)}
        />
      </div>

      <ToggleGroup
        label="Better"
        value={better ?? ("" as MetricBetter)}
        onChange={setBetter}
        options={[
          { id: "lower", label: "Lower" },
          { id: "higher", label: "Higher" },
        ]}
      />

      <fieldset className="space-y-1.5">
        <legend className="text-sm font-medium">Normal range{unitText}, optional</legend>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor={`${id}-low`} className="text-xs text-muted-foreground">
              Low end
            </Label>
            <Input
              id={`${id}-low`}
              type="text"
              inputMode="decimal"
              autoComplete="off"
              value={low}
              disabled={pending}
              placeholder="none"
              onChange={(e) => setLow(e.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor={`${id}-high`} className="text-xs text-muted-foreground">
              High end
            </Label>
            <Input
              id={`${id}-high`}
              type="text"
              inputMode="decimal"
              autoComplete="off"
              value={high}
              disabled={pending}
              placeholder="none"
              onChange={(e) => setHigh(e.target.value)}
            />
          </div>
        </div>
        <p className="text-xs text-muted-foreground">The ends are normal; a reading past one is marked High or Low.</p>
      </fieldset>

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

      <DialogFooter className="gap-2">
        <Button
          type="button"
          variant="ghost"
          className="sm:mr-auto"
          disabled={pending}
          onClick={() =>
            void run(async () => {
              // Blood pressure's two go together, so neither is left logging alone.
              for (const m of group.metrics) await writes.archive(m.urn);
            })
          }
        >
          <ArchiveIcon />
          Archive {nameInText(group.name)}
        </Button>
        <Button type="button" variant="outline" disabled={pending} onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" disabled={pending || !!problem}>
          {pending ? "Saving…" : "Save"}
        </Button>
      </DialogFooter>
    </form>
  );
}

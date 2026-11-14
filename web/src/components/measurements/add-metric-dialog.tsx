import { useId, useState } from "react";
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
  parseValue,
  rangeProblem,
  starterChoices,
  starterText,
  type StarterChoice,
} from "@/lib/features/measurements/measurements";
import type { MetricBetter, MetricRow, MetricTemplate } from "@/lib/features/measurements/types";
import { useWrites } from "@/lib/queries";
import { useWriteAction } from "@/lib/write-action";

/**
 * Add a metric (MEASURE-3, measurements plan §4.8): one of the starter metrics not tracked yet,
 * which comes with its unit, decimal places, which way is better and (blood pressure) its normal
 * range, or one of his own: a name, a unit, decimal places, a goal, which way is better and a
 * normal range. Every reading is information only: none earns XP.
 */
export function AddMetricDialog({
  open,
  onOpenChange,
  metrics,
  catalog,
  onAdded,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  metrics: readonly MetricRow[];
  /** The starter list (`GET /api/measurements/catalog`). */
  catalog: readonly MetricTemplate[];
  /** The added group's key, so the page can open its row. */
  onAdded: (key: string) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[92dvh] overflow-y-auto sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Add a metric</DialogTitle>
          <DialogDescription>Information only: readings earn no XP. You can change any of it later.</DialogDescription>
        </DialogHeader>
        {open ? (
          <AddForm
            metrics={metrics}
            catalog={catalog}
            onDone={(key) => {
              onOpenChange(false);
              if (key) onAdded(key);
            }}
          />
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

function AddForm({
  metrics,
  catalog,
  onDone,
}: {
  metrics: readonly MetricRow[];
  catalog: readonly MetricTemplate[];
  onDone: (key: string | null) => void;
}) {
  const writes = useWrites();
  const id = useId();
  const choices = starterChoices(metrics, catalog);
  const { run, pending, error } = useWriteAction();
  const [name, setName] = useState("");
  const [unit, setUnit] = useState("");
  const [decimals, setDecimals] = useState(1);
  const [goal, setGoal] = useState("");
  const [better, setBetter] = useState<MetricBetter | "">("");
  const [low, setLow] = useState("");
  const [high, setHigh] = useState("");

  const optional = (raw: string, what: string) => (raw.trim() === "" ? { value: null } : parseValue(raw, decimals, what));
  const goalValue = optional(goal, "The goal");
  const lowValue = optional(low, "The normal range's low end");
  const highValue = optional(high, "The normal range's high end");
  const started = name.trim() !== "" || unit.trim() !== "";
  const problem = !started
    ? null
    : name.trim() === ""
      ? "Give the metric a name, e.g. Resting heart rate."
      : unit.trim() === ""
        ? "Give the metric a unit, e.g. lb, in, % or mg/dL."
        : "problem" in goalValue
          ? goalValue.problem
          : "problem" in lowValue
            ? lowValue.problem
            : "problem" in highValue
              ? highValue.problem
              : rangeProblem(lowValue.value, highValue.value, decimals);

  function addStarter(choice: StarterChoice) {
    void run(async () => {
      const tracked = new Set(metrics.filter((m) => m.archivedAt == null).map((m) => m.catalog));
      // By name, so the kernel fills in the rest; blood pressure adds whichever half is missing.
      for (const t of choice.templates) if (!tracked.has(t.key)) await writes.addMetric({ name: t.name });
      return choice.key;
    }, onDone);
  }

  function addOwn() {
    if (!started || problem || pending) return;
    void run(async () => {
      const row = await writes.addMetric({
        name: name.trim(),
        unit: unit.trim(),
        decimals,
        target: "value" in goalValue && goalValue.value != null ? goalValue.value : undefined,
        better: better || undefined,
        low: "value" in lowValue && lowValue.value != null ? lowValue.value : undefined,
        high: "value" in highValue && highValue.value != null ? highValue.value : undefined,
      });
      return row.catalog ?? row.id;
    }, onDone);
  }

  return (
    <div className="space-y-5">
      {choices.length > 0 ? (
        <section aria-labelledby={`${id}-starter`} className="space-y-2">
          <h3 id={`${id}-starter`} className="text-sm font-medium">
            From the starter list
          </h3>
          <ul className="space-y-2">
            {choices.map((choice) => (
              <li key={choice.key}>
                <button
                  type="button"
                  disabled={pending}
                  onClick={() => addStarter(choice)}
                  className="flex min-h-11 w-full flex-col items-start rounded-lg px-3 py-2 text-left ring-1 ring-foreground/10 hover:bg-muted/60 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none disabled:opacity-60"
                >
                  <span className="text-sm font-medium">Add {choice.name}</span>
                  <span className="text-xs text-muted-foreground">{starterText(choice)}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <form
        aria-labelledby={`${id}-own`}
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          addOwn();
        }}
      >
        <h3 id={`${id}-own`} className="text-sm font-medium">
          Your own
        </h3>
        <div className="grid gap-4 sm:grid-cols-[1fr_8rem]">
          <div className="space-y-1.5">
            <Label htmlFor={`${id}-name`}>Name</Label>
            <Input
              id={`${id}-name`}
              value={name}
              maxLength={NAME_MAX}
              disabled={pending}
              autoComplete="off"
              placeholder="Resting heart rate"
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor={`${id}-unit`}>Unit</Label>
            <Input
              id={`${id}-unit`}
              value={unit}
              maxLength={UNIT_MAX}
              disabled={pending}
              autoComplete="off"
              placeholder="bpm"
              onChange={(e) => setUnit(e.target.value)}
            />
          </div>
        </div>
        <ToggleGroup
          label="Decimal places"
          value={String(decimals)}
          onChange={(d) => setDecimals(Number(d))}
          options={Array.from({ length: DECIMALS_MAX + 1 }, (_, d) => ({ id: String(d), label: String(d) }))}
        />
        <div className="space-y-1.5">
          <Label htmlFor={`${id}-goal`}>Goal{unit.trim() ? ` (${unit.trim()})` : ""}, optional</Label>
          <Input
            id={`${id}-goal`}
            type="text"
            inputMode="decimal"
            autoComplete="off"
            value={goal}
            disabled={pending}
            onChange={(e) => setGoal(e.target.value)}
          />
        </div>
        <ToggleGroup
          label="Better"
          value={better as MetricBetter}
          onChange={(b) => setBetter((now) => (now === b ? "" : b))}
          options={[
            { id: "lower", label: "Lower" },
            { id: "higher", label: "Higher" },
          ]}
        />
        <fieldset className="space-y-1.5">
          <legend className="text-sm font-medium">Normal range{unit.trim() ? ` (${unit.trim()})` : ""}, optional</legend>
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
        <DialogFooter>
          <Button type="button" variant="outline" disabled={pending} onClick={() => onDone(null)}>
            Cancel
          </Button>
          <Button type="submit" disabled={pending || !started || !!problem}>
            {pending ? "Adding…" : name.trim() ? `Add ${name.trim()}` : "Add"}
          </Button>
        </DialogFooter>
      </form>
    </div>
  );
}

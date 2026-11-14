import { useId, useState } from "react";
import { WeightIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { parseValue, type MetricTemplate } from "@/lib/features/measurements/measurements";
import { useWrites } from "@/lib/queries";
import { errorText } from "@/lib/write-action";

/**
 * Before there's a Weight metric (MEASURE-1, measurements plan §4.7): Start tracking weight
 * makes it from the starter list's `weight` (lb, 1 decimal place, lower is better) and asks for a
 * goal, which can be left for later.
 */
export function StartTrackingCard({ weight }: { weight: MetricTemplate }) {
  const writes = useWrites();
  const id = useId();
  const [goal, setGoal] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const parsed = goal.trim() === "" ? null : parseValue(goal, weight.decimals, "The goal");
  const problem = parsed && "problem" in parsed ? parsed.problem : null;

  async function start() {
    if (problem || pending) return;
    setError(null);
    setPending(true);
    try {
      await writes.addMetric({ name: weight.name, target: parsed && "value" in parsed ? parsed.value : undefined });
    } catch (e) {
      setError(errorText(e, "That didn't save. Nothing was written."));
    } finally {
      setPending(false);
    }
  }

  return (
    <section aria-labelledby={`${id}-title`} className="space-y-4 rounded-xl bg-card p-5 ring-1 ring-foreground/10">
      <div className="flex items-start gap-3">
        <span className="rounded-lg bg-muted p-2" aria-hidden>
          <WeightIcon className="size-5 text-muted-foreground" />
        </span>
        <div className="space-y-1">
          <h2 id={`${id}-title`} className="font-heading text-xl">
            Track your weight
          </h2>
          <p className="text-sm text-muted-foreground">
            Log each weigh-in, by hand for now. The first one each day is that day's number, and a smoothed trend
            shows where you're heading. Information only: it earns no XP.
          </p>
        </div>
      </div>
      <form
        className="space-y-3"
        onSubmit={(e) => {
          e.preventDefault();
          void start();
        }}
      >
        <div className="max-w-xs space-y-1.5">
          <Label htmlFor={`${id}-goal`}>Goal weight ({weight.unit}, optional)</Label>
          <Input
            id={`${id}-goal`}
            type="text"
            inputMode="decimal"
            autoComplete="off"
            value={goal}
            disabled={pending}
            placeholder="185"
            onChange={(e) => setGoal(e.target.value)}
          />
          {problem ? (
            <p className="text-xs text-muted-foreground" aria-live="polite">
              {problem}
            </p>
          ) : null}
        </div>
        {error ? (
          <p role="alert" className="text-xs text-destructive">
            {error}
          </p>
        ) : null}
        <Button type="submit" className="min-h-11 md:min-h-0" disabled={pending || !!problem}>
          {pending ? "Starting…" : "Start tracking weight"}
        </Button>
      </form>
    </section>
  );
}

import { useId, useState } from "react";
import { CalendarPlusIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { todayIso } from "@/lib/dates";
import { addMonthsClamped, parseRecurrence, recurrenceLabel } from "@/lib/recurrence";
import { useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { errorText } from "@/lib/write-action";

/** The furthest ahead Materialize reaches, as the kernel holds it (`AppointmentSeries.HorizonYears`). */
export const HORIZON_MONTHS = 24;

/**
 * A repeating Appointment's Materialize (CAL-1, D10; the Pilot's "Materialize…"): makes its
 * occurrences through a date, each with its own status. Three months ahead by default, as the
 * Pilot offers, and at most two years (code review CR65); running it again only fills in dates
 * not made yet.
 */
export function MaterializePanel({
  seriesId,
  recurrence,
  className,
}: {
  seriesId: string;
  recurrence: string;
  className?: string;
}) {
  const writes = useWrites();
  const field = useId();
  const [through, setThrough] = useState(() => addMonthsClamped(todayIso(), 3));
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const spec = parseRecurrence(recurrence);

  async function make() {
    if (!through) {
      setError("Pick the last day to make occurrences for.");
      return;
    }
    setError(null);
    setPending(true);
    try {
      await writes.materialize(seriesId, through);
    } catch (e) {
      setError(errorText(e, "Materialize failed."));
    } finally {
      setPending(false);
    }
  }

  return (
    <div className={cn("rounded-lg bg-muted/40 p-3 ring-1 ring-foreground/10", className)}>
      <p className="text-sm font-medium">{spec ? `Repeats: ${recurrenceLabel(spec)}` : "Repeats"}</p>
      <p className="mt-0.5 text-xs text-muted-foreground">
        Each occurrence is its own Appointment with its own status. Make them ahead through a day.
      </p>
      <div className="mt-2 flex flex-wrap items-end gap-2">
        <div className="grid gap-1">
          <Label htmlFor={field} className="text-xs">
            Through
          </Label>
          <Input
            id={field}
            type="date"
            value={through}
            min={todayIso()}
            max={addMonthsClamped(todayIso(), HORIZON_MONTHS)}
            onChange={(e) => setThrough(e.target.value)}
            className="h-8 w-40"
          />
        </div>
        <Button size="sm" variant="outline" onClick={() => void make()} disabled={pending}>
          <CalendarPlusIcon /> {pending ? "Making…" : "Make occurrences"}
        </Button>
      </div>
      {error ? <p className="mt-2 text-xs text-destructive">{error}</p> : null}
    </div>
  );
}

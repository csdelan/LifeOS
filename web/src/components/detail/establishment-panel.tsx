import { PlayIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ReadyToEstablish } from "@/components/habits/establish-dialog";
import { useWrites } from "@/lib/queries";
import type { HabitRow } from "@/lib/production-ui-types";
import {
  establishmentProgress,
  habitStage,
  isReadyToEstablish,
  targetFields,
  targetProblem,
  type TargetDraft,
} from "@/lib/establish";
import { useWriteAction } from "@/lib/write-action";

/**
 * Where a habit stands on its way to graduating, and the Establish suggestion; while editing a
 * Building habit, its target too — "Establish when followed 80 % over 60 days" — pre-filled
 * with what it is now.
 */
export function EstablishmentPanel({
  habit,
  editing,
  draft,
  onChange,
}: {
  habit: HabitRow;
  editing: boolean;
  draft: TargetDraft;
  onChange: (next: TargetDraft) => void;
}) {
  const stage = habitStage(habit);
  if (stage === "abandoned") return null;
  const establishment = habit.establishment;
  const fields = establishment ? targetFields(establishment) : null;
  const problem = editing ? targetProblem(draft) : null;
  return (
    <section className="rounded-xl bg-muted/40 p-3">
      <p className="type-scale-section text-muted-foreground">Establishing</p>
      {stage === "planned" ? (
        <div className="mt-1 flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm">
            Planned — not tracked yet, so no day is scheduled or missed. Its days count from the
            day you start tracking it.
          </p>
          {editing ? null : <StartTracking habit={habit} />}
        </div>
      ) : stage === "established" ? (
        <p className="mt-1 text-sm">
          Established — graduated from daily tracking. Each day you check it off under
          Maintaining still pays; move it back to Building to track it daily again.
        </p>
      ) : establishment ? (
        <div className="mt-1 flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm">{establishmentProgress(establishment)}</p>
          {isReadyToEstablish(habit) ? <ReadyToEstablish habit={habit} /> : null}
        </div>
      ) : null}
      {editing && stage === "building" && fields ? (
        <div className="mt-2 space-y-1">
          <div
            role="group"
            aria-label="Establish target"
            className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm"
          >
            <span>Establish when followed</span>
            <Input
              aria-label="Target percent of days followed"
              type="number"
              inputMode="decimal"
              min={1}
              max={100}
              step="any"
              className="h-7 w-16"
              value={draft.percent ?? fields.percent}
              onChange={(e) => onChange({ ...draft, percent: e.target.value })}
            />
            <span>% over</span>
            <Input
              aria-label="Target window in days"
              type="number"
              inputMode="numeric"
              min={1}
              max={3650}
              step={1}
              className="h-7 w-20"
              value={draft.days ?? fields.days}
              onChange={(e) => onChange({ ...draft, days: e.target.value })}
            />
            <span>days</span>
          </div>
          <p className="text-xs text-muted-foreground">
            A partial day counts half. Establishing is always your call.
          </p>
          {problem ? (
            <p role="alert" className="text-xs text-destructive">
              {problem}
            </p>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
/** Moves a Planned habit to Building: tracking starts today. */
function StartTracking({ habit }: { habit: HabitRow }) {
  const writes = useWrites();
  const reported = useWriteAction({ report: "toast" });
  return (
    <Button
      type="button"
      size="xs"
      variant="outline"
      aria-label={`Start tracking ${habit.name}`}
      onClick={() => void reported.run(() => writes.setStatus(habit.id, "Building"))}
    >
      <PlayIcon aria-hidden /> Start tracking
    </Button>
  );
}

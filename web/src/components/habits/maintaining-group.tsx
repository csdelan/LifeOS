import { ChevronRightIcon } from "lucide-react";
import { useId, useState } from "react";
import { MaintainButtons, type MaintainPick } from "@/components/habits/maintain-buttons";
import { HABIT_STATE_ROW } from "@/components/habits/habit-state-styles";
import { isMaintainedToday, maintainedDayLabel } from "@/lib/establish";
import type { HabitOccurrenceRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/**
 * Today's collapsed Maintaining group (REWARD-2 maintenance): each Established habit with a
 * one-tap check-off. The header counts what's done today; there is no red, no "missed" and
 * no streak, because a maintained habit has no schedule to fall behind on.
 */
export function MaintainingGroup({
  rows,
  onPick,
  onOpen,
  defaultOpen = false,
}: {
  rows: HabitOccurrenceRow[];
  onPick: (habitId: string, pick: MaintainPick) => void;
  onOpen: (habitId: string) => void;
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const listId = useId();
  if (rows.length === 0) return null;
  const done = rows.filter((r) => isMaintainedToday(r.state)).length;
  return (
    <div className="mt-3 border-t pt-3">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={listId}
        onClick={() => setOpen((v) => !v)}
        className="flex min-h-11 w-full items-center justify-between gap-2 rounded-lg px-2 text-left hover:bg-muted/70 md:min-h-9"
      >
        <span className="flex items-center gap-1.5">
          <ChevronRightIcon
            aria-hidden
            className={cn("size-3.5 text-muted-foreground transition-transform", open && "rotate-90")}
          />
          <span className="type-scale-section text-muted-foreground">Maintaining</span>
        </span>
        <span className="text-xs text-muted-foreground">
          {done} of {rows.length} done today
        </span>
      </button>
      {open ? (
        <ul id={listId} className="mt-2 space-y-2" aria-label="Maintaining">
          {rows.map((h) => (
            <li
              key={h.habitId}
              className={cn(
                "flex items-center justify-between rounded-lg px-2 py-1.5",
                isMaintainedToday(h.state) ? HABIT_STATE_ROW[h.state] : null,
              )}
            >
              <button type="button" className="min-h-11 text-left" onClick={() => onOpen(h.habitId)}>
                <p className="text-sm font-medium">{h.habitName}</p>
                <p className="text-[0.6875rem] text-muted-foreground">{maintainedDayLabel(h.state)}</p>
              </button>
              <MaintainButtons
                habitName={h.habitName}
                state={h.state}
                allowsPartial={h.allowsPartial}
                onPick={(pick) => onPick(h.habitId, pick)}
              />
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

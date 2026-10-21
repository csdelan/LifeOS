import { CheckIcon, MinusIcon } from "lucide-react";
import type { ReactNode } from "react";
import type { AdherenceState } from "@/lib/production-ui-types";
import { HABIT_STATE_SURFACE } from "@/components/habits/habit-state-styles";
import { cn } from "@/lib/utils";

/** What a maintained day's check-off records: tapping a lit button again undoes the day. */
export type MaintainPick = "followed" | "partial" | "missed";

/**
 * One-tap check-offs for an Established habit (REWARD-2 maintenance): Followed, and
 * Partial where the habit allows it. There is no "Not followed" button and nothing reads as
 * a miss: tapping the lit button again undoes the day (recorded as missed, which pays
 * nothing and gives back what the day paid).
 */
export function MaintainButtons({
  habitName,
  state,
  allowsPartial,
  onPick,
}: {
  habitName: string;
  state: AdherenceState;
  allowsPartial: boolean;
  onPick: (s: MaintainPick) => void;
}) {
  const toggle = (s: "followed" | "partial") => onPick(state === s ? "missed" : s);
  return (
    <div className="flex gap-1">
      <CheckOff
        label={`${habitName}: followed today`}
        tone="followed"
        active={state === "followed"}
        onClick={() => toggle("followed")}
      >
        <CheckIcon className="size-3.5" />
      </CheckOff>
      {allowsPartial ? (
        <CheckOff
          label={`${habitName}: partly followed today`}
          tone="partial"
          active={state === "partial"}
          onClick={() => toggle("partial")}
        >
          <MinusIcon className="size-3.5" />
        </CheckOff>
      ) : null}
    </div>
  );
}

function CheckOff({
  children,
  label,
  tone,
  active,
  onClick,
}: {
  children: ReactNode;
  label: string;
  tone: "followed" | "partial";
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      aria-pressed={active}
      onClick={onClick}
      className={cn(
        "flex size-11 items-center justify-center rounded-md border transition-colors md:size-7",
        active ? HABIT_STATE_SURFACE[tone] : "border-transparent text-muted-foreground hover:bg-muted",
      )}
    >
      {children}
    </button>
  );
}

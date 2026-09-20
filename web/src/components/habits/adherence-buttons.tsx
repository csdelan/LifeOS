import { CheckIcon, CircleDashedIcon, MinusIcon } from "lucide-react";
import type { ReactNode } from "react";
import type { AdherenceState } from "@/lib/production-ui-types";
import { HABIT_STATE_SURFACE } from "@/components/habits/habit-state-styles";
import { cn } from "@/lib/utils";

export function AdherenceButtons({
  state,
  allowsPartial,
  onPick,
}: {
  state: AdherenceState;
  allowsPartial: boolean;
  onPick: (s: "followed" | "partial" | "not_followed") => void;
}) {
  return (
    <div className="flex gap-1">
      <HabitBtn
        label="Followed"
        tone="followed"
        active={state === "followed"}
        onClick={() => onPick("followed")}
      >
        <CheckIcon className="size-3.5" />
      </HabitBtn>
      {allowsPartial ? (
        <HabitBtn
          label="Partial"
          tone="partial"
          active={state === "partial"}
          onClick={() => onPick("partial")}
        >
          <MinusIcon className="size-3.5" />
        </HabitBtn>
      ) : null}
      <HabitBtn
        label="Not followed"
        tone="not_followed"
        active={state === "not_followed"}
        onClick={() => onPick("not_followed")}
      >
        <CircleDashedIcon className="size-3.5" />
      </HabitBtn>
    </div>
  );
}

function HabitBtn({
  children,
  label,
  tone,
  active,
  onClick,
}: {
  children: ReactNode;
  label: string;
  tone: Exclude<AdherenceState, "unrecorded">;
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
          active
            ? HABIT_STATE_SURFACE[tone]
            : "border-transparent text-muted-foreground hover:bg-muted",
        )}
    >
      {children}
    </button>
  );
}

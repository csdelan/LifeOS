import { createElement } from "react";
import {
  ActivityIcon,
  BikeIcon,
  DumbbellIcon,
  Flower2Icon,
  FootprintsIcon,
  MountainIcon,
  PersonStandingIcon,
  SailboatIcon,
  WavesIcon,
  type LucideIcon,
} from "lucide-react";
import {
  dayName,
  formatDistance,
  formatDuration,
  formatTime,
  intensityWords,
  workoutName,
} from "@/lib/activity";
import type { WorkoutRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

const ICONS: Record<string, LucideIcon> = {
  running: FootprintsIcon,
  treadmillrunning: FootprintsIcon,
  walking: PersonStandingIcon,
  cycling: BikeIcon,
  indoorcycling: BikeIcon,
  mountainbiking: BikeIcon,
  strengthtraining: DumbbellIcon,
  weightlifting: DumbbellIcon,
  crosstraining: DumbbellIcon,
  swimming: WavesIcon,
  hiking: MountainIcon,
  yoga: Flower2Icon,
  pilates: Flower2Icon,
  rowing: SailboatIcon,
};

/** The activity's icon; a generic one for anything Oura adds that we don't know. */
function workoutIcon(activity: string): LucideIcon {
  return ICONS[activity.toLowerCase().replace(/[^a-z]/g, "")] ?? ActivityIcon;
}

/** The activity's icon as an element, decorative. */
export function WorkoutIcon({ activity, className }: { activity: string; className?: string }) {
  return createElement(workoutIcon(activity), { className, "aria-hidden": true });
}

/** "Tue, Oct 21 · 7:02 AM · 40 min · 420 cal · 5.2 km". */
function workoutFacts(w: WorkoutRow, today: string): string {
  return [
    dayName(w.day, today),
    formatTime(w.startedAt),
    formatDuration(w.seconds),
    w.calories != null ? `${w.calories.toLocaleString()} cal` : null,
    w.distanceM != null ? formatDistance(w.distanceM) : null,
  ]
    .filter(Boolean)
    .join(" · ");
}

/**
 * A workout as a list row (activity plan §3.7): its icon and name, the day, time, duration,
 * calories and distance, an intensity chip, and the note on its own line. One not counted is
 * muted and says why. The row is a button that opens the workout's sheet.
 */
export function WorkoutRowButton({
  workout,
  today,
  onOpen,
}: {
  workout: WorkoutRow;
  today: string;
  onOpen: (w: WorkoutRow) => void;
}) {
  const intensity = intensityWords(workout.intensity);
  return (
    <button
      type="button"
      data-testid="workout-row"
      data-counted={workout.counted ? "" : undefined}
      onClick={() => onOpen(workout)}
      className={cn(
        "flex min-h-11 w-full items-start gap-3 rounded-lg px-2 py-2 text-left hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
        !workout.counted && "text-muted-foreground",
      )}
    >
      <WorkoutIcon
        activity={workout.activity}
        className={cn("mt-0.5 size-4 shrink-0", workout.counted ? "text-primary" : "text-muted-foreground")}
      />
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
          <span className="font-medium">{workoutName(workout)}</span>
          {intensity ? (
            <span className="rounded-full bg-muted px-1.5 py-0.5 text-[0.6875rem] text-muted-foreground">{intensity}</span>
          ) : null}
          {!workout.counted ? (
            <span className="text-[0.6875rem]">Not counted: {workout.reason ?? "Unknown source"}</span>
          ) : null}
        </span>
        <span className="block text-xs text-muted-foreground tabular-nums">{workoutFacts(workout, today)}</span>
        {workout.note ? (
          <span data-testid="workout-note" className="mt-0.5 block text-sm text-foreground/90">
            {workout.note}
          </span>
        ) : null}
      </span>
    </button>
  );
}

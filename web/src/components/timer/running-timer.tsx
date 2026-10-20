import { Link } from "@tanstack/react-router";
import { PauseIcon, SquareIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { TimerStateBadge } from "@/components/timer/task-timer";
import { useTimerControl } from "@/components/timer/use-timer-control";
import { useTimers } from "@/lib/queries";
import type { TaskTimer } from "@/lib/production-ui-types";
import { formatClock, liveSeconds, useNow } from "@/lib/timer";
import { cn } from "@/lib/utils";

/**
 * The timer that is running right now, wherever you are (Toggl's header timer). Shows
 * nothing while no timer runs. The title opens the Task; Pause and Stop act in place.
 */
export function RunningTimer({ compact = false, className }: { compact?: boolean; className?: string }) {
  const { data } = useTimers();
  const running = (data ?? []).find((t) => t.state === "running");
  if (!running) return null;
  return <RunningTimerCard key={running.subjectId} timer={running} compact={compact} className={className} />;
}

function RunningTimerCard({
  timer,
  compact,
  className,
}: {
  timer: TaskTimer;
  compact: boolean;
  className?: string;
}) {
  const now = useNow(true);
  const { session } = liveSeconds(timer, now);
  const { run, pending } = useTimerControl(timer.subjectId);

  if (compact) {
    return (
      <Link
        to="/tasks"
        search={{ selected: timer.subjectId }}
        className={cn(
          "inline-flex h-8 min-w-0 items-center gap-1.5 rounded-full bg-success/12 px-2.5 text-xs font-medium text-success tabular-nums",
          className,
        )}
        aria-label={`Timer running on ${timer.subjectTitle}: ${formatClock(session)}. Open the task.`}
      >
        <span aria-hidden className="size-1.5 animate-pulse rounded-full bg-success" />
        {formatClock(session)}
      </Link>
    );
  }

  return (
    <section
      aria-label="Running timer"
      className={cn("rounded-lg border border-success/30 bg-success/8 p-2", className)}
    >
      <div className="flex items-center justify-between gap-2">
        <TimerStateBadge state="running" />
        <span className="font-heading text-base leading-none tabular-nums">{formatClock(session)}</span>
      </div>
      <Link
        to="/tasks"
        search={{ selected: timer.subjectId }}
        className="mt-1.5 block truncate text-sm font-medium hover:underline focus-visible:ring-2 focus-visible:ring-ring/50"
        title={timer.subjectTitle}
      >
        {timer.subjectTitle}
      </Link>
      <div className="mt-1.5 flex gap-1">
        <Button
          size="xs"
          variant="outline"
          className="flex-1"
          disabled={pending !== null}
          onClick={() => void run("pause")}
          aria-label={`Pause the timer on ${timer.subjectTitle}`}
        >
          <PauseIcon aria-hidden /> Pause
        </Button>
        <Button
          size="xs"
          variant="outline"
          className="flex-1"
          disabled={pending !== null}
          onClick={() => void run("stop")}
          aria-label={`Stop the timer on ${timer.subjectTitle}`}
        >
          <SquareIcon aria-hidden /> Stop
        </Button>
      </div>
    </section>
  );
}

import { CheckIcon, PauseIcon, PlayIcon, SquareIcon, TimerIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useTimerControl } from "@/components/timer/use-timer-control";
import { useTimer } from "@/lib/queries";
import type { TaskTimer, TimerAction, TimerState } from "@/lib/production-ui-types";
import {
  availableActions,
  formatClock,
  formatTracked,
  liveSeconds,
  parseEstimateSeconds,
  useNow,
} from "@/lib/timer";
import { cn } from "@/lib/utils";

const ACTION_LABEL: Record<TimerAction, string> = {
  start: "Start",
  pause: "Pause",
  resume: "Resume",
  stop: "Stop",
  complete: "Complete",
};

const ACTION_ICON: Record<TimerAction, typeof PlayIcon> = {
  start: PlayIcon,
  pause: PauseIcon,
  resume: PlayIcon,
  stop: SquareIcon,
  complete: CheckIcon,
};

/**
 * TASKS-2: the optional, Toggl-style timer on a Task. Unused, it is one quiet row with
 * a Start button; once running it shows the session clock, Pause / Resume, Stop, and
 * Complete (stop, then mark the Task Completed). Recorded time sums every session and
 * is compared with the Task's estimate when one parses.
 */
export function TaskTimerPanel({
  taskId,
  estimate,
  className,
}: {
  taskId: string;
  /** The Task's free-text `estimated_duration`, e.g. "90m". */
  estimate?: string | null;
  className?: string;
}) {
  const { data: timer, isLoading } = useTimer(taskId);
  const { run, pending } = useTimerControl(taskId);
  const state: TimerState = timer?.state ?? "stopped";
  const now = useNow(state === "running");
  const { total, session } = liveSeconds(timer, now);
  const estimateSeconds = parseEstimateSeconds(estimate);
  const active = state !== "stopped";

  return (
    <section
      aria-label="Timer"
      className={cn("rounded-lg border bg-muted/30 px-3 py-2", className)}
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        {active ? (
          <>
            <TimerStateBadge state={state} />
            <span
              role="timer"
              aria-label={`Session time ${formatClock(session)}`}
              className="font-heading text-xl leading-none tabular-nums"
            >
              {formatClock(session)}
            </span>
          </>
        ) : (
          <span className="inline-flex items-center gap-1.5 text-sm text-muted-foreground">
            <TimerIcon className="size-4" aria-hidden />
            Timer
          </span>
        )}
        <div className="ml-auto flex flex-wrap gap-1.5">
          {availableActions(state).map((action) => {
            const Icon = ACTION_ICON[action];
            const primary = action === "start" || action === "resume";
            return (
              <Button
                key={action}
                size="sm"
                variant={primary ? "default" : "outline"}
                disabled={pending !== null || isLoading}
                onClick={() => void run(action)}
              >
                <Icon aria-hidden />
                {pending === action ? `${ACTION_LABEL[action]}…` : ACTION_LABEL[action]}
              </Button>
            );
          })}
        </div>
      </div>
      <TimerSummary
        timer={timer}
        total={total}
        estimateSeconds={estimateSeconds}
        showSessions={!active}
      />
      <p className="sr-only" aria-live="polite">
        {state === "running" ? "Timer running" : state === "paused" ? "Timer paused" : ""}
      </p>
    </section>
  );
}

function TimerSummary({
  timer,
  total,
  estimateSeconds,
  showSessions,
}: {
  timer: TaskTimer | null;
  total: number;
  estimateSeconds: number | null;
  showSessions: boolean;
}) {
  const parts: string[] = [];
  parts.push(total > 0 ? `${formatTracked(total)} recorded` : "No time recorded yet");
  if (showSessions && timer && timer.sessions > 1) parts.push(`${timer.sessions} sessions`);
  if (estimateSeconds) {
    const left = estimateSeconds - total;
    parts.push(
      left >= 0
        ? `${formatTracked(estimateSeconds)} estimated · ${formatTracked(left)} left`
        : `${formatTracked(estimateSeconds)} estimated · ${formatTracked(-left)} over`,
    );
  }
  const share = estimateSeconds ? Math.min(1, total / estimateSeconds) : 0;
  const over = !!estimateSeconds && total > estimateSeconds;

  return (
    <div className="mt-1.5">
      <p className="text-xs text-muted-foreground tabular-nums">{parts.join(" · ")}</p>
      {estimateSeconds && total > 0 ? (
        <div className="mt-1.5 h-1 overflow-hidden rounded-full bg-muted" aria-hidden>
          <div
            className={cn("h-full rounded-full", over ? "bg-attention" : "bg-primary")}
            style={{ width: `${Math.round(share * 100)}%` }}
          />
        </div>
      ) : null}
    </div>
  );
}

/** "Running" (with a pulsing dot) or "Paused". Nothing for a stopped timer. */
export function TimerStateBadge({ state, className }: { state: TimerState; className?: string }) {
  if (state === "stopped") return null;
  return (
    <span
      className={cn(
        "inline-flex h-5 items-center gap-1.5 rounded-full px-2 text-[0.6875rem] font-medium",
        state === "running" ? "bg-success/12 text-success" : "bg-attention/12 text-attention",
        className,
      )}
    >
      <span
        aria-hidden
        className={cn(
          "size-1.5 rounded-full",
          state === "running" ? "animate-pulse bg-success" : "bg-attention",
        )}
      />
      {state === "running" ? "Running" : "Paused"}
    </span>
  );
}

/**
 * Compact timer read-out for a Task row: a live clock while running, the frozen
 * session while paused, and the recorded total once stopped. Nothing for an untimed task.
 */
export function TimerChip({ timer }: { timer?: TaskTimer | null }) {
  const now = useNow(timer?.state === "running");
  if (!timer || (timer.state === "stopped" && timer.totalSeconds <= 0)) return null;
  const { total, session } = liveSeconds(timer, now);

  if (timer.state === "stopped") {
    return (
      <span
        className="inline-flex h-5 items-center gap-1 rounded-full bg-muted px-2 text-[0.6875rem] font-medium text-muted-foreground tabular-nums"
        title={`${formatTracked(total)} recorded`}
      >
        <TimerIcon className="size-3" aria-hidden />
        <span className="sr-only">Time recorded: </span>
        {formatTracked(total)}
      </span>
    );
  }

  const running = timer.state === "running";
  return (
    <span
      className={cn(
        "inline-flex h-5 items-center gap-1.5 rounded-full px-2 text-[0.6875rem] font-medium tabular-nums",
        running ? "bg-success/12 text-success" : "bg-attention/12 text-attention",
      )}
      title={`${running ? "Running" : "Paused"} · ${formatTracked(total)} recorded`}
    >
      <span
        aria-hidden
        className={cn("size-1.5 rounded-full", running ? "animate-pulse bg-success" : "bg-attention")}
      />
      <span className="sr-only">{running ? "Timer running: " : "Timer paused: "}</span>
      {formatClock(session)}
    </span>
  );
}

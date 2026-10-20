import { useState } from "react";
import { toast } from "sonner";
import { useWrites } from "@/lib/queries";
import type { TimerAction } from "@/lib/production-ui-types";
import { formatTracked } from "@/lib/timer";

/**
 * Runs timer actions with a pending flag and the user-facing messages: which other
 * timer a start paused, the time recorded on Complete, and the kernel's refusal text.
 */
export function useTimerControl(taskId: string) {
  const writes = useWrites();
  const [pending, setPending] = useState<TimerAction | null>(null);

  async function run(action: TimerAction) {
    setPending(action);
    try {
      const result = await writes.timer(taskId, action);
      for (const other of result.paused) toast(`Paused the timer on ${other.title}`);
      if (result.completed) {
        const spent = result.timer.totalSeconds;
        toast.success(
          spent > 0
            ? `Completed ${result.timer.subjectTitle} · ${formatTracked(spent)} recorded`
            : `Completed ${result.timer.subjectTitle}`,
        );
      }
    } catch (e) {
      toast.error(e instanceof Error && e.message ? e.message : "The timer did not change.");
    } finally {
      setPending(null);
    }
  }

  return { run, pending };
}

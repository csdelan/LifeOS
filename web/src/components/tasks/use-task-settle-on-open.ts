import { useEffect, useRef } from "react";
import { useWrites } from "@/lib/queries";

/**
 * Catches up repeating Tasks once, when the page opens (TASKS-3, recurring-tasks plan §7):
 * a calendar date that passed while the app was closed gets its repetition. There's no
 * background job, so the Tasks and Focus pages ask. It changes no status, and a failed
 * settle changes nothing on screen.
 */
export function useTaskSettleOnOpen() {
  const writes = useWrites();
  const asked = useRef(false);

  useEffect(() => {
    if (asked.current) return;
    asked.current = true;
    void writes.settleTasks().catch(() => undefined);
  }, [writes]);
}

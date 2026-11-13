import { useCallback, useRef, useState } from "react";
import { toast } from "sonner";

/** What a form says when a write fails with no words of its own. */
export const NOTHING_WRITTEN = "That didn't save. Nothing was written.";

/** What a failure says: the kernel's refusal when it gave one, else the fallback. */
export function errorText(e: unknown, fallback: string = NOTHING_WRITTEN): string {
  return e instanceof Error && e.message ? e.message : fallback;
}

export interface WriteActionOptions {
  /**
   * Where a failure is said (code review CR17). "inline" keeps it in `error`, for a form to show
   * beside its buttons and stay open on, and runs one write at a time. "toast" says it in a toast,
   * for a button with nowhere of its own to say it (a habit check-off, Archive), and lets each
   * click run, so ticking two habits quickly saves both.
   */
  report?: "inline" | "toast";
  /** Said when the failure carries no words of its own. */
  fallback?: string;
  /** Called when a write succeeds and its `run` named no `onDone` of its own. */
  onDone?: () => void;
}

export interface WriteAction {
  /**
   * Runs one write. Its failure is said, never thrown, and the answer comes back, or undefined
   * when it failed or (inline) another was still running.
   */
  run<T>(write: () => Promise<T>, onDone?: (result: T) => void): Promise<T | undefined>;
  /** A write is running. */
  pending: boolean;
  /** The last failure, inline; null while a write runs and after one succeeds. */
  error: string | null;
  /** Says a problem of the form's own beside a write's (a photo that didn't upload), or clears it. */
  setError(message: string | null): void;
}

/**
 * The one way a component runs a write and says when it fails. Before it, ten forms each kept a
 * copy of the same `run` and a dozen buttons fired writes whose failures went unsaid.
 */
export function useWriteAction({ report = "inline", fallback, onDone: done }: WriteActionOptions = {}): WriteAction {
  const [running, setRunning] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const inFlight = useRef(0);

  const run = useCallback(
    async <T>(write: () => Promise<T>, onDone?: (result: T) => void): Promise<T | undefined> => {
      if (report === "inline" && inFlight.current > 0) return undefined;
      inFlight.current += 1;
      setRunning((n) => n + 1);
      if (report === "inline") setError(null);
      let result: T;
      try {
        result = await write();
      } catch (e) {
        const message = errorText(e, fallback);
        if (report === "toast") toast.error(message);
        else setError(message);
        return undefined;
      } finally {
        inFlight.current -= 1;
        setRunning((n) => n - 1);
      }
      if (onDone) onDone(result);
      else done?.();
      return result;
    },
    [report, fallback, done],
  );

  return { run, pending: running > 0, error, setError };
}

/**
 * The backstop for a promise nothing waited on: its failure is said in a toast, not lost to the
 * console. Returns the way to stop listening.
 */
export function reportUnhandledRejections(target: Pick<Window, "addEventListener" | "removeEventListener">) {
  const listener = (event: PromiseRejectionEvent) => {
    const reason: unknown = event.reason;
    // A cancelled fetch or query is not a failure anyone needs to hear about.
    if (!(reason instanceof Error) || reason.name === "AbortError" || reason.name === "CancelledError") return;
    toast.error(errorText(reason, "Something went wrong."));
  };
  target.addEventListener("unhandledrejection", listener);
  return () => target.removeEventListener("unhandledrejection", listener);
}

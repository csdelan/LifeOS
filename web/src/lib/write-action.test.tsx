import { act, renderHook } from "@testing-library/react";
import { toast } from "sonner";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { errorText, NOTHING_WRITTEN, reportUnhandledRejections, useWriteAction } from "./write-action";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn() }),
}));

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

beforeEach(() => vi.mocked(toast.error).mockClear());

describe("errorText", () => {
  it("says the refusal, or the fallback when there's nothing to say", () => {
    expect(errorText(new Error("A Goal needs a target date."))).toBe("A Goal needs a target date.");
    expect(errorText(new Error(""), "Couldn't save.")).toBe("Couldn't save.");
    expect(errorText("boom")).toBe(NOTHING_WRITTEN);
  });
});

describe("useWriteAction", () => {
  it("keeps an inline failure for the form to show, and clears it when the next write starts", async () => {
    const { result } = renderHook(() => useWriteAction());
    await act(() => result.current.run(() => Promise.reject(new Error("Refused."))));
    expect(result.current.error).toBe("Refused.");
    expect(toast.error).not.toHaveBeenCalled();

    const next = deferred<string>();
    let running!: Promise<string | undefined>;
    act(() => {
      running = result.current.run(() => next.promise);
    });
    expect(result.current.error).toBeNull();
    expect(result.current.pending).toBe(true);
    await act(async () => {
      next.resolve("ok");
      await running;
    });
    expect(result.current.pending).toBe(false);
    await expect(running).resolves.toBe("ok");
  });

  it("runs one inline write at a time, so a double click saves once", async () => {
    const write = vi.fn(() => deferred<void>().promise);
    const { result } = renderHook(() => useWriteAction());
    act(() => {
      void result.current.run(write);
      void result.current.run(write);
    });
    expect(write).toHaveBeenCalledTimes(1);
  });

  it("calls the run's onDone with the answer, else the hook's, and neither when the write fails", async () => {
    const hookDone = vi.fn();
    const runDone = vi.fn();
    const { result } = renderHook(() => useWriteAction({ onDone: hookDone }));
    await act(() => result.current.run(() => Promise.resolve(7), runDone));
    expect(runDone).toHaveBeenCalledWith(7);
    expect(hookDone).not.toHaveBeenCalled();

    await act(() => result.current.run(() => Promise.resolve()));
    expect(hookDone).toHaveBeenCalledTimes(1);

    await act(() => result.current.run(() => Promise.reject(new Error("no"))));
    expect(hookDone).toHaveBeenCalledTimes(1);
  });

  it("says a toast failure in a toast with the fallback, and lets clicks run side by side", async () => {
    const { result } = renderHook(() => useWriteAction({ report: "toast", fallback: "That check-off didn't save." }));
    const first = deferred<void>();
    const second = vi.fn(() => Promise.reject("not an Error"));
    let one!: Promise<unknown>;
    let two!: Promise<unknown>;
    act(() => {
      one = result.current.run(() => first.promise);
      two = result.current.run(second);
    });
    expect(second).toHaveBeenCalledTimes(1);
    await act(async () => {
      first.resolve();
      await Promise.all([one, two]);
    });
    expect(toast.error).toHaveBeenCalledWith("That check-off didn't save.");
    expect(result.current.error).toBeNull();
    expect(result.current.pending).toBe(false);
  });

  it("says a write's own fallback in place of the hook's, and calls that write's onDone (code review CR102)", async () => {
    const { result } = renderHook(() => useWriteAction({ report: "toast", fallback: "The hook's words." }));
    const onDone = vi.fn();
    await act(async () => {
      await result.current.run(() => Promise.reject("not an Error"), { fallback: "Drop failed. It is still in your Inbox." });
      await result.current.run(() => Promise.reject(new Error("The kernel's refusal.")), { fallback: "Not said." });
      await result.current.run(() => Promise.reject("not an Error"));
      await result.current.run(() => Promise.resolve(7), { onDone, fallback: "Not said either." });
    });
    expect(vi.mocked(toast.error).mock.calls.map(([m]) => m)).toEqual([
      "Drop failed. It is still in your Inbox.",
      "The kernel's refusal.",
      "The hook's words.",
    ]);
    expect(onDone).toHaveBeenCalledWith(7);
  });
});

describe("reportUnhandledRejections", () => {
  it("toasts a failure nothing waited on, but not a cancelled request", () => {
    const target = new EventTarget();
    const stop = reportUnhandledRejections(target as unknown as Window);
    const fire = (reason: unknown) => {
      const event = new Event("unhandledrejection") as PromiseRejectionEvent;
      Object.defineProperty(event, "reason", { value: reason });
      target.dispatchEvent(event);
    };
    fire(new Error("The habit isn't scheduled that day."));
    const abort = new Error("aborted");
    abort.name = "AbortError";
    fire(abort);
    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledWith("The habit isn't scheduled that day.");

    stop();
    fire(new Error("after"));
    expect(toast.error).toHaveBeenCalledTimes(1);
  });
});

const sources = import.meta.glob<string>(["/src/**/*.{ts,tsx}", "!/src/**/*.test.{ts,tsx}"], {
  query: "?raw",
  import: "default",
  eager: true,
});

/**
 * Where code still says a failure in a toast of its own, and why `useWriteAction` doesn't fit:
 * mostly a write that went in while a step after it didn't, which the toast says beside the
 * success.
 */
const OWN_FAILURE_TOASTS: Record<string, string> = {
  "/src/lib/write-action.ts": "the hook itself, and the backstop for a promise nothing waited on",
  "/src/components/create/new-subject-dialog.tsx": "the item is made, and a detail after it didn't save",
  "/src/components/environment/walk-through-sheet.tsx": "the ratings are in, and some photos didn't save",
  "/src/components/food/log-food-sheet.tsx": "the meal is logged, and its photo didn't save",
  "/src/components/measurements/lab-visit-sheet.tsx": "the visit is saved, and its report didn't upload",
  "/src/components/journal/journal-editor.tsx": "it adds to the refusal that the entry is still in the editor",
  "/src/pages/gallery-page.tsx": "the gallery shows what a failure toast looks like",
};

describe("a write's failure is said through useWriteAction (code review CR102)", () => {
  it("leaves failure toasts of their own to the writes that went in only in part", () => {
    const own = Object.entries(sources)
      .filter(([, source]) => /\btoast\.error\(/.test(source))
      .map(([file]) => file)
      .sort();
    expect(own).toEqual(Object.keys(OWN_FAILURE_TOASTS).sort());
  });
});

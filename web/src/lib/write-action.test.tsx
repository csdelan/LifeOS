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

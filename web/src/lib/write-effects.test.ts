import { QueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiRequestError } from "@/lib/live/http";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";
import { qk } from "@/lib/query-keys";
import { decorateWrites, LANDED_UNSHOWN, writeEffects } from "@/lib/write-effects";

afterEach(() => {
  vi.restoreAllMocks();
});

/** A write client whose every write is refused with `error`. */
function refusing(error: Error): LifeOsWriteClient {
  return new Proxy({} as LifeOsWriteClient, {
    get: () => async () => {
      throw error;
    },
  });
}

type Name = keyof LifeOsWriteClient;

/** A query key's first segment, calling a key that takes arguments with a placeholder. */
function first(key: unknown): string {
  const made = typeof key === "function" ? (key as (...args: unknown[]) => unknown)("x", "y", "z") : key;
  return String((made as unknown[])[0]);
}
const names = Object.keys(writeEffects) as Name[];

/** Runs `name` refused with `error` and says whether it refreshed anything. */
async function refreshesAfter(name: Name, error: Error): Promise<boolean> {
  const qc = new QueryClient();
  const refreshed = vi.spyOn(qc, "invalidateQueries");
  const write = decorateWrites(refusing(error), qc)[name] as (...args: unknown[]) => Promise<unknown>;
  await expect(write("s1", "s2", "s3")).rejects.toBe(error);
  return refreshed.mock.calls.length > 0;
}

// Code review CR83: one table says what every write does besides itself, and one wrapper runs it.
describe("the write effects", () => {
  it("cover every write both clients have", () => {
    expect([...names].sort()).toEqual(Object.keys(createMockWriteClient()).sort());
  });

  it("refresh after a plain failure only for a write that showed something first, or that says why", async () => {
    vi.spyOn(toast, "success").mockImplementation(() => 1);
    const refreshing: Name[] = [];
    for (const name of names) {
      if (await refreshesAfter(name, new Error("No"))) refreshing.push(name);
    }
    expect(refreshing.sort()).toEqual(["adhere", "dismiss", "drop", "setStatus", "syncActivity"]);
    for (const name of refreshing) {
      const effect = writeEffects[name] as { optimistic?: unknown; refreshesOnFailure?: string };
      expect(effect.optimistic ?? effect.refreshesOnFailure).toBeTruthy();
    }
  });

  it("answer a write that landed as landed, even when what it shows afterwards fails (CR113)", async () => {
    const qc = new QueryClient();
    const client = createMockWriteClient();
    const answer = { subject: { id: "s1" }, awards: [], progress: [] };
    vi.spyOn(client, "setStatus").mockResolvedValue(answer as never);
    vi.spyOn(toast, "success").mockImplementation(() => {
      throw new Error("The toaster is gone.");
    });
    const warned = vi.spyOn(toast, "warning").mockImplementation(() => 1);
    const refreshed = vi.spyOn(qc, "invalidateQueries");

    await expect(decorateWrites(client, qc).setStatus("s1", "Completed")).resolves.toBe(answer);
    expect(warned).toHaveBeenCalledWith(LANDED_UNSHOWN);
    expect(refreshed).toHaveBeenCalled();
  });

  // Code review CR143: the review recap was refreshed by no write, so a check-off made from a
  // review left the recap it was made from stale. Every cached read is refreshed by some write,
  // or is listed here with why none changes it.
  it("refresh every cached read from some write, but the reads no write changes", () => {
    const noWriteChanges: Record<string, string> = {
      me: "who is signed in; a sign-in changes it, not a write",
      "bounty-defaults": "the kernel's defaults, which no write changes",
      "cover-search": "Unsplash's answer to a search",
      "metric-catalog": "the kernel's starter list of metrics, which no write changes",
      artifact: "a stored file's bytes, which never change once stored",
      agents: "Connected agents' writes run through useAgentAction, outside the table (code review, Web data #7)",
    };
    const refreshed = new Set<string>();
    for (const name of names) {
      const effect = writeEffects[name] as { refreshes?: (args: unknown[], answer?: unknown) => unknown };
      let keys: unknown;
      try {
        keys = effect.refreshes?.(["s1", "s2", "s3"], undefined);
      } catch {
        continue;
      }
      if (!Array.isArray(keys)) {
        if (keys) for (const key of Object.values(qk)) refreshed.add(first(key));
        continue;
      }
      for (const key of keys) refreshed.add(String((key as unknown[])[0]));
    }
    const missing = [...new Set(Object.values(qk).map(first))].filter((key) => !refreshed.has(key) && !(key in noWriteChanges));
    expect(missing).toEqual([]);
  });

  it("refresh the review recap when a status changes", () => {
    const keys = writeEffects.setStatus.refreshes?.(["s1", "Completed"] as never, undefined as never);
    expect(keys).toContainEqual(qk.recap);
  });

  it("refresh after a 409, since another window changed what was shown", async () => {
    const conflict = () => new ApiRequestError("The timer changed in another window.", 409);
    expect(await refreshesAfter("timer", conflict())).toBe(true);
    expect(await refreshesAfter("markException", conflict())).toBe(true);
    expect(await refreshesAfter("markException", new ApiRequestError("No", 400))).toBe(false);
  });

  it("refresh a timer's reads after its 409, and a completion's lists only once it lands", async () => {
    const qc = new QueryClient();
    for (const key of [qk.timers, qk.subjects, qk.wallet]) qc.setQueryData(key, []);
    await expect(
      decorateWrites(refusing(new ApiRequestError("Changed", 409)), qc).timer("t1", "complete"),
    ).rejects.toThrow("Changed");
    expect([qk.timers, qk.subjects, qk.wallet].map((key) => qc.getQueryState(key)?.isInvalidated)).toEqual([
      true,
      false,
      false,
    ]);
  });
});

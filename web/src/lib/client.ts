/**
 * Single factory for the LifeOS data seam.
 *
 * `VITE_API_BASE_URL` unset → in-memory mock (default, unchanged).
 * Set (e.g. http://localhost:5280) → live LifeOs.Api.
 *
 * The mock is reached only through a dynamic `import()` here (code review CR23), so a production
 * build keeps it in a chunk of its own that's fetched only in mock mode. A lint rule keeps the
 * rest of the app from importing `@/lib/mock`.
 */

import { isLiveApi } from "@/lib/api-base";
import { liveReads } from "@/lib/live/reads";
import { createLiveWriteClient } from "@/lib/live/write-client";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";
import type { LifeOsReads } from "@/lib/reads";

export { isLiveApi };

/**
 * An object whose every method waits for `load()`, once, and then calls the loaded object's
 * method of that name. Every member of the read and write seams is an async method, so callers
 * can't tell it from the object itself.
 */
export function lazily<T extends object>(load: () => Promise<T>): T {
  let loaded: Promise<T> | undefined;
  return new Proxy({} as T, {
    get(_target, key) {
      // Only the seam's methods: no symbols, and not a thenable, so awaiting the object is harmless.
      if (typeof key !== "string" || key === "then") return undefined;
      return async (...args: unknown[]) => {
        const target = await (loaded ??= load());
        const member: unknown = Reflect.get(target, key);
        if (typeof member !== "function") throw new TypeError(`The mock has no method ${key}.`);
        return (member as (...a: unknown[]) => unknown).apply(target, args);
      };
    },
  });
}

const mockReads = lazily<LifeOsReads>(() => import("@/lib/mock/api").then((m) => m.mockReads));

export function getReads(): LifeOsReads {
  return isLiveApi() ? liveReads : mockReads;
}

export function createWriteClient(): LifeOsWriteClient {
  return isLiveApi()
    ? createLiveWriteClient()
    : lazily<LifeOsWriteClient>(() => import("@/lib/mock/write-client").then((m) => m.createMockWriteClient()));
}

/** The mock store's state, for a mock-mode stand-in that isn't one of the seams (Settings' connected agents). */
export async function mockState() {
  return (await import("@/lib/mock/store")).getState();
}

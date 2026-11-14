import { describe, expect, it, vi } from "vitest";
import { isMissingChunk, listenForNewBuild, reloadForNewBuild, type ReloadEnv } from "@/lib/new-build";

function env(now = 1_000_000): ReloadEnv & { reloads: number; at: { now: number } } {
  const store = new Map<string, string>();
  const at = { now };
  const e = {
    storage: { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v) },
    reloads: 0,
    at,
    reload: () => {
      e.reloads += 1;
    },
    now: () => at.now,
  };
  return e;
}

describe("a file of the build the page came from is gone (code review CR93)", () => {
  it("knows each browser's words for it, and Vite's for a stylesheet", () => {
    for (const message of [
      "Failed to fetch dynamically imported module: https://app/assets/browse-page-Ab12.js",
      "error loading dynamically imported module: https://app/assets/browse-page-Ab12.js",
      "Importing a module script failed.",
      "Unable to preload CSS for /assets/map-page-Cd34.css",
    ]) {
      expect(isMissingChunk(new TypeError(message)), message).toBe(true);
    }
    expect(isMissingChunk(new Error("Cannot read properties of undefined"))).toBe(false);
    expect(isMissingChunk(undefined)).toBe(false);
  });

  it("reloads once, and not again while that reload is a minute old", () => {
    const e = env();
    expect(reloadForNewBuild(e)).toBe(true);
    expect(reloadForNewBuild(e)).toBe(false);
    e.at.now += 59_000;
    expect(reloadForNewBuild(e)).toBe(false);
    expect(e.reloads).toBe(1);

    // A later deploy is a new missing file, and reloading for it is worth trying again.
    e.at.now += 2_000;
    expect(reloadForNewBuild(e)).toBe(true);
    expect(e.reloads).toBe(2);
  });

  it("doesn't reload when there's no storage to stop a loop", () => {
    const e = { ...env(), storage: null };
    expect(reloadForNewBuild(e)).toBe(false);
    const throwing = {
      ...env(),
      storage: {
        getItem: () => {
          throw new DOMException("blocked");
        },
        setItem: () => {},
      },
    };
    expect(reloadForNewBuild(throwing)).toBe(false);
    expect(throwing.reloads).toBe(0);
  });

  it("reloads when Vite says a lazily loaded file couldn't be fetched, until it stops listening", () => {
    const e = env();
    const stop = listenForNewBuild(window, e);
    window.dispatchEvent(new Event("vite:preloadError"));
    expect(e.reloads).toBe(1);

    stop();
    e.at.now += 120_000;
    window.dispatchEvent(new Event("vite:preloadError"));
    expect(e.reloads).toBe(1);
  });

  it("leaves the error to reach its boundary", () => {
    const e = env();
    const stop = listenForNewBuild(window, e);
    const event = new Event("vite:preloadError", { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
    stop();
    vi.restoreAllMocks();
  });
});

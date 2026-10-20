import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { celebrate, isSoundOn, launchConfetti, setSoundOn } from "@/lib/celebrate";

function reduceMotion(reduce: boolean) {
  vi.spyOn(window, "matchMedia").mockImplementation(
    (query: string) =>
      ({
        matches: reduce && query.includes("prefers-reduced-motion"),
        media: query,
        onchange: null,
        addEventListener: () => {},
        removeEventListener: () => {},
        addListener: () => {},
        removeListener: () => {},
        dispatchEvent: () => false,
      }) as MediaQueryList,
  );
}

/** A Web Audio stand-in that counts the oscillators it starts. */
function fakeAudio() {
  const started: number[] = [];
  class FakeContext {
    state = "running";
    currentTime = 0;
    destination = {};
    resume() {
      return Promise.resolve();
    }
    createGain() {
      const param = { setValueAtTime() {}, exponentialRampToValueAtTime() {} };
      return { gain: param, connect: (next: unknown) => next };
    }
    createOscillator() {
      return {
        type: "",
        frequency: { setValueAtTime(f: number) { started.push(f); } },
        connect: (next: unknown) => next,
        start() {},
        stop() {},
      };
    }
  }
  vi.stubGlobal("AudioContext", FakeContext);
  return started;
}

describe("celebrate", () => {
  beforeEach(() => {
    localStorage.clear();
    document.querySelectorAll(".confetti-layer").forEach((el) => el.remove());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("throws confetti, hidden from screen readers, and clears it away", () => {
    vi.useFakeTimers();
    reduceMotion(false);

    expect(launchConfetti()).toBe(true);
    const layer = document.querySelector(".confetti-layer");
    expect(layer?.getAttribute("aria-hidden")).toBe("true");
    expect(layer?.querySelectorAll(".confetti-piece").length).toBeGreaterThan(20);

    vi.advanceTimersByTime(5_000);
    expect(document.querySelector(".confetti-layer")).toBeNull();
  });

  it("throws no confetti under reduced motion, but still plays the fanfare", () => {
    reduceMotion(true);
    const tones = fakeAudio();

    expect(celebrate()).toEqual({ confetti: false, sound: true });
    expect(document.querySelector(".confetti-layer")).toBeNull();
    expect(tones.length).toBeGreaterThan(0);
  });

  it("stays silent when muted, and remembers the choice", () => {
    reduceMotion(false);
    const tones = fakeAudio();
    expect(isSoundOn()).toBe(true);

    setSoundOn(false);

    expect(isSoundOn()).toBe(false);
    expect(celebrate()).toEqual({ confetti: true, sound: false });
    expect(tones).toEqual([]);
  });

  it("goes quietly without Web Audio", () => {
    reduceMotion(false);
    vi.stubGlobal("AudioContext", undefined);

    expect(celebrate().sound).toBe(false);
  });
});

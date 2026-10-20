/**
 * Feedback for rewards (REWARD-4): a small chime on every earn, and confetti with a short
 * fanfare for redemptions and the big earns. Sounds are
 * synthesized with Web Audio, so there are no audio files to ship or license. Sound has
 * one mute switch, a per-device preference kept in browser storage, not data. Confetti
 * never runs under the system's reduced-motion setting; sound is not motion, so it
 * follows only the mute switch.
 */
import { useCallback, useState } from "react";
import { prefersReducedMotion } from "@/lib/media-query";
import { loadView, saveView } from "@/lib/view-state";

const SOUND_VIEW = "rewards.sound";

export function isSoundOn(): boolean {
  return loadView(SOUND_VIEW, { on: true }).on !== false;
}

export function setSoundOn(on: boolean) {
  try {
    saveView(SOUND_VIEW, { on });
  } catch {
    // Storage can be full or blocked; the switch still works for this page view.
  }
}

/** The mute switch's state, remembered on this device. */
export function useSoundOn(): [boolean, (on: boolean) => void] {
  const [on, setOn] = useState(isSoundOn);
  const update = useCallback((next: boolean) => {
    setSoundOn(next);
    setOn(next);
  }, []);
  return [on, update];
}

type AudioContextCtor = typeof AudioContext;
let context: AudioContext | null = null;

function audio(): AudioContext | null {
  if (typeof window === "undefined") return null;
  const Ctor: AudioContextCtor | undefined =
    window.AudioContext ??
    (window as unknown as { webkitAudioContext?: AudioContextCtor }).webkitAudioContext;
  if (!Ctor) return null;
  try {
    // One context per page; browsers cap how many may exist.
    if (!(context instanceof Ctor)) context = new Ctor();
    if (context.state === "suspended") void context.resume();
    return context;
  } catch {
    return null;
  }
}

function tone(ac: AudioContext, frequency: number, start: number, length: number, peak: number) {
  const osc = ac.createOscillator();
  const gain = ac.createGain();
  osc.type = "triangle";
  osc.frequency.setValueAtTime(frequency, start);
  gain.gain.setValueAtTime(0.0001, start);
  gain.gain.exponentialRampToValueAtTime(peak, start + 0.02);
  gain.gain.exponentialRampToValueAtTime(0.0001, start + length);
  osc.connect(gain).connect(ac.destination);
  osc.start(start);
  osc.stop(start + length + 0.05);
}

/** A rising C–E–G, then a held C major chord: about a second. False when there's no audio. */
export function playFanfare(): boolean {
  const ac = audio();
  if (!ac) return false;
  const t = ac.currentTime + 0.02;
  [523.25, 659.25, 783.99].forEach((f, i) => tone(ac, f, t + i * 0.11, 0.2, 0.16));
  [523.25, 659.25, 783.99, 1046.5].forEach((f) => tone(ac, f, t + 0.36, 0.95, 0.09));
  return true;
}

/** Two quick bright notes, a coin's "ding": under half a second. False when there's no audio. */
export function playChime(): boolean {
  const ac = audio();
  if (!ac) return false;
  const t = ac.currentTime + 0.02;
  tone(ac, 1318.51, t, 0.18, 0.08);
  tone(ac, 1975.53, t + 0.08, 0.3, 0.06);
  return true;
}

/** The small chime, unless muted. */
export function chime(): boolean {
  return isSoundOn() && playChime();
}

const CONFETTI_COLORS = [
  "var(--type-reward)",
  "var(--primary)",
  "var(--attention)",
  "var(--type-project)",
  "var(--type-value)",
  "var(--type-idea)",
];
const CONFETTI_PIECES = 90;
const CONFETTI_MS = 2600;

/** Confetti across the window. False (and nothing added) under reduced motion. */
export function launchConfetti(): boolean {
  if (typeof document === "undefined" || prefersReducedMotion()) return false;
  const layer = document.createElement("div");
  layer.className = "confetti-layer";
  layer.setAttribute("aria-hidden", "true");
  layer.dataset.testid = "confetti";
  for (let i = 0; i < CONFETTI_PIECES; i++) {
    const piece = document.createElement("span");
    piece.className = "confetti-piece";
    const style = piece.style;
    style.setProperty("--x", `${Math.random() * 100}vw`);
    style.setProperty("--dx", `${(Math.random() - 0.5) * 40}vw`);
    style.setProperty("--r", `${(Math.random() - 0.5) * 1440}deg`);
    style.setProperty("--w", `${6 + Math.random() * 6}px`);
    style.setProperty("--d", `${1.4 + Math.random() * 1}s`);
    style.setProperty("--delay", `${Math.random() * 0.25}s`);
    style.setProperty("--c", CONFETTI_COLORS[i % CONFETTI_COLORS.length]);
    layer.appendChild(piece);
  }
  document.body.appendChild(layer);
  window.setTimeout(() => layer.remove(), CONFETTI_MS + 300);
  return true;
}

export interface Celebration {
  confetti: boolean;
  sound: boolean;
}

/** The full celebration: confetti unless motion is reduced, a fanfare unless muted. */
export function celebrate(): Celebration {
  const confetti = launchConfetti();
  const sound = isSoundOn() && playFanfare();
  return { confetti, sound };
}

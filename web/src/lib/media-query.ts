import { useEffect, useState } from "react";

/** Desktop / two-pane breakpoint — matches Tailwind `md`. */
export const MD_MIN_WIDTH = 768;

export function useMediaQuery(query: string, defaultValue = false): boolean {
  const [matches, setMatches] = useState(defaultValue);

  useEffect(() => {
    const mql = window.matchMedia(query);
    const onChange = () => setMatches(mql.matches);
    onChange();
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, [query]);

  return matches;
}

/** Defaults to desktop so first paint can rely on `hidden md:*` CSS to avoid overflow. */
export function useIsDesktop(): boolean {
  return useMediaQuery(`(min-width: ${MD_MIN_WIDTH}px)`, true);
}

export function prefersReducedMotion(): boolean {
  return (
    typeof window !== "undefined" &&
    window.matchMedia("(prefers-reduced-motion: reduce)").matches
  );
}

export function motionMs(ms: number): number {
  return prefersReducedMotion() ? 0 : ms;
}

import { useEffect, useState } from "react";

/** True once the browser is idle after the first page, and two seconds on at the latest. */
export function useIdleAfterFirstPage(): boolean {
  const [idle, setIdle] = useState(false);
  useEffect(() => {
    if (typeof window.requestIdleCallback === "function") {
      const id = window.requestIdleCallback(() => setIdle(true), { timeout: 2_000 });
      return () => window.cancelIdleCallback(id);
    }
    const id = window.setTimeout(() => setIdle(true), 1_000);
    return () => window.clearTimeout(id);
  }, []);
  return idle;
}

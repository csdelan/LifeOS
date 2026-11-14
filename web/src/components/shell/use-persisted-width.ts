import { useCallback, useEffect, useState, type PointerEvent as ReactPointerEvent } from "react";
import { loadView, saveView } from "@/lib/view-state";

function clamp(n: number, min: number, max: number) {
  return Math.min(max, Math.max(min, n));
}

/** A panel's width, remembered between visits, kept within `range`, and dragged or set. */
export function usePersistedWidth(
  key: string,
  initial: number,
  range: { min: number; max: number },
) {
  const { min, max } = range;
  const [width, setWidth] = useState(() => clamp(loadView(key, { width: initial }).width, min, max));
  const [dragging, setDragging] = useState(false);

  useEffect(() => {
    if (dragging) return;
    saveView(key, { width });
  }, [dragging, key, width]);

  const onPointerDown = useCallback(
    (event: ReactPointerEvent<HTMLElement>, invert = false) => {
      event.preventDefault();
      event.stopPropagation();
      const pointerId = event.pointerId;
      const startX = event.clientX;
      const startW = width;
      setDragging(true);
      document.body.style.cursor = "col-resize";
      document.body.style.userSelect = "none";

      const onMove = (e: PointerEvent) => {
        if (e.pointerId !== pointerId) return;
        const dx = invert ? startX - e.clientX : e.clientX - startX;
        setWidth(clamp(startW + dx, min, max));
      };
      const onUp = (e: PointerEvent) => {
        if (e.pointerId !== pointerId) return;
        window.removeEventListener("pointermove", onMove);
        window.removeEventListener("pointerup", onUp);
        window.removeEventListener("pointercancel", onUp);
        setDragging(false);
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
      };
      window.addEventListener("pointermove", onMove);
      window.addEventListener("pointerup", onUp);
      window.addEventListener("pointercancel", onUp);
    },
    [width, min, max],
  );

  const resize = useCallback((next: number) => setWidth(clamp(next, min, max)), [min, max]);

  return { width, dragging, onPointerDown, resize, min, max };
}

/** A panel's width and the ways to change it, as `usePersistedWidth` hands them out. */
export type PanelWidth = ReturnType<typeof usePersistedWidth>;

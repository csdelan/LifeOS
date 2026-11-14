import { useState, type KeyboardEvent, type MouseEvent, type PointerEvent } from "react";

/**
 * The point a chart reads out, moved by the pointer or the keyboard (code review CR98). Hovering
 * reads the point under the pointer; focus starts at the newest, the arrow keys move a point at a
 * time, Home and End go to the first and the newest, and Enter or a click opens it when the chart
 * can. Leaving or blurring the chart clears it. The bar chart, the trend chart and the rank chart
 * all read their points through it, so they behave alike.
 *
 * `props` go on the chart's focusable area, beside its own role, label and description.
 */
export function useChartCursor({
  count,
  nearest = (fraction) => Math.floor(fraction * count),
  onSelect,
}: {
  /** How many points the chart has, oldest first. */
  count: number;
  /** The point nearest a place across the chart, from 0 at its left edge to 1 at its right. */
  nearest?: (fraction: number) => number;
  /** Opens a point, on Enter or a click. */
  onSelect?: (i: number) => void;
}) {
  const [hovered, setHovered] = useState<number | null>(null);
  const last = count - 1;
  const clamp = (i: number) => Math.min(Math.max(i, 0), last);
  // A point the data no longer has (a shorter range) reads as none.
  const at = hovered != null && hovered < count ? hovered : null;

  function pick(clientX: number, el: HTMLElement): number | null {
    const box = el.getBoundingClientRect();
    if (count === 0 || box.width === 0) return null;
    const i = clamp(nearest((clientX - box.left) / box.width));
    setHovered(i);
    return i;
  }

  function onKeyDown(e: KeyboardEvent<HTMLElement>) {
    if (count === 0) return;
    const now = at ?? last;
    if (e.key === "Enter" && onSelect) {
      e.preventDefault();
      onSelect(now);
      return;
    }
    const next =
      e.key === "ArrowLeft" ? now - 1 : e.key === "ArrowRight" ? now + 1 : e.key === "Home" ? 0 : e.key === "End" ? last : null;
    if (next == null) return;
    e.preventDefault();
    setHovered(clamp(next));
  }

  return {
    at,
    props: {
      tabIndex: 0,
      onKeyDown,
      onFocus: () => setHovered((h) => h ?? (count > 0 ? last : null)),
      onBlur: () => setHovered(null),
      onPointerMove: (e: PointerEvent<HTMLElement>) => {
        pick(e.clientX, e.currentTarget);
      },
      onPointerLeave: () => setHovered(null),
      onClick: onSelect
        ? (e: MouseEvent<HTMLElement>) => {
            const i = pick(e.clientX, e.currentTarget);
            if (i != null) onSelect(i);
          }
        : undefined,
    },
  };
}

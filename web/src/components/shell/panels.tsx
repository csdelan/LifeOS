import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
} from "react";
import { cn } from "@/lib/utils";
import { loadView, saveView } from "@/lib/view-state";

function clamp(n: number, min: number, max: number) {
  return Math.min(max, Math.max(min, n));
}

export function usePersistedWidth(
  key: string,
  initial: number,
  range: { min: number; max: number },
) {
  const [width, setWidth] = useState(() =>
    clamp(loadView(key, { width: initial }).width, range.min, range.max),
  );
  const [dragging, setDragging] = useState(false);
  const widthRef = useRef(width);
  widthRef.current = width;
  const rangeRef = useRef(range);
  rangeRef.current = range;

  useEffect(() => {
    if (dragging) return;
    saveView(key, { width });
  }, [dragging, key, width]);

  const onPointerDown = useCallback((event: ReactPointerEvent<HTMLElement>, invert = false) => {
    event.preventDefault();
    event.stopPropagation();
    const pointerId = event.pointerId;
    const startX = event.clientX;
    const startW = widthRef.current;
    setDragging(true);
    document.body.style.cursor = "col-resize";
    document.body.style.userSelect = "none";

    const onMove = (e: PointerEvent) => {
      if (e.pointerId !== pointerId) return;
      const dx = invert ? startX - e.clientX : e.clientX - startX;
      const { min, max } = rangeRef.current;
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
  }, []);

  return { width, dragging, onPointerDown };
}

export function ResizeHandle({
  invert = false,
  edge,
  onPointerDown,
}: {
  invert?: boolean;
  edge: "start" | "end";
  onPointerDown: (event: ReactPointerEvent<HTMLElement>, invert?: boolean) => void;
}) {
  return (
    <button
      type="button"
      aria-orientation="vertical"
      aria-label="Resize panel"
      className={cn(
        "absolute inset-y-0 z-20 w-2 cursor-col-resize touch-none appearance-none border-0 bg-transparent p-0",
        "after:absolute after:inset-y-0 after:left-1/2 after:w-px after:-translate-x-1/2 after:bg-transparent after:transition-colors",
        "hover:after:bg-primary/50",
        edge === "start" ? "left-0 -translate-x-1/2" : "right-0 translate-x-1/2",
      )}
      onPointerDown={(e) => onPointerDown(e, invert)}
    />
  );
}

export function PeekPanel({
  open,
  children,
}: {
  open: boolean;
  children: ReactNode;
}) {
  const { width, dragging, onPointerDown } = usePersistedWidth("layout.peekWidth", 448, {
    min: 320,
    max: 800,
  });

  return (
    <aside
      className={cn(
        "relative shrink-0 overflow-hidden border-l bg-card shadow-(--shadow-peek)",
        !open && "border-l-0",
        !dragging && "transition-[width] duration-200 ease-hearth",
      )}
      style={{ width: open ? width : 0 }}
    >
      {open ? (
        <div className="relative h-full" style={{ width }}>
          <ResizeHandle edge="start" invert onPointerDown={onPointerDown} />
          <div className="h-full min-w-0 overflow-hidden">{children}</div>
        </div>
      ) : null}
    </aside>
  );
}

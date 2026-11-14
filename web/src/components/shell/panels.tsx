import { type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import { cn } from "@/lib/utils";
import { usePersistedWidth, type PanelWidth } from "@/components/shell/use-persisted-width";
import { useIsDesktop } from "@/lib/media-query";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";

/** How far an arrow key moves a panel's edge, and with Shift held. */
const STEP = 16;
const BIG_STEP = 64;

/**
 * The edge a panel is resized by: dragged with the pointer, or focused and moved with the arrow
 * keys (Shift for a bigger step), Home and End for the narrowest and widest (code review CR87).
 * It is a window splitter, a focusable separator whose value is the panel's width. `invert` is
 * for a panel's start edge, where moving the edge left widens the panel.
 */
export function ResizeHandle({
  panel,
  label,
  edge,
  invert = false,
}: {
  panel: PanelWidth;
  label: string;
  edge: "start" | "end";
  invert?: boolean;
}) {
  function onKeyDown(event: ReactKeyboardEvent<HTMLDivElement>) {
    const step = (event.shiftKey ? BIG_STEP : STEP) * (invert ? -1 : 1);
    const next =
      event.key === "ArrowRight"
        ? panel.width + step
        : event.key === "ArrowLeft"
          ? panel.width - step
          : event.key === "Home"
            ? panel.min
            : event.key === "End"
              ? panel.max
              : null;
    if (next == null) return;
    event.preventDefault();
    panel.resize(next);
  }

  return (
    <div
      role="separator"
      tabIndex={0}
      aria-label={label}
      aria-orientation="vertical"
      aria-valuenow={panel.width}
      aria-valuemin={panel.min}
      aria-valuemax={panel.max}
      className={cn(
        "absolute inset-y-0 z-20 hidden w-2 cursor-col-resize touch-none outline-none md:block",
        "after:absolute after:inset-y-0 after:left-1/2 after:w-px after:-translate-x-1/2 after:bg-transparent after:transition-colors",
        "hover:after:bg-primary/50 focus-visible:after:w-0.5 focus-visible:after:bg-ring",
        edge === "start" ? "left-0 -translate-x-1/2" : "right-0 translate-x-1/2",
      )}
      onPointerDown={(e) => panel.onPointerDown(e, invert)}
      onKeyDown={onKeyDown}
    />
  );
}

export function PeekPanel({
  open,
  onClose,
  title = "Subject detail",
  children,
}: {
  open: boolean;
  onClose?: () => void;
  title?: string;
  children: ReactNode;
}) {
  const isDesktop = useIsDesktop();
  const panel = usePersistedWidth("layout.peekWidth", 448, {
    min: 320,
    max: 800,
  });
  const { width, dragging } = panel;

  if (!isDesktop) {
    return (
      <Sheet
        open={open}
        onOpenChange={(next) => {
          if (!next) onClose?.();
        }}
      >
        <SheetContent
          side="right"
          showCloseButton={false}
          className="h-full w-full gap-0 p-0 data-[side=right]:w-full data-[side=right]:sm:max-w-none sm:max-w-none"
        >
          <SheetHeader className="sr-only">
            <SheetTitle>{title}</SheetTitle>
            <SheetDescription>
              Read-only first. Use Edit to change attributes and status. Close returns to the
              list.
            </SheetDescription>
          </SheetHeader>
          <div className="flex h-full min-h-0 flex-col overflow-hidden">{children}</div>
        </SheetContent>
      </Sheet>
    );
  }

  return (
    <aside
      className={cn(
        "relative hidden shrink-0 overflow-hidden border-l bg-card shadow-(--shadow-peek) md:block",
        !open && "border-l-0",
        !dragging && "transition-[width] duration-200 ease-hearth",
      )}
      style={{ width: open ? width : 0 }}
      aria-hidden={!open}
    >
      {open ? (
        <div className="relative h-full" style={{ width }}>
          <ResizeHandle panel={panel} label="Resize the detail panel" edge="start" invert />
          <div className="h-full min-w-0 overflow-hidden">{children}</div>
        </div>
      ) : null}
    </aside>
  );
}

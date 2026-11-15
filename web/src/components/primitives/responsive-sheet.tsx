import { useRef, type ReactNode } from "react";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { useCloseGuard } from "@/components/primitives/close-guard";
import { useIsDesktop } from "@/lib/media-query";
import { cn } from "@/lib/utils";

/**
 * The sheet a form or a record opens in: a bottom sheet on a phone, a side panel on a desktop
 * (code review CR96). Once something in it is typed or chosen, closing it by Escape, the close
 * button or a tap outside asks first, through `useCloseGuard`, which `FormDialog` shares; Discard
 * closes it, Stay keeps it. A form's own Save or Cancel closes it through `onOpenChange` directly,
 * so neither asks. Each opening starts afresh.
 */
export function ResponsiveSheet({
  open,
  onOpenChange,
  wide = false,
  focusSheet = false,
  guard = true,
  className,
  children,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Wider on a desktop, for a form laid out in columns. */
  wide?: boolean;
  /** Open on the sheet itself, not its first field, which on a phone raises the keyboard. */
  focusSheet?: boolean;
  /** False for a sheet with nothing to lose: it shows a record, or saves as it goes. */
  guard?: boolean;
  className?: string;
  children: ReactNode;
}) {
  const desktop = useIsDesktop();
  const content = useRef<HTMLDivElement>(null);
  const { requestOpenChange, contentProps, confirm } = useCloseGuard({ open, onOpenChange, guard });

  return (
    <Sheet open={open} onOpenChange={requestOpenChange}>
      <SheetContent
        ref={content}
        side={desktop ? "right" : "bottom"}
        className={cn(
          "gap-0",
          desktop ? (wide ? "w-full data-[side=right]:sm:max-w-xl" : "w-full sm:max-w-md") : "max-h-[92dvh] rounded-t-2xl",
          className,
        )}
        {...contentProps}
        onOpenAutoFocus={
          focusSheet
            ? (e) => {
                e.preventDefault();
                content.current?.focus();
              }
            : undefined
        }
      >
        {children}
      </SheetContent>
      {confirm}
    </Sheet>
  );
}

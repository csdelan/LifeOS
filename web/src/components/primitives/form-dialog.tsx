import type { KeyboardEvent, ReactNode } from "react";
import { Dialog, DialogContent } from "@/components/ui/dialog";
import { useCloseGuard } from "@/components/primitives/close-guard";

/**
 * The dialog a form opens in (fifth review R2): shadcn's dialog through the same guard as
 * `ResponsiveSheet`, so once something in it is typed or chosen, Escape, the close button or a tap
 * outside asks "Unsaved edits" before what's typed is thrown away. Its own Save or Cancel closes it
 * through `onOpenChange` directly, without asking.
 */
export function FormDialog({
  open,
  onOpenChange,
  guard,
  escapeDiscards,
  dirty,
  className,
  showCloseButton,
  onKeyDown,
  children,
  ...data
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** False for a dialog with nothing to lose. */
  guard?: boolean;
  /** Escape closes at once, as the captures' spec asks; a tap outside still asks. */
  escapeDiscards?: boolean;
  /** Something is held that a field's change doesn't show, such as a recording. */
  dirty?: boolean;
  className?: string;
  showCloseButton?: boolean;
  onKeyDown?: (e: KeyboardEvent<HTMLDivElement>) => void;
  children: ReactNode;
} & { [data: `data-${string}`]: string | undefined }) {
  const { requestOpenChange, contentProps, confirm } = useCloseGuard({
    open,
    onOpenChange,
    guard,
    escapeDiscards,
    dirty,
  });
  return (
    <Dialog open={open} onOpenChange={requestOpenChange}>
      <DialogContent
        className={className}
        showCloseButton={showCloseButton}
        onKeyDown={onKeyDown}
        {...data}
        {...contentProps}
      >
        {children}
      </DialogContent>
      {confirm}
    </Dialog>
  );
}

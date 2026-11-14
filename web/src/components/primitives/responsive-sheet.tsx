import { useRef, useState, type ReactNode } from "react";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { useIsDesktop } from "@/lib/media-query";
import { cn } from "@/lib/utils";

/** A control whose click is a choice in a form: a toggle, a switch, a checkbox, a radio, an option. */
const CHOICE = '[aria-pressed], [role="switch"], [role="checkbox"], [role="radio"], [role="option"]';

/**
 * The sheet a form or a record opens in: a bottom sheet on a phone, a side panel on a desktop
 * (code review CR96). Once something in it is typed or chosen, closing it by Escape, the close
 * button or a tap outside asks first, as leaving a page with an unsaved edit does; Discard closes
 * it, Stay keeps it. A form's own Save or Cancel closes it through `onOpenChange` directly, so
 * neither asks. Each opening starts afresh.
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
  const [touched, setTouched] = useState(false);
  const [asking, setAsking] = useState(false);
  const [openedAs, setOpenedAs] = useState(open);
  if (openedAs !== open) {
    setOpenedAs(open);
    if (open) {
      setTouched(false);
      setAsking(false);
    }
  }

  function touch() {
    if (guard && !touched) setTouched(true);
  }

  function requestOpenChange(next: boolean) {
    if (!next && guard && touched) {
      setAsking(true);
      return;
    }
    onOpenChange(next);
  }

  function discard() {
    setAsking(false);
    setTouched(false);
    onOpenChange(false);
  }

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
        onChange={touch}
        onClickCapture={(e) => {
          if (e.target instanceof Element && e.target.closest(CHOICE)) touch();
        }}
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
      <AlertDialog open={asking} onOpenChange={setAsking}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Unsaved edits</AlertDialogTitle>
            <AlertDialogDescription>
              What's typed here isn't saved. Discard it, or stay to finish it?
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Stay</AlertDialogCancel>
            <AlertDialogAction variant="outline" onClick={discard}>
              Discard
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Sheet>
  );
}

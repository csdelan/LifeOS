import { useRef, useState, type MouseEvent, type ReactNode } from "react";
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

/** A control whose click is a choice in a form: a toggle, a switch, a checkbox, a radio, an option. */
const CHOICE = '[aria-pressed], [role="switch"], [role="checkbox"], [role="radio"], [role="option"]';

export interface CloseGuardOptions {
  open: boolean;
  /** Told only once the closing is settled: at once, or after Discard. */
  onOpenChange: (open: boolean) => void;
  /** False for an overlay with nothing to lose: it shows a record, or saves as it goes. */
  guard?: boolean;
  /**
   * Escape closes at once, typed or not, where the spec asks for it (the captures:
   * ui-requirements, Quick capture and Voice capture). A tap outside still asks.
   */
  escapeDiscards?: boolean;
  /** Something is held that a field's change doesn't show, such as a recording. */
  dirty?: boolean;
}

export interface CloseGuard {
  /** The overlay's own `onOpenChange`: a close asks first once something is typed or chosen. */
  requestOpenChange: (open: boolean) => void;
  /** Spread on the overlay's content: they notice typing and choosing, and Escape. */
  contentProps: {
    onChange: () => void;
    onClickCapture: (e: MouseEvent) => void;
    onEscapeKeyDown: () => void;
  };
  /** The "Unsaved edits" question, rendered beside the content. */
  confirm: ReactNode;
}

/**
 * The guard a form's sheet or dialog closes through (code review CR96, fifth review R2): once
 * something in it is typed or chosen, closing it by Escape, the close button or a tap outside asks
 * "Unsaved edits" first, as leaving a page with an unsaved edit does. Discard closes it, Stay keeps
 * it. A form's own Save or Cancel calls its `onOpenChange` directly, so neither asks. Each opening
 * starts afresh.
 */
export function useCloseGuard({
  open,
  onOpenChange,
  guard = true,
  escapeDiscards = false,
  dirty = false,
}: CloseGuardOptions): CloseGuard {
  const [touched, setTouched] = useState(false);
  const [asking, setAsking] = useState(false);
  const [openedAs, setOpenedAs] = useState(open);
  // Radix calls onEscapeKeyDown just before it asks to close, so the close can tell it was Escape.
  const escaping = useRef(false);
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
    const viaEscape = escaping.current;
    escaping.current = false;
    if (!next && guard && (touched || dirty) && !viaEscape) {
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

  return {
    requestOpenChange,
    contentProps: {
      onChange: touch,
      onClickCapture: (e) => {
        if (e.target instanceof Element && e.target.closest(CHOICE)) touch();
      },
      onEscapeKeyDown: () => {
        if (escapeDiscards) escaping.current = true;
      },
    },
    confirm: (
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
    ),
  };
}

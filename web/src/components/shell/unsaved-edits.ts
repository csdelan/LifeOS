import { createContext, useContext } from "react";

/**
 * The shell's unsaved-edits guard. Whatever holds an edit (the detail panel, a review) says so
 * through `setDirty`; a page asks before moving away from it with `requestNavigation`, and the
 * router's own moves are blocked while it's set. The shell asks "Unsaved edits" and runs the move
 * only on Discard.
 */
export type UnsavedEdits = {
  dirty: boolean;
  setDirty: (dirty: boolean) => void;
  requestNavigation: (proceed: () => void) => void;
  /**
   * Counts the times Discard was chosen. What holds an edit and can stay on screen after the move
   * (the detail panel, when the same item stays open) keys itself by it, so Discard throws the
   * edit away rather than only stopping the shell asking about it (code review CR92).
   */
  discards: number;
};

export const UnsavedEditsCtx = createContext<UnsavedEdits>({
  dirty: false,
  setDirty: () => {},
  requestNavigation: (proceed) => proceed(),
  discards: 0,
});

export function useDirty(): UnsavedEdits {
  return useContext(UnsavedEditsCtx);
}

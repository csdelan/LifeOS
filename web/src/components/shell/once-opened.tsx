import { Suspense, useState, type ReactNode } from "react";

/**
 * Its children from the first time `open` is true, and kept after: a dialog loaded lazily is
 * fetched on its first open rather than with the shell, and still closes with its animation
 * (code review CR84). The shell also opens it to mount once the first page is idle
 * (`useIdleAfterFirstPage`, `lib/idle.ts`), so a first Ctrl+K types into a palette that's already there.
 */
export function OnceOpened({ open, children }: { open: boolean; children: ReactNode }) {
  const [opened, setOpened] = useState(open);
  // Adjusting state to a prop while rendering, as React's docs describe: no effect, no extra paint.
  if (open && !opened) setOpened(true);
  return opened ? <Suspense fallback={null}>{children}</Suspense> : null;
}

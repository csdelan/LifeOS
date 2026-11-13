/** Where focus does its own typing. */
const FIELD = 'input, textarea, select, [contenteditable=""], [contenteditable="true"]';

/** Popups that use the arrow keys and Enter themselves (Radix Select, menus, dialogs). */
const POPUP =
  '[role="dialog"], [role="alertdialog"], [role="listbox"], [role="menu"], [role="combobox"]';

/** Controls that Enter activates natively. */
const CONTROL =
  'button, a[href], [role="button"], [role="link"], [role="option"], [role="menuitem"], [role="tab"], [role="checkbox"], [role="switch"]';

/**
 * True when a page-wide shortcut should leave this key alone: something already handled it,
 * focus is in a field or a popup, or the key is Enter on a control that activates itself.
 */
export function keyBelongsToFocus(
  e: Pick<KeyboardEvent, "key" | "target" | "defaultPrevented">,
): boolean {
  if (e.defaultPrevented) return true;
  if (!(e.target instanceof Element)) return false;
  if (e.target.closest(FIELD) || e.target.closest(POPUP)) return true;
  return e.key === "Enter" && e.target.closest(CONTROL) !== null;
}

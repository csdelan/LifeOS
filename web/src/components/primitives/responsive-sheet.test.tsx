import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { ResponsiveSheet } from "@/components/primitives/responsive-sheet";
import { SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";

function Harness({ guard }: { guard?: boolean }) {
  const [open, setOpen] = useState(true);
  const [pressed, setPressed] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Open the form
      </button>
      <ResponsiveSheet open={open} onOpenChange={setOpen} guard={guard}>
        <SheetHeader>
          <SheetTitle>Log something</SheetTitle>
          <SheetDescription>A form in a sheet.</SheetDescription>
        </SheetHeader>
        <input aria-label="Name" />
        <button type="button" aria-pressed={pressed} onClick={() => setPressed((p) => !p)}>
          Large
        </button>
        <button type="button" onClick={() => setOpen(false)}>
          Save
        </button>
      </ResponsiveSheet>
    </>
  );
}

// By its title: while Unsaved edits is up, the sheet behind it is hidden from the accessibility tree.
const sheet = () => screen.queryByText("Log something");
const asked = () => screen.queryByRole("alertdialog", { name: "Unsaved edits" });

describe("a sheet with something typed in it asks before it closes (code review CR96)", () => {
  it("closes at once when nothing has been typed or chosen", async () => {
    render(<Harness />);
    await userEvent.keyboard("{Escape}");
    expect(sheet()).toBeNull();
    expect(asked()).toBeNull();
  });

  it("asks on Escape once a field is typed in; Stay keeps it and what's typed", async () => {
    render(<Harness />);
    await userEvent.type(screen.getByRole("textbox", { name: "Name" }), "Oatmeal");
    await userEvent.keyboard("{Escape}");
    expect(asked()).not.toBeNull();
    expect(sheet()).not.toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Stay" }));
    expect(asked()).toBeNull();
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveValue("Oatmeal");
  });

  it("asks from the close button once a choice is made, and Discard closes it", async () => {
    render(<Harness />);
    await userEvent.click(screen.getByRole("button", { name: "Large" }));
    await userEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(asked()).not.toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Discard" }));
    expect(asked()).toBeNull();
    expect(sheet()).toBeNull();
  });

  it("doesn't ask when the form closes it itself, and opens again afresh", async () => {
    render(<Harness />);
    await userEvent.type(screen.getByRole("textbox", { name: "Name" }), "Oatmeal");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(asked()).toBeNull();
    expect(sheet()).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Open the form" }));
    await userEvent.keyboard("{Escape}");
    expect(asked()).toBeNull();
    expect(sheet()).toBeNull();
  });

  it("never asks when it has nothing to lose", async () => {
    render(<Harness guard={false} />);
    await userEvent.type(screen.getByRole("textbox", { name: "Name" }), "Oatmeal");
    await userEvent.keyboard("{Escape}");
    expect(asked()).toBeNull();
    expect(sheet()).toBeNull();
  });
});

const sources = import.meta.glob<string>(
  ["/src/**/*.tsx", "!/src/**/*.test.tsx"],
  { query: "?raw", import: "default", eager: true },
);

/**
 * The sheets that don't open through ResponsiveSheet, and why. A new one is a form or a record
 * on a phone and a desktop alike, and opens through it, so what's typed in it is asked about.
 */
const OWN_SHEETS: Record<string, string> = {
  "/src/components/primitives/responsive-sheet.tsx": "the shared sheet itself",
  "/src/components/shell/app-shell.tsx": "the phone's navigation: links, nothing typed",
  "/src/components/shell/panels.tsx":
    "the detail panel on a phone: closing it is a move, which the shell's Unsaved edits asks about",
  "/src/pages/browse-page.tsx": "Browse's filters, which apply as they're chosen",
  "/src/pages/inbox-page.tsx":
    "an Inbox item on a phone: a half-typed tag or title goes with any move to another item too",
};

describe("every form or record sheet opens through ResponsiveSheet (code review CR96)", () => {
  it("leaves only the listed sheets to open shadcn's sheet themselves", () => {
    const own = Object.entries(sources)
      .filter(([, text]) => /<SheetContent\b/.test(text))
      .map(([path]) => path)
      .sort();
    expect(own).toEqual(Object.keys(OWN_SHEETS).sort());
  });
});

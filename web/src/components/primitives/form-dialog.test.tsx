import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { FormDialog } from "@/components/primitives/form-dialog";
import { DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

function Harness({ escapeDiscards, dirty }: { escapeDiscards?: boolean; dirty?: boolean }) {
  const [open, setOpen] = useState(true);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Open the form
      </button>
      <FormDialog open={open} onOpenChange={setOpen} escapeDiscards={escapeDiscards} dirty={dirty}>
        <DialogHeader>
          <DialogTitle>Add something</DialogTitle>
          <DialogDescription>A form in a dialog.</DialogDescription>
        </DialogHeader>
        <input aria-label="Name" />
        <button type="button" onClick={() => setOpen(false)}>
          Save
        </button>
      </FormDialog>
    </>
  );
}

// By its title: while Unsaved edits is up, the dialog behind it is hidden from the accessibility tree.
const dialog = () => screen.queryByText("Add something");
const asked = () => screen.queryByRole("alertdialog", { name: "Unsaved edits" });
// Radix waits for the click that follows a pointer down outside, so a tap is the whole click.
const tapOutside = () => userEvent.click(document.querySelector('[data-slot="dialog-overlay"]')!);

describe("a dialog with something typed in it asks before it closes (fifth review R2)", () => {
  it("closes at once when nothing has been typed", async () => {
    render(<Harness />);
    await userEvent.keyboard("{Escape}");
    expect(dialog()).toBeNull();
    expect(asked()).toBeNull();
  });

  it("asks on Escape once something is typed; Stay keeps it and Discard closes it", async () => {
    render(<Harness />);
    await userEvent.type(screen.getByLabelText("Name"), "Weight");
    await userEvent.keyboard("{Escape}");
    expect(asked()).not.toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Stay" }));
    expect(asked()).toBeNull();
    expect(screen.getByLabelText("Name")).toHaveValue("Weight");

    await userEvent.keyboard("{Escape}");
    await userEvent.click(screen.getByRole("button", { name: "Discard" }));
    expect(dialog()).toBeNull();
  });

  it("asks on a tap outside once something is typed", async () => {
    render(<Harness />);
    await userEvent.type(screen.getByLabelText("Name"), "Weight");
    await tapOutside();
    expect(asked()).not.toBeNull();
  });

  it("closes through its own Save without asking", async () => {
    render(<Harness />);
    await userEvent.type(screen.getByLabelText("Name"), "Weight");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(dialog()).toBeNull();
    expect(asked()).toBeNull();
  });

  it("lets Escape discard at once where the spec asks it to, and still asks on a tap outside", async () => {
    render(<Harness escapeDiscards />);
    await userEvent.type(screen.getByLabelText("Name"), "Weight");
    await tapOutside();
    expect(asked()).not.toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Stay" }));

    await userEvent.click(screen.getByLabelText("Name"));
    await userEvent.keyboard("{Escape}");
    expect(dialog()).toBeNull();
    expect(asked()).toBeNull();
  });

  it("asks when it holds something a field doesn't show, such as a recording", async () => {
    render(<Harness dirty />);
    await tapOutside();
    expect(asked()).not.toBeNull();
  });

  it("starts afresh each time it opens", async () => {
    render(<Harness />);
    await userEvent.type(screen.getByLabelText("Name"), "Weight");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    await userEvent.click(screen.getByRole("button", { name: "Open the form" }));
    await userEvent.keyboard("{Escape}");
    expect(dialog()).toBeNull();
    expect(asked()).toBeNull();
  });
});

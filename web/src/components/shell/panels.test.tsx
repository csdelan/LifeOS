import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import { ResizeHandle } from "@/components/shell/panels";
import { usePersistedWidth } from "@/components/shell/use-persisted-width";

function Panel({ invert = false }: { invert?: boolean }) {
  const panel = usePersistedWidth("test.panelWidth", 300, { min: 200, max: 400 });
  return <ResizeHandle panel={panel} label="Resize the test panel" edge={invert ? "start" : "end"} invert={invert} />;
}

function handle() {
  return screen.getByRole("separator", { name: "Resize the test panel" });
}

beforeEach(() => {
  localStorage.clear();
});

describe("ResizeHandle (code review CR87)", () => {
  it("is a focusable separator that says the panel's width and its limits", () => {
    render(<Panel />);
    const edge = handle();
    expect(edge).toHaveAttribute("tabindex", "0");
    expect(edge).toHaveAttribute("aria-orientation", "vertical");
    expect(edge).toHaveAttribute("aria-valuenow", "300");
    expect(edge).toHaveAttribute("aria-valuemin", "200");
    expect(edge).toHaveAttribute("aria-valuemax", "400");
  });

  it("moves with the arrow keys, further with Shift, and to either limit with Home and End", async () => {
    render(<Panel />);
    handle().focus();
    await userEvent.keyboard("{ArrowRight}");
    expect(handle()).toHaveAttribute("aria-valuenow", "316");
    await userEvent.keyboard("{Shift>}{ArrowLeft}{/Shift}");
    expect(handle()).toHaveAttribute("aria-valuenow", "252");
    await userEvent.keyboard("{End}");
    expect(handle()).toHaveAttribute("aria-valuenow", "400");
    await userEvent.keyboard("{ArrowRight}");
    expect(handle()).toHaveAttribute("aria-valuenow", "400");
    await userEvent.keyboard("{Home}");
    expect(handle()).toHaveAttribute("aria-valuenow", "200");
  });

  it("widens a panel from its start edge when the edge moves left", async () => {
    render(<Panel invert />);
    handle().focus();
    await userEvent.keyboard("{ArrowLeft}");
    expect(handle()).toHaveAttribute("aria-valuenow", "316");
  });

  it("keeps the width for next time", async () => {
    const { unmount } = render(<Panel />);
    handle().focus();
    await userEvent.keyboard("{End}");
    unmount();
    render(<Panel />);
    expect(handle()).toHaveAttribute("aria-valuenow", "400");
  });
});

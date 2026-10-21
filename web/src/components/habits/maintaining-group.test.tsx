import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { MaintainingGroup } from "@/components/habits/maintaining-group";
import type { AdherenceState, HabitOccurrenceRow } from "@/lib/production-ui-types";

function row(name: string, state: AdherenceState, allowsPartial = false): HabitOccurrenceRow {
  return {
    habitId: name.toLowerCase().replace(/\s+/g, "-"),
    habitUrn: `urn:bsk:habit:${name}`,
    habitName: name,
    occurrenceDate: "2026-10-21",
    state,
    allowsPartial,
  };
}

describe("MaintainingGroup (REWARD-2 maintenance)", () => {
  const rows = [row("Floss", "followed"), row("Walk", "unrecorded", true), row("Read", "not_followed")];

  it("starts collapsed, counting what's done today", async () => {
    render(<MaintainingGroup rows={rows} onPick={vi.fn()} onOpen={vi.fn()} />);

    const toggle = screen.getByRole("button", { name: /Maintaining/ });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(toggle).toHaveTextContent("1 of 3 done today");
    expect(screen.queryByRole("list", { name: "Maintaining" })).toBeNull();

    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(within(screen.getByRole("list", { name: "Maintaining" })).getAllByRole("listitem")).toHaveLength(3);
  });

  it("never reads a day as missed and has no Not followed button", () => {
    render(<MaintainingGroup rows={rows} onPick={vi.fn()} onOpen={vi.fn()} defaultOpen />);

    const list = screen.getByRole("list", { name: "Maintaining" });
    expect(within(list).getByText("Done today")).toBeInTheDocument();
    // An unlogged day and an undone one both read as not logged.
    expect(within(list).getAllByText("Not logged today")).toHaveLength(2);
    expect(within(list).queryByText(/missed/i)).toBeNull();
    expect(within(list).queryByRole("button", { name: /not followed/i })).toBeNull();
    // Partial only where the habit allows it.
    expect(within(list).getByRole("button", { name: "Walk: partly followed today" })).toBeInTheDocument();
    expect(within(list).queryByRole("button", { name: "Floss: partly followed today" })).toBeNull();
  });

  it("checks a day off with one tap, and a second tap on the lit button undoes it", async () => {
    const onPick = vi.fn();
    render(<MaintainingGroup rows={rows} onPick={onPick} onOpen={vi.fn()} defaultOpen />);

    await userEvent.click(screen.getByRole("button", { name: "Walk: followed today" }));
    await userEvent.click(screen.getByRole("button", { name: "Walk: partly followed today" }));
    const floss = screen.getByRole("button", { name: "Floss: followed today" });
    expect(floss).toHaveAttribute("aria-pressed", "true");
    await userEvent.click(floss);

    expect(onPick.mock.calls).toEqual([
      ["walk", "followed"],
      ["walk", "partial"],
      ["floss", "missed"],
    ]);
  });

  it("opens a habit from its name, and shows nothing with no Established habits", async () => {
    const onOpen = vi.fn();
    const { rerender } = render(<MaintainingGroup rows={rows} onPick={vi.fn()} onOpen={onOpen} defaultOpen />);

    await userEvent.click(screen.getByRole("button", { name: "Read Not logged today" }));
    expect(onOpen).toHaveBeenCalledWith("read");

    rerender(<MaintainingGroup rows={[]} onPick={vi.fn()} onOpen={onOpen} />);
    expect(screen.queryByRole("button", { name: /Maintaining/ })).toBeNull();
  });
});

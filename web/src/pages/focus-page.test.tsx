import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import type { DashboardRead } from "@/lib/derive";
import type { HabitOccurrenceRow } from "@/lib/production-ui-types";
import { FocusPage } from "@/pages/focus-page";

const adhere = vi.fn();

function row(name: string, occurrenceDate: string): HabitOccurrenceRow {
  return {
    habitId: name.toLowerCase(),
    habitUrn: `urn:bsk:habit:${name.toLowerCase()}`,
    habitName: name,
    occurrenceDate,
    state: "unrecorded",
    allowsPartial: false,
  };
}

// The rows' day in the browser: 9 pm on 20 October in Pacific time is already the 21st in UTC.
const dashboard: DashboardRead = {
  primaryFocus: { title: "No active goal yet", subjectId: "", kind: "goal" },
  objectives: [],
  dueAndOverdue: [],
  nextActions: [],
  inboxCount: 0,
  todayHabits: [row("Walk", "2026-10-20")],
  maintainingHabits: [row("Floss", "2026-10-20")],
  activeGoals: [],
  activeProjects: [],
};

vi.mock("@/lib/queries", () => ({
  useDashboard: () => ({ data: dashboard, isLoading: false, isError: false }),
  useWrites: () => ({ adhere }),
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="#">{children}</a>,
  useNavigate: () => vi.fn(),
}));

describe("FocusPage habit check-offs", () => {
  it("records each check-off on its row's date, never leaving the day to the server", async () => {
    render(<FocusPage />);

    const walk = screen.getByText("Walk").closest("li")!;
    await userEvent.click(within(walk).getByRole("button", { name: "Followed" }));
    await userEvent.click(screen.getByRole("button", { name: /Maintaining/ }));
    await userEvent.click(screen.getByRole("button", { name: "Floss: followed today" }));

    expect(adhere.mock.calls).toEqual([
      ["walk", "followed", { on: "2026-10-20" }],
      ["floss", "followed", { on: "2026-10-20" }],
    ]);
  });
});

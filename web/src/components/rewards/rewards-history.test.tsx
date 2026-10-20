import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { RewardsHistory } from "@/components/rewards/rewards-history";
import { TooltipProvider } from "@/components/ui/tooltip";
import { addDays, formatDayFull, todayIso } from "@/lib/dates";
import { mockLedgerDaily, resetStore, setStatus } from "@/lib/mock/store";
import { monthLabel, periodLabel, shortPeriodLabel, totalsInWords, viewerTimeZone, weekPeriod } from "@/lib/rewards-history";

beforeEach(() => {
  resetStore();
  localStorage.clear();
});

function renderHistory(onOpenSubject = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <RewardsHistory onOpenSubject={onOpenSubject} />
      </TooltipProvider>
    </QueryClientProvider>,
  );
  return onOpenSubject;
}

/** Today as the mock's own fold totals it, in words — what the calendar cell says. */
function todayInWords(): string {
  const today = todayIso();
  const [day] = mockLedgerDaily({ from: today, to: addDays(today, 1), tz: viewerTimeZone() });
  return totalsInWords(day ?? { earned: {}, reversed: {}, spent: {} });
}

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

describe("Rewards History", () => {
  it("shows what today earned, and picking it lists the entries behind it, each opening its item", async () => {
    // Completed, reopened and completed again: two earns and a reversal today.
    setStatus("task-shoes", "Completed");
    setStatus("task-shoes", "In progress");
    setStatus("task-shoes", "Completed");
    const user = userEvent.setup();
    const onOpen = renderHistory();

    const cell = await screen.findByRole("button", {
      name: `${formatDayFull(todayIso())} (today): ${todayInWords()}`,
    });
    expect(todayInWords()).toContain("gave back 2 Coins, 10 Freedom, 10 XP");
    await user.click(cell);
    expect(cell).toHaveAttribute("aria-pressed", "true");

    const entries = await screen.findByRole("list", { name: `Entries for ${formatDayFull(todayIso())}` });
    await waitFor(() => expect(within(entries).getAllByRole("button", { name: /Buy race shoes/ })).toHaveLength(3));
    expect(within(entries).getByText(/given back · completed undone/)).toBeInTheDocument();

    await user.click(within(screen.getByRole("group", { name: "Show" })).getByRole("button", { name: "Given back" }));
    const given = screen.getByRole("list", { name: `Entries for ${formatDayFull(todayIso())}` });
    expect(within(given).getAllByRole("button", { name: /Buy race shoes/ })).toHaveLength(1);

    await user.click(within(given).getByRole("button", { name: /Buy race shoes/ }));
    expect(onOpen).toHaveBeenCalledWith("task-shoes");

    // Picking the day again goes back to the whole month.
    await user.click(cell);
    expect(screen.getByRole("heading", { name: monthLabel(todayIso()), level: 3 })).toBeInTheDocument();
  });

  it("charts a currency earned vs spent by week, as bars or a table, and a week can be picked", async () => {
    const user = userEvent.setup();
    renderHistory();
    await user.click(within(screen.getByRole("group", { name: "Currency" })).getByRole("button", { name: /Freedom/ }));
    expect(screen.getByRole("heading", { name: "Freedom earned vs spent by week" })).toBeInTheDocument();

    // The seed spent 45 Freedom on an episode four days ago.
    const week = weekPeriod(addDays(todayIso(), -4));
    const bar = await screen.findByRole("button", { name: new RegExp(`^${escape(periodLabel(week))}: .*spent 45$`) });
    await user.click(bar);
    expect(screen.getByRole("heading", { name: periodLabel(week), level: 3 })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Show all of / })).toBeInTheDocument();
    expect(await screen.findByText(/· redeemed — Season finale$/)).toBeInTheDocument();

    await user.click(within(screen.getByRole("group", { name: "Show as" })).getByRole("button", { name: "Table" }));
    const table = screen.getByRole("table", { name: "Freedom earned and spent by week" });
    const row = within(table).getByRole("button", { name: shortPeriodLabel(week) }).closest("tr")!;
    expect(within(row).getAllByRole("cell").map((c) => c.textContent)).toEqual([expect.any(String), "0", "45"]);
  });

  it("charts a tracked currency without a spent series", async () => {
    const user = userEvent.setup();
    renderHistory();
    await user.click(within(screen.getByRole("group", { name: "Chart by" })).getByRole("button", { name: "Months" }));
    expect(screen.getByRole("heading", { name: "XP earned by month" })).toBeInTheDocument();
    expect(screen.getByText("XP is tracked, never spent.")).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Legend" })).not.toBeInTheDocument();
  });
});

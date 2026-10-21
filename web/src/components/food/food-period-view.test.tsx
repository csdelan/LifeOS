import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { FoodPeriodView } from "@/components/food/food-period-view";
import { TooltipProvider } from "@/components/ui/tooltip";
import { addDays, formatDay, formatDayFull, todayIso } from "@/lib/dates";
import { foodPeriod, type FoodPeriod } from "@/lib/food-period";
import { mockFoodDays, mockSettleFood, resetStore } from "@/lib/mock/store";

beforeEach(() => {
  resetStore();
  localStorage.clear();
});

function renderView(period: FoodPeriod, onOpenDay = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <FoodPeriodView period={period} today={todayIso()} onOpenDay={onOpenDay} />
      </TooltipProvider>
    </QueryClientProvider>,
  );
  return onOpenDay;
}

describe("Food Log Week and Month views", () => {
  // The seed logs food from six days back to two days back; the page settles it on open.
  const today = todayIso();
  const lastSeven: FoodPeriod = { view: "week", from: addDays(today, -6), to: today };

  it("totals what the days paid and labels each day with its standing", async () => {
    mockSettleFood();
    const paid = mockFoodDays(lastSeven.from, lastSeven.to).days.reduce((s, d) => s + d.paid.total, 0);
    renderView(lastSeven);

    expect(await screen.findByText(`${paid.toLocaleString()} Health XP`)).toBeInTheDocument();
    expect(screen.getByText("6 of 7")).toBeInTheDocument();

    // Two days back: the 23-hour fast and a salmon salad, 39 base at ×1.2.
    expect(
      screen.getByRole("button", { name: new RegExp(`^${escape(formatDayFull(addDays(today, -2)))}: 46 Health XP paid \\(39 base\\)`) }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: `${formatDayFull(today)} (today): on track for 0 of 40 · nothing logged` }),
    ).toBeInTheDocument();
  });

  it("opens a day when it's picked, from the chart or the table", async () => {
    const user = userEvent.setup();
    const onOpenDay = renderView(lastSeven);
    const day = addDays(today, -3);

    await user.click(await screen.findByRole("button", { name: new RegExp(`^${escape(formatDayFull(day))}:`) }));
    expect(onOpenDay).toHaveBeenLastCalledWith(day);

    await user.click(screen.getByRole("button", { name: "Table" }));
    const table = screen.getByRole("table", { name: "Diet XP each day, oldest first" });
    expect(within(table).getAllByRole("row")).toHaveLength(8);
    await user.click(within(table).getByRole("button", { name: formatDay(addDays(today, -4)) }));
    expect(onOpenDay).toHaveBeenLastCalledWith(addDays(today, -4));
  });

  it("shows the month as a calendar, every day ahead disabled", async () => {
    renderView(foodPeriod("month", today));
    const calendar = await screen.findByRole("group", { name: "Diet XP each day" });
    const days = within(calendar).getAllByRole("button");
    const [y, m] = today.split("-").map(Number);
    expect(days).toHaveLength(new Date(y, m, 0).getDate());
    expect(days.filter((b) => (b as HTMLButtonElement).disabled)).toHaveLength(days.length - Number(today.slice(8)));
  });
});

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

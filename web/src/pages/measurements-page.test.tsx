import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { toast } from "sonner";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { addDays, todayIso } from "@/lib/dates";
import { goalText, withUnit } from "@/lib/measurements";
import { clearMockMeasurements, mockMetricDetail } from "@/lib/mock/measurements-store";
import { resetStore } from "@/lib/mock/store";
import { MeasurementsPage } from "@/pages/measurements-page";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 10, 7, 12, 0, 0));
  resetStore();
  localStorage.clear();
  vi.clearAllMocks();
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <MeasurementsPage />
      </TooltipProvider>
    </QueryClientProvider>,
  );
}

const LB = { unit: "lb", decimals: 1 };

/** The header's Log weight; jsdom applies no CSS, so the phone bar's is there too. */
function logButton() {
  return screen.getAllByRole("button", { name: "Log weight" })[0];
}

async function openSheet(user: ReturnType<typeof userEvent.setup>) {
  await user.click(logButton());
  return screen.findByRole("dialog", { name: "Log weight" });
}

describe("Measurements page: the Weight card (MEASURE-1, MEASURE-2, measurements plan §4.7)", () => {
  it("shows the trend, the last weigh-in, the goal date in words and a dot for each day", async () => {
    renderPage();
    const all = mockMetricDetail("weight");

    expect(await screen.findByTestId("weight-trend")).toHaveTextContent(withUnit(all.days.at(-1)!.trend, LB));
    expect(screen.getByTestId("weight-last")).toHaveTextContent(/^Last weigh-in \d+\.\d lb · Yesterday, \d+:\d\d [AP]M$/);
    const goal = await screen.findByTestId("weight-goal");
    expect(goal).toHaveTextContent(goalText(all.goal, all.metric, todayIso(), "weigh-in")!);
    expect(goal).toHaveTextContent(/^185\.0 lb by .+ at [\d.]+ lb a week, from \d+ weigh-ins\.$/);

    // 90 days by default, which holds all six seeded weeks: a dot a day, the trend, the goal.
    const chart = await screen.findByRole("group", { name: "Weight by day, last 90 days. Use the arrow keys to read a day." });
    expect(within(chart).getAllByTestId("trend-dot")).toHaveLength(all.days.length);
    expect(within(chart).getAllByTestId("trend-line").length).toBeGreaterThan(0);
    expect(within(chart).getByTestId("trend-target")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Legend" })).toHaveTextContent("Goal 185.0 lb");
  });

  it("reads a day in words from the arrow keys", async () => {
    renderPage();
    const all = mockMetricDetail("weight");
    const chart = await screen.findByRole("group", { name: /^Weight by day/ });
    await waitFor(() => expect(within(chart).getAllByTestId("trend-dot").length).toBeGreaterThan(0));

    fireEvent.focus(chart);
    fireEvent.keyDown(chart, { key: "End" });
    const last = all.days.at(-1)!;
    const readout = `Yesterday: ${withUnit(last.value, LB)}, trend ${withUnit(last.trend, LB)}`;
    expect(screen.getByText(readout)).toBeInTheDocument();

    fireEvent.keyDown(chart, { key: "ArrowLeft" });
    expect(screen.queryByText(readout)).not.toBeInTheDocument();
  });

  it("narrows to 30 days and remembers it", async () => {
    const user = userEvent.setup();
    const view = renderPage();
    await screen.findByRole("group", { name: /^Weight by day, last 90 days/ });

    await user.click(screen.getByRole("button", { name: "30 days" }));
    const month = mockMetricDetail("weight", 30);
    const chart = await screen.findByRole("group", { name: /^Weight by day, last 30 days/ });
    await waitFor(() => expect(within(chart).getAllByTestId("trend-dot")).toHaveLength(month.days.length));
    expect(screen.getByRole("button", { name: "30 days" })).toHaveAttribute("aria-pressed", "true");

    view.unmount();
    renderPage();
    expect(await screen.findByRole("group", { name: /^Weight by day, last 30 days/ })).toBeInTheDocument();
  });

  it("changes the goal, and the dashed line and the date follow it", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Change goal" }));
    const dialog = await screen.findByRole("dialog", { name: "Weight goal" });
    const input = within(dialog).getByLabelText("Goal weight (lb)");
    expect(input).toHaveValue("185.0");

    await user.clear(input);
    await user.type(input, "180");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.getByTestId("weight-goal")).toHaveTextContent(/^180\.0 lb by /));
    expect(screen.getByRole("list", { name: "Legend" })).toHaveTextContent("Goal 180.0 lb");
  });
});

describe("Measurements page: logging (MEASURE-1)", () => {
  it("logs a weigh-in now, and it's the last weigh-in and today's dot", async () => {
    const user = userEvent.setup();
    renderPage();
    const before = mockMetricDetail("weight").days.length;
    await screen.findByTestId("weight-trend");

    const sheet = await openSheet(user);
    const value = within(sheet).getByLabelText("Weight (lb)");
    await user.type(value, "192.45");
    expect(within(sheet).getByText("Weight takes at most 1 decimal place; got 192.45.")).toBeInTheDocument();
    expect(within(sheet).getByRole("button", { name: "Log" })).toBeDisabled();

    await user.clear(value);
    await user.type(value, "192.4");
    await user.click(within(sheet).getByRole("button", { name: "Log" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Logged 192.4 lb"));
    await waitFor(() => expect(screen.getByTestId("weight-last")).toHaveTextContent(/^Last weigh-in 192\.4 lb · Today, 12:00 PM$/));
    expect(screen.queryByRole("dialog", { name: "Log weight" })).not.toBeInTheDocument();
    const chart = screen.getByRole("group", { name: /^Weight by day/ });
    await waitFor(() => expect(within(chart).getAllByTestId("trend-dot")).toHaveLength(before + 1));
  });

  it("backdates a weigh-in, but not into the future", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByTestId("weight-trend");

    const sheet = await openSheet(user);
    await user.type(within(sheet).getByLabelText("Weight (lb)"), "194");
    const time = within(sheet).getByLabelText("Taken at");
    fireEvent.change(time, { target: { value: `${addDays(todayIso(), 1)}T07:00` } });
    expect(within(sheet).getByText("A reading is taken now or earlier, not in the future.")).toBeInTheDocument();
    expect(within(sheet).getByRole("button", { name: "Log" })).toBeDisabled();

    // Three days back has a morning weigh-in, so this one is that day's second.
    fireEvent.change(time, { target: { value: `${addDays(todayIso(), -3)}T22:30` } });
    await user.type(within(sheet).getByLabelText("Note (optional)"), "late snack");
    await user.click(within(sheet).getByRole("button", { name: "Log" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Logged 194.0 lb"));

    const logged = mockMetricDetail("weight").readings.find((r) => r.note === "late snack")!;
    expect(logged).toMatchObject({ value: 194, takenOn: addDays(todayIso(), -3), takenLocal: `${addDays(todayIso(), -3)}T22:30:00` });
  });
});

describe("Measurements page: the table (MEASURE-2)", () => {
  it("lists every weigh-in newest first, marks one that isn't the day's first, and changes one", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByTestId("weight-trend");
    await user.click(screen.getByRole("button", { name: "Table" }));

    const table = await screen.findByRole("table", { name: "Weight weigh-ins, last 90 days, newest first" });
    const all = mockMetricDetail("weight");
    const rows = within(table).getAllByTestId("metric-row");
    expect(rows).toHaveLength(all.readings.length);
    expect(rows[0]).toHaveTextContent(/^Yesterday, /);
    const evening = rows.find((r) => r.textContent!.includes("after dinner"))!;
    expect(evening).toHaveTextContent("Not the day's first");
    expect(evening).toHaveTextContent("—");

    const newest = all.readings[0];
    await user.click(within(rows[0]).getByRole("button", { name: /^Change / }));
    const sheet = await screen.findByRole("dialog", { name: "Change weigh-in" });
    const value = within(sheet).getByLabelText("Weight (lb)");
    expect(value).toHaveValue(newest.value.toFixed(1));
    await user.clear(value);
    await user.type(value, "190.2");
    await user.click(within(sheet).getByRole("button", { name: "Save" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Saved 190.2 lb"));
    const changed = mockMetricDetail("weight").readings[0];
    expect(changed).toMatchObject({ id: newest.id, value: 190.2, versions: 2, takenAt: newest.takenAt });
    await waitFor(() => expect(within(table).getAllByTestId("metric-row")[0]).toHaveTextContent("190.2 lb"));
  });

  it("deletes a weigh-in after asking", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByTestId("weight-trend");
    await user.click(screen.getByRole("button", { name: "Table" }));
    const table = await screen.findByRole("table");
    const before = within(table).getAllByTestId("metric-row").length;
    const newest = mockMetricDetail("weight").readings[0];

    await user.click(within(table).getAllByRole("button", { name: /^Delete / })[0]);
    const ask = await screen.findByRole("alertdialog", { name: "Delete this weigh-in?" });
    expect(ask).toHaveTextContent(withUnit(newest.value, LB));
    await user.click(within(ask).getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Deleted the reading"));
    await waitFor(() => expect(within(table).getAllByTestId("metric-row")).toHaveLength(before - 1));
    expect(mockMetricDetail("weight").readings.some((r) => r.id === newest.id)).toBe(false);
  });
});

describe("Measurements page: before there's a Weight (MEASURE-1)", () => {
  it("starts tracking weight with a goal, then asks for the first weigh-in", async () => {
    clearMockMeasurements();
    const user = userEvent.setup();
    renderPage();

    expect(await screen.findByRole("heading", { name: "Track your weight" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Log weight" })).not.toBeInTheDocument();
    const goal = screen.getByLabelText("Goal weight (lb, optional)");
    await user.type(goal, "18.55");
    expect(screen.getByText("The goal takes at most 1 decimal place; got 18.55.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Start tracking weight" })).toBeDisabled();
    await user.clear(goal);
    await user.type(goal, "185");
    await user.click(screen.getByRole("button", { name: "Start tracking weight" }));

    expect(await screen.findByRole("heading", { name: "Weight" })).toBeInTheDocument();
    expect(screen.getByTestId("weight-last")).toHaveTextContent("No weigh-ins yet: log your first to start the trend.");
    expect(await screen.findByText("Nothing logged yet.")).toBeInTheDocument();
    expect(screen.queryByTestId("weight-goal")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Change goal" })).toBeInTheDocument();

    const sheet = await openSheet(user);
    await user.type(within(sheet).getByLabelText("Weight (lb)"), "199.8");
    await user.click(within(sheet).getByRole("button", { name: "Log" }));
    await waitFor(() => expect(screen.getByTestId("weight-goal")).toHaveTextContent("Log another day to see when you'll reach 185.0 lb."));
    expect(screen.getByTestId("weight-trend")).toHaveTextContent("199.8 lb");
  });
});

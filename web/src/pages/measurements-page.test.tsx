import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { toast } from "sonner";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { addDays, todayIso } from "@/lib/dates";
import { goalText, withUnit } from "@/lib/features/measurements/measurements";
import {
  clearMockMeasurements,
  mockLabVisits,
  mockMetricDetail,
  mockMetrics,
} from "@/lib/mock/measurements-store";
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
    await user.click(await screen.findByRole("button", { name: "Edit Weight" }));
    const dialog = await screen.findByRole("dialog", { name: "Edit weight" });
    // The unit stays once there are weigh-ins.
    expect(within(dialog).getByLabelText("Unit")).toBeDisabled();
    const input = within(dialog).getByLabelText("Goal (lb), optional");
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
    expect(screen.getByRole("button", { name: "Edit Weight" })).toBeInTheDocument();

    const sheet = await openSheet(user);
    await user.type(within(sheet).getByLabelText("Weight (lb)"), "199.8");
    await user.click(within(sheet).getByRole("button", { name: "Log" }));
    await waitFor(() => expect(screen.getByTestId("weight-goal")).toHaveTextContent("Log another day to see when you'll reach 185.0 lb."));
    expect(screen.getByTestId("weight-trend")).toHaveTextContent("199.8 lb");
  });
});

/** A row of Your metrics by its name, and its disclosure button. */
function row(name: string) {
  const list = screen.getByRole("list", { name: "Your metrics" });
  const item = within(list)
    .getAllByTestId("metric-list-row")
    .find((li) => within(li).queryByRole("heading", { name: new RegExp(`^${name}`) }))!;
  return { item, toggle: within(item).getAllByRole("button").find((b) => b.hasAttribute("aria-expanded"))! };
}

describe("Measurements page: any metric (MEASURE-3, measurements plan §4.8)", () => {
  it("lists the other metrics as rows in starter-list order, blood pressure as one, with its newest pair flagged", async () => {
    renderPage();
    const list = await screen.findByRole("list", { name: "Your metrics" });
    const rows = within(list).getAllByTestId("metric-list-row");
    // Weight has its own card at the top; Waist comes before blood pressure on the starter list.
    expect(rows.map((r) => within(r).getByRole("heading").textContent)).toEqual([
      expect.stringMatching(/^Waist/),
      expect.stringMatching(/^Blood pressure/),
    ]);
    const bp = row("Blood pressure").item;
    expect(within(bp).getByTestId("metric-row-value")).toHaveTextContent("121/78 mmHg");
    expect(within(bp).getByTestId("metric-row-flag")).toHaveTextContent("High");
    expect(within(bp).getByTestId("metric-row-when")).toHaveTextContent(/^Thu, Nov 5, 7:\d\d [AP]M$/);
    expect(within(row("Waist").item).queryByTestId("metric-row-flag")).not.toBeInTheDocument();
    await waitFor(() => expect(within(bp).getByTestId("metric-sparkline")).toBeInTheDocument());
  });

  it("opens blood pressure's card in place: one chart with both lines and their ranges, and a table of pairs", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("list", { name: "Your metrics" });
    const { item, toggle } = row("Blood pressure");
    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");

    const card = await within(item).findByRole("region", { name: "Blood pressure" });
    expect(within(card).getByTestId("bp-range")).toHaveTextContent("Normal: systolic 90–119, diastolic 60–79 mmHg");
    const chart = await within(card).findByRole("group", { name: /^Blood pressure by day, last 90 days/ });
    const systolic = mockMetricDetail("Systolic BP");
    await waitFor(() => expect(within(chart).getAllByTestId("trend-dot")).toHaveLength(systolic.days.length * 2));
    expect(within(chart).getAllByTestId("trend-band")).toHaveLength(2);
    expect(within(chart).getAllByTestId("trend-dot").filter((d) => d.dataset.flagged).length).toBeGreaterThan(1);
    const legend = within(card).getByRole("list", { name: "Legend" });
    expect(legend).toHaveTextContent("Systolic");
    expect(legend).toHaveTextContent("Diastolic");
    expect(legend).toHaveTextContent("Outside the range");

    fireEvent.focus(chart);
    expect(within(card).getByText(/^Thu, Nov 5: 121\/78 mmHg \(high\), trend \d+\/\d+ mmHg$/)).toBeInTheDocument();

    await user.click(within(card).getByRole("button", { name: "Table" }));
    const table = await within(card).findByRole("table", { name: "Blood pressure readings, last 90 days, newest first" });
    const rows = within(table).getAllByTestId("metric-row");
    expect(rows).toHaveLength(systolic.readings.length);
    expect(rows[0]).toHaveTextContent("121/78 mmHg");
    expect(rows[0]).toHaveTextContent("High");
    expect(rows.at(-1)).toHaveTextContent("after coffee");
  });

  it("logs blood pressure from its row, both values in one write", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("list", { name: "Your metrics" });
    await user.click(within(row("Blood pressure").item).getByRole("button", { name: "Log blood pressure" }));
    const sheet = await screen.findByRole("dialog", { name: "Log blood pressure" });
    expect(within(sheet).getByText("Type the systolic BP, e.g. 118.")).toBeInTheDocument();
    await user.type(within(sheet).getByLabelText("Systolic (mmHg)"), "118");
    expect(within(sheet).getByText("Type the diastolic BP, e.g. 76.")).toBeInTheDocument();
    await user.type(within(sheet).getByLabelText("Diastolic (mmHg)"), "82.5");
    expect(within(sheet).getByText("Diastolic BP takes a whole number; got 82.5.")).toBeInTheDocument();
    await user.clear(within(sheet).getByLabelText("Diastolic (mmHg)"));
    await user.type(within(sheet).getByLabelText("Diastolic (mmHg)"), "82");
    await user.click(within(sheet).getByRole("button", { name: "Log" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Logged 118/82 mmHg"));
    const bp = row("Blood pressure").item;
    await waitFor(() => expect(within(bp).getByTestId("metric-row-value")).toHaveTextContent("118/82 mmHg"));
    // Diastolic 82 is over 79.
    expect(within(bp).getByTestId("metric-row-flag")).toHaveTextContent("High");
    const [sys] = mockMetricDetail("Systolic BP").readings;
    const [dia] = mockMetricDetail("Diastolic BP").readings;
    expect([sys.value, dia.value, sys.takenAt === dia.takenAt]).toEqual([118, 82, true]);
  });

  it("changes a pair from its card's table, both halves at once", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("list", { name: "Your metrics" });
    const { item, toggle } = row("Blood pressure");
    await user.click(toggle);
    await user.click(await within(item).findByRole("button", { name: "Table" }));
    const table = await within(item).findByRole("table");
    await user.click(within(within(table).getAllByTestId("metric-row")[0]).getByRole("button", { name: /^Change 121\/78 mmHg/ }));

    const sheet = await screen.findByRole("dialog", { name: "Change reading" });
    expect(within(sheet).getByLabelText("Systolic (mmHg)")).toHaveValue("121");
    await user.clear(within(sheet).getByLabelText("Systolic (mmHg)"));
    await user.type(within(sheet).getByLabelText("Systolic (mmHg)"), "117");
    await user.click(within(sheet).getByRole("button", { name: "Save" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Saved 117/78 mmHg"));
    await waitFor(() => expect(within(row("Blood pressure").item).queryByTestId("metric-row-flag")).not.toBeInTheDocument());
    expect(mockMetricDetail("Systolic BP").readings[0]).toMatchObject({ value: 117, versions: 2, flag: null });
  });

  it("adds a starter metric and one of his own, and opens it", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Add a metric" }));
    let dialog = await screen.findByRole("dialog", { name: "Add a metric" });
    // Waist and blood pressure are tracked; Weight has its own card.
    expect(within(dialog).getAllByRole("button", { name: /^Add / }).map((b) => b.firstChild?.textContent)).toEqual([
      "Add Body fat",
      "Add VO2 max (lab)",
    ]);
    expect(within(dialog).getByRole("button", { name: "Add" })).toBeDisabled();
    await user.click(within(dialog).getByRole("button", { name: /^Add Body fat/ }));
    // The dialog hides the page from getByRole until the add is written and it closes, which on
    // a busy CI runner took longer than waitFor's default second.
    await waitFor(() => expect(row("Body fat").item).toBeInTheDocument(), { timeout: 4000 });

    await user.click(screen.getByRole("button", { name: "Add a metric" }));
    dialog = await screen.findByRole("dialog", { name: "Add a metric" });
    await user.type(within(dialog).getByLabelText("Name"), "Resting heart rate");
    expect(within(dialog).getByText("Give the metric a unit, e.g. lb, in, % or mg/dL.")).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText("Unit"), "bpm");
    await user.click(within(within(dialog).getByRole("group", { name: "Decimal places" })).getByRole("button", { name: "0" }));
    await user.type(within(dialog).getByLabelText("Low end"), "70");
    await user.type(within(dialog).getByLabelText("High end"), "50");
    expect(within(dialog).getByText("The normal range's low end (70) is above its high end (50).")).toBeInTheDocument();
    await user.clear(within(dialog).getByLabelText("Low end"));
    await user.type(within(dialog).getByLabelText("Low end"), "40");
    await user.click(within(dialog).getByRole("button", { name: "Add Resting heart rate" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Add a metric" })).not.toBeInTheDocument(), {
      timeout: 4000,
    });
    const added = mockMetrics().find((m) => m.name === "Resting heart rate")!;
    expect(added).toMatchObject({ unit: "bpm", decimals: 0, low: 40, high: 50, catalog: null });
    // His own come after the starter list's; the new one is open, with nothing logged yet.
    const list = screen.getByRole("list", { name: "Your metrics" });
    expect(within(list).getAllByRole("heading").map((h) => h.textContent)).toEqual([
      expect.stringMatching(/^Body fat/),
      expect.stringMatching(/^Waist/),
      expect.stringMatching(/^Blood pressure/),
      expect.stringMatching(/^Resting heart rate/),
    ]);
    const card = await within(list).findByRole("region", { name: "Resting heart rate" });
    expect(await within(card).findByText("Nothing logged yet.")).toBeInTheDocument();
  });

  it("gives Waist a normal range, which flags its readings, then archives and restores it", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("list", { name: "Your metrics" });
    const { item, toggle } = row("Waist");
    await user.click(toggle);
    const card = await within(item).findByRole("region", { name: "Waist" });
    expect(within(card).queryByTestId("waist-range")).not.toBeInTheDocument();

    await user.click(within(card).getByRole("button", { name: "Edit Waist" }));
    let dialog = await screen.findByRole("dialog", { name: "Edit waist" });
    await user.type(within(dialog).getByLabelText("High end"), "38");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(within(card).getByTestId("waist-range")).toHaveTextContent("Normal: up to 38.0 in"));
    // 38.6, 38.4 and 38.3 are over it.
    await waitFor(() =>
      expect(within(card).getAllByTestId("trend-dot").filter((d) => d.dataset.flagged)).toHaveLength(3),
    );
    expect(within(row("Waist").item).queryByTestId("metric-row-flag")).not.toBeInTheDocument();

    await user.click(within(card).getByRole("button", { name: "Edit Waist" }));
    dialog = await screen.findByRole("dialog", { name: "Edit waist" });
    await user.click(within(dialog).getByRole("button", { name: "Archive waist" }));
    const archived = await screen.findByRole("list", { name: "Archived metrics" });
    expect(archived).toHaveTextContent("Waist");
    expect(archived).toHaveTextContent("6 readings");
    expect(within(screen.getByRole("list", { name: "Your metrics" })).queryByRole("heading", { name: /^Waist/ })).not.toBeInTheDocument();

    await user.click(within(archived).getByRole("button", { name: "Restore Waist" }));
    await waitFor(() => expect(screen.queryByRole("list", { name: "Archived metrics" })).not.toBeInTheDocument());
    expect(row("Waist").item).toBeInTheDocument();
  });
});

/** A sheet's result box for a test: its label is the test's name over its unit. */
function resultBox(sheet: HTMLElement, name: string, unit: string) {
  return within(sheet).getByRole("textbox", { name: new RegExp(`^${name}\\s*${unit.replace("/", "\\/")}$`) });
}

describe("Measurements page: lab results (MEASURE-4, measurements plan §4.9)", () => {
  it("lists the visits, then the tests by panel, under Your metrics, which leaves the tests out", async () => {
    const user = userEvent.setup();
    renderPage();
    const yours = await screen.findByRole("list", { name: "Your metrics" });
    expect(within(yours).queryByRole("heading", { name: /^Glucose/ })).not.toBeInTheDocument();

    const section = await screen.findByRole("region", { name: "Lab results" });
    const visits = await within(section).findByRole("list", { name: "Visits" });
    const toggle = within(visits).getByRole("button", {
      name: /^Jul \d+, 2026 · Quest Diagnostics: 21 results, 3 outside the range, with its report$/,
    });
    expect(within(section).getByRole("list", { name: "CMP" })).toHaveTextContent("Glucose");
    expect(within(within(section).getByRole("list", { name: "Lipid panel" })).getAllByTestId("metric-list-row")).toHaveLength(5);
    expect(within(section).getByRole("list", { name: "A1c" })).toHaveTextContent("5.4 %");
    // A test's results come with a visit, so its row has no Log of its own.
    expect(within(section).queryByRole("button", { name: /^Log glucose$/i })).not.toBeInTheDocument();

    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    const table = within(section).getByRole("table", { name: /^Results from Jul \d+, 2026 · Quest Diagnostics$/ });
    const flagged = within(table)
      .getAllByRole("row")
      .filter((r) => r.textContent!.endsWith("High"));
    expect(flagged.map((r) => within(r).getByRole("rowheader").textContent)).toEqual(["Glucose", "LDL", "Non-HDL cholesterol"]);
    // The range never breaks at its dash, so word joiners sit either side of it.
    const glucose = within(table).getByRole("rowheader", { name: "Glucose" }).closest("tr")!;
    expect(glucose.textContent!.replace(/⁠/g, "")).toBe("Glucose104 mg/dL65–99High");
    expect(within(section).getByRole("button", { name: "Open the report, quest-results.pdf" })).toBeInTheDocument();
  });

  it("logs a visit with the last report's ranges filled in and a test of his own, and flags what's outside", async () => {
    const user = userEvent.setup();
    renderPage();
    const section = await screen.findByRole("region", { name: "Lab results" });
    await within(section).findByRole("list", { name: "Visits" });
    await user.click(within(section).getByRole("button", { name: "Log a lab visit" }));

    const sheet = await screen.findByRole("dialog", { name: "Log a lab visit" });
    const panels = within(sheet).getByRole("group", { name: "Panels" });
    expect(within(panels).getAllByRole("button").map((b) => [b.textContent, b.getAttribute("aria-pressed")])).toEqual([
      ["CMP", "true"],
      ["Lipid panel", "true"],
      ["A1c", "true"],
    ]);
    // The last report's range, not the starter list's 70–100.
    expect(within(sheet).getByLabelText("Glucose: the range's low end")).toHaveValue("65");
    expect(within(sheet).getByLabelText("Glucose: the range's high end")).toHaveValue("99");
    await user.click(within(panels).getByRole("button", { name: "Lipid panel" }));
    await user.click(within(panels).getByRole("button", { name: "A1c" }));
    expect(within(sheet).queryByLabelText("LDL: the range's high end")).not.toBeInTheDocument();

    await user.type(within(sheet).getByLabelText("Lab (optional)"), "LabCorp");
    await user.type(resultBox(sheet, "Glucose", "mg/dL"), "101");
    await user.click(within(sheet).getByRole("button", { name: "Add a test" }));
    await user.type(within(sheet).getByLabelText("Test"), "TSH");
    expect(within(sheet).getByText("Give TSH's unit, as the report prints it (e.g. mIU/L).")).toBeInTheDocument();
    expect(within(sheet).getByRole("button", { name: "Save visit" })).toBeDisabled();
    await user.type(within(sheet).getByLabelText("Unit"), "mIU/L");
    await user.type(within(sheet).getByLabelText("Result"), "2.15");
    await user.type(within(sheet).getByLabelText("Low"), "0.45");
    await user.type(within(sheet).getByLabelText("High"), "4.5");
    await user.click(within(sheet).getByRole("button", { name: "Save visit" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Saved 2 results from Nov 7"));
    const visits = within(section).getByRole("list", { name: "Visits" });
    await waitFor(() => expect(within(visits).getAllByTestId("lab-visit")).toHaveLength(2));
    // Glucose 101 is over the 99 printed beside it.
    expect(within(visits).getAllByTestId("lab-visit-summary").map((s) => s.textContent)).toEqual([
      "2 results, 1 outside the range",
      "21 results, 3 outside the range",
    ]);
    expect(within(visits).getAllByRole("button", { name: /^Nov 7, 2026 · LabCorp:/ })).toHaveLength(1);
    expect(await within(section).findByRole("list", { name: "Other tests" })).toHaveTextContent("TSH");
    expect(mockMetrics().find((m) => m.name === "TSH")).toMatchObject({ panel: "other", unit: "mIU/L", low: 0.45, high: 4.5 });
  });

  it("opens a result's visit from its card, saves a change, and deletes the visit after asking", async () => {
    const user = userEvent.setup();
    renderPage();
    const section = await screen.findByRole("region", { name: "Lab results" });
    const cmp = await within(section).findByRole("list", { name: "CMP" });
    const item = within(cmp)
      .getAllByTestId("metric-list-row")
      .find((li) => within(li).queryByRole("heading", { name: /^Glucose/ }))!;
    await user.click(within(item).getAllByRole("button").find((b) => b.hasAttribute("aria-expanded"))!);

    // Its card leads with the latest result, and opens on all of it: labs come months apart.
    const card = await within(item).findByRole("region", { name: "Glucose" });
    await waitFor(() => expect(card).toHaveTextContent(/Latest 104 mg\/dL.*\(high\)/));
    expect(card).toHaveTextContent("Newest report's range: 65–99 mg/dL");
    await user.click(within(card).getByRole("button", { name: "Table" }));
    const table = await within(card).findByRole("table");
    const [result] = within(table).getAllByTestId("metric-row");
    // The range the report printed beside it, its unit left to the result.
    expect(result).toHaveTextContent(/104 mg\/dL.*High.*65–99$/);
    // A result goes with its visit, never on its own.
    expect(within(result).queryByRole("button", { name: /^Delete / })).not.toBeInTheDocument();
    await user.click(within(result).getByRole("button", { name: /in its lab visit$/ }));

    const sheet = await screen.findByRole("dialog", { name: "Edit lab visit" });
    const value = resultBox(sheet, "Glucose", "mg/dL");
    expect(value).toHaveValue("104");
    await user.clear(value);
    await user.type(value, "98");
    await user.click(within(sheet).getByRole("button", { name: "Save visit" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(expect.stringMatching(/^Saved 21 results from Jul \d+$/)));
    await waitFor(() =>
      expect(within(section).getByTestId("lab-visit-summary")).toHaveTextContent("21 results, 2 outside the range"),
    );

    await user.click(within(section).getByRole("button", { name: /^Jul \d+, 2026 · Quest Diagnostics/ }));
    await user.click(within(section).getByRole("button", { name: "Edit visit" }));
    const again = await screen.findByRole("dialog", { name: "Edit lab visit" });
    await user.click(within(again).getByRole("button", { name: "Delete visit" }));
    const ask = await screen.findByRole("alertdialog", { name: "Delete this lab visit?" });
    expect(ask).toHaveTextContent("21 results, 2 outside the range");
    await user.click(within(ask).getByRole("button", { name: "Delete visit" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Deleted the lab visit"));
    await waitFor(() => expect(within(section).queryByRole("list", { name: "Visits" })).not.toBeInTheDocument());
    expect(section).toHaveTextContent("Log a lab visit when you have blood drawn");
    expect(mockLabVisits().visits).toHaveLength(0);
  });
});

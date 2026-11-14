import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DailyMetricChart, MetricTable, type MetricBar } from "@/components/charts/daily-metric-chart";

const bars: MetricBar[] = [
  { key: "a", value: 4_000, tone: "bg-steps-under", title: "Mon, Oct 19", text: "4,000 steps", axis: "Oct 19" },
  { key: "b", value: null, tone: "", title: "Tue, Oct 20", text: "no steps recorded", axis: "Oct 20" },
  { key: "c", value: 8_000, tone: "bg-steps-goal", title: "Wed, Oct 21", text: "8,000 steps", detail: "7-day average 6,000", axis: "Oct 21" },
  { key: "d", value: 3_000, tone: "bg-steps-under", partial: true, title: "Today", text: "3,000 steps so far", axis: "Today" },
];

describe("DailyMetricChart", () => {
  it("draws a bar for each day with a value, hatching the one still going", () => {
    render(<DailyMetricChart label="Steps by day" bars={bars} legend={[]} />);
    const marks = screen.getAllByTestId("metric-bar");
    expect(marks).toHaveLength(3);
    expect(marks.map((m) => m.hasAttribute("data-partial"))).toEqual([false, false, true]);
    expect(marks[1]).toHaveClass("bg-steps-goal");
  });

  it("breaks the average line where there's no average", () => {
    render(<DailyMetricChart label="Steps by day" bars={bars} average={[5_000, 5_500, null, 6_000]} legend={[]} />);
    // Only 5,000 → 5,500 is an unbroken run of two points; 6,000 stands alone.
    expect(screen.getAllByTestId("metric-average")).toHaveLength(1);
  });

  it("labels each threshold on the axis and lists the legend", () => {
    render(
      <DailyMetricChart
        label="Steps by day"
        bars={bars}
        references={[
          { value: 5_000, label: "Minimum", pattern: "dotted" },
          { value: 7_500, label: "Goal", pattern: "dashed" },
        ]}
        legend={[
          { kind: "bar", tone: "bg-steps-goal", label: "At the goal" },
          { kind: "dashed", label: "7,500 goal" },
        ]}
      />,
    );
    expect(screen.getByText("5,000")).toBeInTheDocument();
    expect(screen.getByText("7,500")).toBeInTheDocument();
    expect(within(screen.getByRole("list", { name: "Legend" })).getAllByRole("listitem")).toHaveLength(2);
  });

  it("takes a fixed top and says the axis in the chart's own unit", () => {
    render(
      <DailyMetricChart
        label="Time asleep by day"
        bars={[{ key: "a", value: 7.5, tone: "bg-sleep-time", title: "Mon", text: "7 h 30 min asleep", axis: "Mon" }]}
        top={10}
        formatValue={(n) => `${n} h`}
        references={[{ value: 8, label: "Eight", pattern: "dashed" }]}
        legend={[]}
      />,
    );
    expect(screen.getByText("10 h")).toBeInTheDocument();
    expect(screen.getByText("8 h")).toBeInTheDocument();
    expect(screen.getByTestId("metric-bar").style.height).toBe("max(2px, 75%)");
  });

  it("reads bars with the arrow keys and opens one with Enter", async () => {
    const onSelect = vi.fn();
    render(<DailyMetricChart label="Steps by day" bars={bars} legend={[]} onSelect={onSelect} />);
    const chart = screen.getByRole("group", { name: "Steps by day. Use the arrow keys to read a bar, and Enter to open it." });
    act(() => chart.focus());
    const readout = document.getElementById(chart.getAttribute("aria-describedby")!)!;
    expect(readout).toHaveTextContent("Today: 3,000 steps so far");
    await userEvent.keyboard("{ArrowLeft}");
    expect(readout).toHaveTextContent("Wed, Oct 21: 8,000 steps; 7-day average 6,000");
    await userEvent.keyboard("{ArrowLeft}");
    expect(readout).toHaveTextContent("Tue, Oct 20: no steps recorded");
    await userEvent.keyboard("{Enter}");
    expect(onSelect).toHaveBeenCalledWith(1);
    await userEvent.keyboard("{Home}");
    expect(readout).toHaveTextContent(/^Mon, Oct 19/);
  });
});

describe("MetricTable", () => {
  it("makes the first cell of each row its header", () => {
    render(
      <MetricTable
        label="Steps, as a table"
        caption="Steps by day"
        columns={[
          { key: "day", label: "Day" },
          { key: "steps", label: "Steps", numeric: true },
        ]}
        rows={[{ key: "a", cells: ["Today", "3,000"] }]}
      />,
    );
    const table = within(screen.getByRole("region", { name: "Steps, as a table" })).getByRole("table", { name: "Steps by day" });
    expect(within(table).getByRole("rowheader", { name: "Today" })).toBeInTheDocument();
    expect(within(table).getByRole("cell", { name: "3,000" })).toHaveClass("text-right");
  });
});

describe("a stacked part (code review CR86)", () => {
  it("sits on top of its bar in its own tone, and the axis makes room for both", () => {
    const stacked: MetricBar[] = [
      { key: "a", value: 90, tone: "bg-chart-earned", stack: { value: 30, tone: "bg-chart-3/55" }, title: "Oct 20", text: "90 action XP + 30 sleep", axis: "Oct 20" },
      { key: "b", value: 40, tone: "bg-chart-earned/35", title: "Today", text: "40 action XP", axis: "Today" },
    ];
    render(<DailyMetricChart label="Health XP by day" bars={stacked} steps={[20, 25]} stepsLabel="25" legend={[]} />);
    const [first] = screen.getAllByTestId("metric-column");
    const parts = [...first.children] as HTMLElement[];
    expect(parts.map((p) => p.dataset.testid)).toEqual(["metric-stack", "metric-bar"]);
    expect(parts[0]).toHaveClass("bg-chart-3/55", "rounded-t-[4px]");
    expect(parts[1]).not.toHaveClass("rounded-t-[4px]");
    const percent = (el: HTMLElement) => Number(/([\d.]+)%/.exec(el.style.height)![1]);
    expect(percent(parts[0]) + percent(parts[1])).toBeLessThanOrEqual(100);
    // The step line's latest value is on the axis.
    expect(screen.getByText("25")).toBeInTheDocument();
  });
});

// A chart read by pointer and arrow keys is drawn by DailyMetricChart or TrendChart, and reads its
// points through useChartCursor (code review CR98), so each has the same reading, keys and phone
// behaviour; and every such chart lets a phone scroll the page past it (touch-pan-y), where
// touch-none trapped the scroll on the Character page.
describe("the charts in the views", () => {
  const views = import.meta.glob<string>(["/src/**/*.tsx", "!/src/**/*.test.tsx"], {
    query: "?raw",
    import: "default",
    eager: true,
  });
  const charts = Object.entries(views).filter(([, text]) => text.includes("useChartCursor("));

  it("read their points through useChartCursor, and none by keys and pointer of its own", () => {
    const own = Object.entries(views)
      .filter(([, text]) => text.includes("onPointerMove") && text.includes("ArrowLeft"))
      .map(([path]) => path);
    expect(own).toEqual([]);
    expect(charts.length).toBeGreaterThanOrEqual(3);
  });

  it("are the shared ones, the rank chart's lines aside", () => {
    // The rank over time is a line per domain against rank titles, which neither shared chart draws.
    const own = charts.map(([path]) => path).filter((p) => !p.startsWith("/src/components/charts/"));
    expect(own).toEqual(["/src/components/character/domain-reports.tsx"]);
  });

  it("let a phone scroll the page past them", () => {
    expect(charts.filter(([, text]) => text.includes("touch-none")).map(([path]) => path)).toEqual([]);
  });
});

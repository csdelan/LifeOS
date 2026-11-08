import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TrendChart, type TrendChartPoint, type TrendChartSeries } from "@/components/charts/trend-chart";

function point(day: string, value: number, trend = value, flagged = false): TrendChartPoint {
  return { day, value, trend, flagged };
}

function renderChart(points: TrendChartPoint[], target?: number, more: Partial<TrendChartSeries>[] = []) {
  const series: TrendChartSeries[] = [
    { key: "a", label: "Each day's value", points, ...more[0] },
    ...more.slice(1).map((s, i) => ({ key: `s${i}`, label: `Series ${i}`, points: [], ...s })),
  ];
  return render(
    <TrendChart
      label="Weight by day"
      series={series}
      readout={(day) => ({ title: day, text: `read ${day}` })}
      from="2026-10-01"
      to="2026-10-31"
      target={target != null ? { value: target, label: `Goal ${target}` } : undefined}
      formatValue={(n) => String(n)}
      axisLabel={(day) => day.slice(5)}
    />,
  );
}

describe("TrendChart (measurements plan §4.7–§4.8)", () => {
  it("draws a dot a day, spaced by date, and the trend through them", () => {
    renderChart([point("2026-10-01", 200), point("2026-10-02", 199), point("2026-10-31", 195)]);
    const chart = screen.getByRole("group", { name: "Weight by day. Use the arrow keys to read a day." });
    const dots = within(chart).getAllByTestId("trend-dot");
    expect(dots).toHaveLength(3);
    // 31 days across: the first day's middle, the second's, the last's.
    expect(dots.map((d) => d.style.left)).toEqual([`${(0.5 / 31) * 100}%`, `${(1.5 / 31) * 100}%`, `${(30.5 / 31) * 100}%`]);
    expect(screen.queryByTestId("trend-target")).not.toBeInTheDocument();
    expect(screen.queryByTestId("trend-band")).not.toBeInTheDocument();
  });

  it("breaks the trend line across a gap of more than a week, and keeps it across a week", () => {
    const { unmount } = renderChart([
      point("2026-10-01", 200),
      point("2026-10-02", 199.5),
      point("2026-10-09", 199),
      point("2026-10-20", 198),
      point("2026-10-21", 197.8),
    ]);
    // Oct 2 to Oct 9 is 7 days: one line. Oct 9 to Oct 20 is 11: a second.
    expect(screen.getAllByTestId("trend-line")).toHaveLength(2);
    unmount();

    // A lone day after a gap has a dot and no line of its own.
    renderChart([point("2026-10-01", 200), point("2026-10-02", 199.5), point("2026-10-25", 197)]);
    expect(screen.getAllByTestId("trend-line")).toHaveLength(1);
    expect(screen.getAllByTestId("trend-dot")).toHaveLength(3);
  });

  it("draws the target inside the axis, labelled, even when every value is above it", () => {
    renderChart([point("2026-10-01", 200), point("2026-10-15", 199)], 185);
    const target = screen.getByTestId("trend-target");
    const top = parseFloat(target.style.top);
    expect(top).toBeGreaterThan(50);
    expect(top).toBeLessThanOrEqual(100);
    expect(screen.getByRole("list", { name: "Legend" })).toHaveTextContent("Goal 185");
  });

  it("shades the normal range, clipped to the axis, and draws a reading outside it hollow", () => {
    renderChart([point("2026-10-01", 122, 120, true), point("2026-10-08", 116, 118)], undefined, [{ band: { low: 90, high: 119 } }]);
    const band = screen.getByTestId("trend-band");
    // The axis doesn't stretch to 90: the band runs from 119 down to the axis's bottom.
    expect(parseFloat(band.style.top)).toBeGreaterThan(0);
    expect(parseFloat(band.style.top) + parseFloat(band.style.height)).toBeCloseTo(100);
    const dots = screen.getAllByTestId("trend-dot");
    expect(dots.map((d) => d.dataset.flagged ?? null)).toEqual(["true", null]);
    const legend = screen.getByRole("list", { name: "Legend" });
    expect(legend).toHaveTextContent("Normal range");
    expect(legend).toHaveTextContent("Outside the range");
  });

  it("draws two series on one axis, and a day reads across both", () => {
    renderChart([point("2026-10-01", 122), point("2026-10-08", 116)], undefined, [
      { label: "Systolic", band: { low: 90, high: 119 } },
      { label: "Diastolic", points: [point("2026-10-01", 78), point("2026-10-08", 74)], band: { low: 60, high: 79 } },
    ]);
    expect(screen.getAllByTestId("trend-dot")).toHaveLength(4);
    expect(screen.getAllByTestId("trend-line")).toHaveLength(2);
    expect(screen.getAllByTestId("trend-band")).toHaveLength(2);
    const legend = screen.getByRole("list", { name: "Legend" });
    expect(legend).toHaveTextContent("Systolic");
    expect(legend).toHaveTextContent("Diastolic");

    // The keys walk the days, not each series' dots.
    const chart = screen.getByRole("group", { name: /^Weight by day/ });
    fireEvent.focus(chart);
    expect(screen.getByText("2026-10-08: read 2026-10-08")).toBeInTheDocument();
    fireEvent.keyDown(chart, { key: "ArrowLeft" });
    expect(screen.getByText("2026-10-01: read 2026-10-01")).toBeInTheDocument();
  });
});

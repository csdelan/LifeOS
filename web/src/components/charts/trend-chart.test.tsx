import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TrendChart, type TrendChartPoint } from "@/components/charts/trend-chart";

function point(day: string, value: number, trend = value): TrendChartPoint {
  return { day, value, trend, title: day, text: `${value}, trend ${trend}` };
}

function renderChart(points: TrendChartPoint[], target?: number) {
  return render(
    <TrendChart
      label="Weight by day"
      points={points}
      from="2026-10-01"
      to="2026-10-31"
      target={target != null ? { value: target, label: `Goal ${target}` } : undefined}
      formatValue={(n) => String(n)}
      axisLabel={(day) => day.slice(5)}
    />,
  );
}

describe("TrendChart (measurements plan §4.7)", () => {
  it("draws a dot a day, spaced by date, and the trend through them", () => {
    renderChart([point("2026-10-01", 200), point("2026-10-02", 199), point("2026-10-31", 195)]);
    const chart = screen.getByRole("group", { name: "Weight by day. Use the arrow keys to read a day." });
    const dots = within(chart).getAllByTestId("trend-dot");
    expect(dots).toHaveLength(3);
    // 31 days across: the first day's middle, the second's, the last's.
    expect(dots.map((d) => d.style.left)).toEqual([`${(0.5 / 31) * 100}%`, `${(1.5 / 31) * 100}%`, `${(30.5 / 31) * 100}%`]);
    expect(screen.queryByTestId("trend-target")).not.toBeInTheDocument();
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
});

import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useChartCursor } from "@/components/charts/use-chart-cursor";

function Chart({ count, onSelect }: { count: number; onSelect?: (i: number) => void }) {
  const cursor = useChartCursor({ count, onSelect });
  return (
    <div role="group" aria-label="Test chart" {...cursor.props}>
      <output>{cursor.at == null ? "none" : `point ${cursor.at}`}</output>
    </div>
  );
}

const chart = () => screen.getByRole("group", { name: "Test chart" });
const reads = () => screen.getByRole("status").textContent;

describe("a chart's cursor (code review CR98)", () => {
  it("starts at the newest point on focus, moves with the arrow keys, Home and End, and stops at the ends", () => {
    render(<Chart count={5} />);
    expect(reads()).toBe("none");
    fireEvent.focus(chart());
    expect(reads()).toBe("point 4");
    fireEvent.keyDown(chart(), { key: "ArrowRight" });
    expect(reads()).toBe("point 4");
    fireEvent.keyDown(chart(), { key: "ArrowLeft" });
    expect(reads()).toBe("point 3");
    fireEvent.keyDown(chart(), { key: "Home" });
    expect(reads()).toBe("point 0");
    fireEvent.keyDown(chart(), { key: "ArrowLeft" });
    expect(reads()).toBe("point 0");
    fireEvent.keyDown(chart(), { key: "End" });
    expect(reads()).toBe("point 4");
    fireEvent.blur(chart());
    expect(reads()).toBe("none");
  });

  it("reads the point under the pointer, and nothing once it leaves", () => {
    render(<Chart count={4} />);
    vi.spyOn(chart(), "getBoundingClientRect").mockReturnValue({ left: 100, width: 200 } as DOMRect);
    fireEvent.pointerMove(chart(), { clientX: 160 });
    expect(reads()).toBe("point 1");
    fireEvent.pointerMove(chart(), { clientX: 400 });
    expect(reads()).toBe("point 3");
    fireEvent.pointerLeave(chart());
    expect(reads()).toBe("none");
  });

  it("opens a point on Enter or a click when the chart can", () => {
    const onSelect = vi.fn();
    render(<Chart count={4} onSelect={onSelect} />);
    vi.spyOn(chart(), "getBoundingClientRect").mockReturnValue({ left: 0, width: 400 } as DOMRect);
    fireEvent.focus(chart());
    fireEvent.keyDown(chart(), { key: "ArrowLeft" });
    fireEvent.keyDown(chart(), { key: "Enter" });
    expect(onSelect).toHaveBeenLastCalledWith(2);
    fireEvent.click(chart(), { clientX: 10 });
    expect(onSelect).toHaveBeenLastCalledWith(0);
  });

  it("reads nothing on a chart with no points", () => {
    // The bar and rank charts' own copies read point -1 here.
    render(<Chart count={0} />);
    fireEvent.focus(chart());
    fireEvent.keyDown(chart(), { key: "End" });
    expect(reads()).toBe("none");
  });
});

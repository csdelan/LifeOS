import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { HORIZON_MONTHS, MaterializePanel } from "@/components/appointments/materialize-panel";
import { todayIso } from "@/lib/dates";
import { addMonthsClamped } from "@/lib/recurrence";

describe("MaterializePanel", () => {
  // Code review CR65: the kernel refuses a day more than two years ahead, so the picker stops there.
  it("lets the day go from today to two years ahead, as the kernel allows", () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MaterializePanel seriesId="series-1" recurrence='{"freq":"weekly","on":["mon"]}' />
      </QueryClientProvider>,
    );

    const through = screen.getByLabelText("Through");
    expect(HORIZON_MONTHS).toBe(24);
    expect(through).toHaveAttribute("min", todayIso());
    expect(through).toHaveAttribute("max", addMonthsClamped(todayIso(), 24));
  });
});

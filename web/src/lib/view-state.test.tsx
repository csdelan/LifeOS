import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, renderHook, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { DomainDailyChart } from "@/components/character/domain-daily-chart";
import { HomeScoreChart } from "@/components/environment/home-score-chart";
import { resetStore } from "@/lib/mock/store";
import { loadChoice, loadView, saveView, usePersistedChoice } from "@/lib/view-state";

beforeEach(() => {
  resetStore();
  localStorage.clear();
});

describe("remembered choices (code review CR75)", () => {
  it("reads a stored choice only while it is still one of the options", () => {
    saveView("t", { range: "90" });
    expect(loadChoice("t", "range", ["30", "90"], "30")).toBe("90");
    // Renamed or removed since it was stored.
    expect(loadChoice("t", "range", [{ id: "30" }, { id: "365" }], "30")).toBe("30");
    saveView("t", { range: 90 });
    expect(loadChoice("t", "range", ["30", "90"], "30")).toBe("30");
    localStorage.setItem("lifeos.view.t", "not json");
    expect(loadChoice("t", "range", ["30", "90"], "30")).toBe("30");
  });

  it("keeps the other choices stored under the same name when one is set", () => {
    saveView("t", { range: "90", metric: "sleep" });
    const { result } = renderHook(() => usePersistedChoice("t", "metric", ["sleep", "readiness"], "sleep"));

    act(() => result.current[1]("readiness"));

    expect(result.current[0]).toBe("readiness");
    expect(loadView("t", {})).toEqual({ range: "90", metric: "readiness" });
  });

  it("goes on when storage won't save", () => {
    const full = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("The quota has been exceeded.", "QuotaExceededError");
    });
    try {
      expect(() => saveView("t", { range: "90" })).not.toThrow();
    } finally {
      full.mockRestore();
    }
  });

  it("draws a chart whose stored range is no longer offered, on its default", async () => {
    // Ranges the charts don't offer: each one read its range's label with a non-null find, and
    // threw while rendering.
    saveView("environment.chart", { range: "7" });
    saveView("character.chart", { range: "14" });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(
      <QueryClientProvider client={client}>
        <HomeScoreChart />
        <DomainDailyChart domain="health" name="Health" />
      </QueryClientProvider>,
    );

    const ranges = screen.getAllByRole("group", { name: "Range" });
    expect(ranges).toHaveLength(2);
    for (const range of ranges) {
      expect(within(range).getByRole("button", { name: "30 days" })).toHaveAttribute("aria-pressed", "true");
    }
  });
});

import { act, render, renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createElement } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { HomeDayRollover } from "@/components/shell/home-day-rollover";
import { HOME_DAY_CHECK_MS, useHomeToday } from "@/lib/home-day";

// Code review CR58: a page left open overnight moves to the new home day by itself.
describe("the home day on a page that stays open", () => {
  beforeEach(() => {
    window.__LIFEOS_PUBLIC_CONFIG__ = { homeTimeZone: "UTC" };
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-12T23:59:50Z"));
  });

  afterEach(() => {
    delete window.__LIFEOS_PUBLIC_CONFIG__;
    vi.useRealTimers();
  });

  it("turns at midnight while the page is in view", () => {
    const { result } = renderHook(() => useHomeToday());
    expect(result.current).toBe("2026-11-12");

    act(() => vi.advanceTimersByTime(HOME_DAY_CHECK_MS));

    expect(result.current).toBe("2026-11-13");
  });

  it("turns the moment a tab that slept overnight comes back into view", () => {
    const { result } = renderHook(() => useHomeToday());
    // A hidden tab's timers don't run: the clock moves on, and nothing fires until it's shown.
    vi.setSystemTime(new Date("2026-11-13T07:30:00Z"));
    expect(result.current).toBe("2026-11-12");

    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });

    expect(result.current).toBe("2026-11-13");
  });

  it("drops yesterday's cached reads when the day turns, and only then", () => {
    const client = new QueryClient();
    client.setQueryData(["dashboard"], { todayHabits: [{ occurrenceDate: "2026-11-12" }] });
    render(createElement(QueryClientProvider, { client }, createElement(HomeDayRollover)));

    act(() => {
      window.dispatchEvent(new Event("focus"));
    });
    expect(client.getQueryData(["dashboard"])).toBeDefined();

    vi.setSystemTime(new Date("2026-11-13T07:30:00Z"));
    act(() => {
      window.dispatchEvent(new Event("focus"));
    });

    expect(client.getQueryData(["dashboard"])).toBeUndefined();
  });
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { addDays, todayIso } from "@/lib/dates";
import { getState, resetStore } from "@/lib/mock/store";
import { EnvironmentPage } from "@/pages/environment-page";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

beforeEach(() => {
  // Midday, so yesterday has settled (days settle at 4 a.m.).
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 9, 25, 12, 0, 0));
  resetStore();
  localStorage.clear();
});

afterEach(() => {
  vi.useRealTimers();
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <EnvironmentPage />
      </TooltipProvider>
    </QueryClientProvider>,
  );
}

describe("Environment page: the bar (environment plan §4.8)", () => {
  it("says the Home Score today needs to keep the level on track, rounded up", async () => {
    // 6,000 Environment XP: effort has earned level 37, and the seeded month of ratings holds 19.
    getState().ledger.push({
      id: "cleaned",
      occurredAt: new Date().toISOString(),
      entry: "earn",
      provenance: "derived",
      amounts: { environment_xp: 6_000 },
      trigger: "completed",
      awardKey: "completed:task-garage",
      subjectId: "task-garage",
      creditedOn: addDays(todayIso(), -2),
    });
    renderPage();

    expect(await screen.findByTestId("home-score-bar")).toHaveTextContent("· 39.6+ keeps level 19 on track");
    expect(screen.getByTestId("home-score")).toHaveTextContent("69.2");
    expect(await screen.findByText("The bar for your level")).toBeInTheDocument();
  });

  it("says level 1's bar before any Environment XP", async () => {
    renderPage();
    expect(await screen.findByTestId("home-score-bar")).toHaveTextContent("· 30.0+ keeps level 1 on track");
  });
});

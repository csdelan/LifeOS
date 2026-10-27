import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

describe("Environment page: photos (HOME-4, environment plan §8)", () => {
  it("shows a space's Then beside its Now, a box to add the first, and its shots to pick from", async () => {
    const user = userEvent.setup();
    renderPage();
    const office = await screen.findByRole("article", { name: "Office" });
    const photos = within(office).getByTestId("then-and-now");
    expect(within(photos).getByTestId("photo-then")).toHaveAccessibleName(/^Compare Office: Then · \w{3} \d+ · \d+\.\d$/);
    expect(within(photos).getByTestId("photo-now")).toHaveAccessibleName(/^Compare Office: Now · \w{3} \d+ · \d+\.\d$/);
    expect(photos).toHaveTextContent(/photo (today|yesterday|\d+ days ago) · 2 photos$/);

    const garage = screen.getByRole("article", { name: "Garage" });
    expect(within(garage).getByRole("button", { name: "Add photos of Garage: none yet" })).toHaveTextContent(
      "No photos of Garage yet",
    );

    // The Kitchen has two shots; its Counters has one photo, so Now waits for another.
    const kitchen = screen.getByRole("article", { name: "Kitchen" });
    const shots = within(kitchen).getByRole("group", { name: "Shot" });
    expect(within(shots).getByRole("button", { name: "Kitchen" })).toHaveAttribute("aria-pressed", "true");
    await user.click(within(shots).getByRole("button", { name: "Counters" }));
    expect(within(kitchen).getByTestId("photo-then")).toHaveAccessibleName(/^Compare Kitchen · Counters: Then · /);
    expect(
      within(kitchen).getByRole("button", { name: "Add a photo of Kitchen · Counters to compare" }),
    ).toHaveTextContent("Add a photo to compare");
  });

  it("opens the compare dialog on Now, and makes Now the baseline", async () => {
    const user = userEvent.setup();
    renderPage();
    const office = await screen.findByRole("article", { name: "Office" });
    await user.click(within(office).getByTestId("photo-now"));
    const dialog = await screen.findByRole("dialog", { name: "Office" });
    const left = within(dialog).getByTestId("compare-left");
    const right = within(dialog).getByTestId("compare-right");
    expect(within(left).getByRole("button", { name: /^Left side: Then · / })).toHaveAttribute("aria-pressed", "false");
    expect(within(right).getByRole("button", { name: /^Right side: Now · / })).toHaveAttribute("aria-pressed", "true");
    expect(left).toHaveTextContent(/Tidiness \d/);
    const strip = await within(dialog).findByTestId("filmstrip");
    expect(within(strip).getAllByRole("button")).toHaveLength(2);
    expect(within(strip).getAllByRole("button")[0]).toHaveAccessibleName(/ · baseline$/);

    await user.click(within(dialog).getByRole("button", { name: "Make this the baseline" }));
    expect(await within(dialog).findByRole("button", { name: "This is the baseline" })).toBeDisabled();
    expect(within(strip).getAllByRole("button")[1]).toHaveAccessibleName(/ · baseline$/);
    expect(within(strip).getAllByRole("button")[0]).not.toHaveAccessibleName(/baseline/);
  });
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { toast } from "sonner";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { celebrate } from "@/lib/celebrate";
import { addDays, todayIso } from "@/lib/dates";
import { getState, resetStore } from "@/lib/mock/store";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";
import { CharacterPage } from "@/pages/character-page";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

vi.mock("@/lib/celebrate", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/celebrate")>()),
  celebrate: vi.fn(() => ({ confetti: false, sound: false })),
  chime: vi.fn(() => false),
}));

let writes: LifeOsWriteClient;

const router = vi.hoisted(() => ({ search: {} as { view?: "reports" }, navigate: vi.fn() }));

vi.mock("@tanstack/react-router", () => ({
  useSearch: () => router.search,
  useNavigate: () => router.navigate,
}));

vi.mock("@/lib/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/client")>()),
  createWriteClient: () => writes,
}));

beforeEach(() => {
  resetStore();
  localStorage.clear();
  vi.clearAllMocks();
  router.search = {};
  writes = createMockWriteClient();
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <CharacterPage />
      </TooltipProvider>
    </QueryClientProvider>,
  );
}

const healthCard = async () => within(await screen.findByRole("region", { name: "Health" }));

describe("Character page (xp-domains plan §3.12)", () => {
  it("shows Health's lifetime beside how it's going now, and settles once on opening", async () => {
    const settle = vi.spyOn(writes, "settleDomains");
    renderPage();
    const card = await healthCard();

    expect(card.getByText("Level 9")).toBeInTheDocument();
    expect(card.getByText("Novice")).toBeInTheDocument();
    expect(card.getByRole("progressbar", { name: "Progress to Health level 10" })).toBeInTheDocument();
    expect(card.getByText(/· 40 to level 10/)).toBeInTheDocument();
    expect(
      card.getByText("Apprentice at level 10, with a successful day on 50% of days over 14 days: met"),
    ).toBeInTheDocument();
    expect(card.getByText(/3,560 Health XP in all/)).toBeInTheDocument();

    expect(card.getByRole("img", { name: "Today: 0 of 60 base XP" })).toBeInTheDocument();
    expect(card.getByText("24 of the last 30 days successful")).toBeInTheDocument();
    expect(card.getByText("×1.2 · 80% of the last 30 days")).toBeInTheDocument();
    expect(card.getByText("2 more successful days in the window for ×1.3")).toBeInTheDocument();

    expect(settle).toHaveBeenCalledTimes(1);
    expect(toast).not.toHaveBeenCalled();
  });

  it("lists all 11 ranks with the one held marked and each requirement", async () => {
    renderPage();
    const ladder = within(await screen.findByRole("region", { name: "Health ranks" }));
    const ranks = ladder.getAllByRole("listitem");
    expect(ranks).toHaveLength(11);
    expect(ranks[0]).toHaveAttribute("aria-current", "step");
    expect(ranks[0]).toHaveTextContent("Novice(you)where every domain starts");
    expect(ranks[1]).not.toHaveAttribute("aria-current");
    expect(ranks[1]).toHaveTextContent("Lv 10–19Apprenticea successful day on 50% of days over 14 days💎 1 Gem");
    expect(ranks[10]).toHaveTextContent("Grandmaster");
    expect(ranks[10]).toHaveTextContent("5 Gems");
  });

  it("gives domains without rules a small card with their XP", async () => {
    renderPage();
    const others = within(await screen.findByRole("region", { name: "Other domains" }));
    const cards = others.getAllByRole("listitem");
    expect(cards.map((c) => c.textContent)).toEqual([
      expect.stringContaining("Social0 XP"),
      expect.stringContaining("Environment0 XP"),
      expect.stringContaining("Finance0 XP"),
    ]);
    expect(others.getAllByText("Levels start once this domain has its own rules.")).toHaveLength(3);
  });

  it("opens the daily chart: 30 days of bars, a keyboard readout and a table view", async () => {
    const user = userEvent.setup();
    renderPage();
    const card = await healthCard();

    const toggle = card.getByRole("button", { name: "Show the daily chart" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    await user.click(toggle);
    expect(card.getByRole("button", { name: "Hide the daily chart" })).toHaveAttribute("aria-expanded", "true");

    const chart = within(await card.findByRole("region", { name: "Health base XP by day" }));
    expect(await chart.findAllByTestId("day-bar")).toHaveLength(30);
    expect(chart.getByText("24 of the last 30 days")).toBeInTheDocument();

    const plot = chart.getByRole("group", { name: /Health base XP by day, last 30 days/ });
    // Focus reads today; the left arrow steps back to yesterday, a followed day at ×1.2.
    plot.focus();
    await waitFor(() => expect(plot).toHaveAccessibleDescription(/: 0 base XP · short of 60$/));
    await user.keyboard("{ArrowLeft}");
    await waitFor(() => expect(plot).toHaveAccessibleDescription(/: 90 base XP \(108 paid\) · a successful day$/));

    await user.click(chart.getByRole("button", { name: "Table" }));
    const table = chart.getByRole("table", { name: /newest first/ });
    const rows = within(table).getAllByRole("row");
    expect(rows).toHaveLength(31);
    expect(within(rows[1]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["0", "0", "No"]);
    expect(within(rows[2]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["90", "108", "Yes"]);
  });

  it("switches to the Reports tab through the URL, and shows them there", async () => {
    const user = userEvent.setup();
    const { unmount } = renderPage();
    await healthCard();
    await user.click(screen.getByRole("tab", { name: "Reports" }));
    const update = router.navigate.mock.calls[0][0].search as (prev: object) => object;
    expect(update({})).toEqual({ view: "reports" });
    unmount();

    router.search = { view: "reports" };
    renderPage();
    expect(screen.getByRole("tab", { name: "Reports" })).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByRole("list", { name: "Consistency, last 30 days" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Health" })).not.toBeInTheDocument();
  });

  it("pays a rank time alone reached when it opens, and celebrates it", async () => {
    // Health XP filed on yesterday without a reconcile, as an import would: level 10 then.
    getState().ledger.push({
      id: "imported",
      occurredAt: new Date().toISOString(),
      entry: "earn",
      provenance: "derived",
      amounts: { health_xp: 100 },
      trigger: "completed",
      awardKey: "completed:task-longrun",
      subjectId: "task-longrun",
      creditedOn: addDays(todayIso(), -1),
    });
    renderPage();

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith("You're now a Health Apprentice.", expect.objectContaining({ icon: "🏅" })),
    );
    expect(celebrate).toHaveBeenCalledWith("grand");
    const card = await healthCard();
    await waitFor(() => expect(card.getByText("Level 10")).toBeInTheDocument());
    expect(card.getByText("Apprentice")).toBeInTheDocument();
  });
});

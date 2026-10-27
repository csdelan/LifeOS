import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { toast } from "sonner";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { celebrate } from "@/lib/celebrate";
import { addDays, formatDay, formatDayFull, todayIso } from "@/lib/dates";
import { shortDay } from "@/lib/domains";
import { clearMockActivity, setMockOura } from "@/lib/mock/activity-store";
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
  // Midday, so yesterday has settled (days settle at 4 a.m.).
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 9, 25, 12, 0, 0));
  resetStore();
  clearMockActivity();
  localStorage.clear();
  vi.clearAllMocks();
  router.search = {};
  writes = createMockWriteClient();
});

afterEach(() => {
  vi.useRealTimers();
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

describe("Character page (xp-domains plan §7.4.9)", () => {
  it("shows Health's level, its best, what sets it, its two levels and today; and settles once on opening", async () => {
    const settle = vi.spyOn(writes, "settleDomains");
    renderPage();
    const card = await healthCard();

    expect(card.getByText("Level 9")).toBeInTheDocument();
    expect(card.getByText("Novice")).toBeInTheDocument();
    expect(card.getByText(`Best: level 9, Novice · ${formatDay(addDays(todayIso(), -3))}`)).toBeInTheDocument();
    expect(card.getByText("Lifetime XP sets your level: 33 XP to level 10.")).toBeInTheDocument();

    // Lifetime: 243 of the 276 XP level 9 costs.
    expect(card.getByText("Lifetime level 9")).toBeInTheDocument();
    expect(card.getByRole("progressbar", { name: "Progress to Health lifetime level 10" })).toHaveAttribute(
      "aria-valuetext",
      "243 of 276 XP",
    );
    expect(card.getByText(/· 33 to level 10/)).toBeInTheDocument();

    // Consistency: the last 30 counted days support 23; 24 needs one more active day so far.
    expect(card.getByText("Consistency level 23")).toBeInTheDocument();
    expect(card.getByRole("progressbar", { name: "Health average toward consistency level 24" })).toBeInTheDocument();
    const needs = within(card.getByRole("list", { name: "What consistency level 24 needs" }));
    expect(needs.getAllByRole("listitem").map((li) => li.textContent)).toEqual([
      "a 30-day average of 41 base XP (you're at 44.5): met",
      "18 active days of 30 (you have 24): met",
      "25 active days so far (you have 24): not yet",
    ]);
    expect(card.getByText(/1,443 Health XP in all · since .+ · 44\.5 base XP a day over the last 30 days/)).toBeInTheDocument();

    expect(card.getByRole("img", { name: "Today: 0 of 20 action XP to count as active" })).toBeInTheDocument();
    expect(card.getByText("×1.2 · 24 of the last 30 days active")).toBeInTheDocument();
    expect(card.getByText("2 more active days in the window for ×1.3")).toBeInTheDocument();
    expect(
      card.getByText("An active day is at least 20 action XP (base XP less sleep). Days count once they settle at 4 a.m."),
    ).toBeInTheDocument();

    expect(settle).toHaveBeenCalledTimes(1);
    // Nothing kept yet: the page keeps the current best without celebrating it.
    await waitFor(() => expect(localStorage.getItem("lifeos.domains.best")).not.toBeNull());
    expect(toast).not.toHaveBeenCalled();
  });

  it("asks for the throttled Oura sync once on opening while Oura is connected (activity plan §3.8)", async () => {
    const sync = vi.spyOn(writes, "syncActivity");
    renderPage();
    await healthCard();
    await waitFor(() => expect(sync).toHaveBeenCalledTimes(1));
    expect(sync).toHaveBeenCalledWith(false);
  });

  it("leaves Oura alone when it isn't connected", async () => {
    setMockOura({ state: "not_connected", connected: false });
    const sync = vi.spyOn(writes, "syncActivity");
    const settle = vi.spyOn(writes, "settleDomains");
    renderPage();
    await healthCard();
    await waitFor(() => expect(settle).toHaveBeenCalledTimes(1));
    expect(sync).not.toHaveBeenCalled();
  });

  it("shows Environment held by the home: the condition level, today's Home Score against the bar, and kept-up days (environment plan §4.8)", async () => {
    // 6,000 Environment XP filed two days ago: effort has earned level 37, and the seeded month
    // of ratings holds it at 19.
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
    const card = within(await screen.findByRole("region", { name: "Environment" }));

    expect(card.getByText("Level 19")).toBeInTheDocument();
    expect(card.getByText("Apprentice")).toBeInTheDocument();
    expect(card.getByText("Your effort has earned level 37. Your home holds you at 19.")).toBeInTheDocument();
    expect(card.getByText("Lifetime level 37")).toBeInTheDocument();

    expect(card.getByText("Condition level 19")).toBeInTheDocument();
    expect(
      card.getByText("Your home supports level 19: a Home Score of 39.6+ on 20 of the last 27 days."),
    ).toBeInTheDocument();
    expect(card.getByRole("progressbar", { name: "Environment kept-up days toward condition level 20" })).toBeInTheDocument();
    const needs = within(card.getByRole("list", { name: "What condition level 20 needs" }));
    expect(needs.getAllByRole("listitem").map((li) => li.textContent)).toEqual([
      "a Home Score of 40.2+ on 16 of the last 27 days (you have 19): met",
      "21 kept-up days so far (you have 19): not yet",
    ]);
    expect(card.getByText(/6,000 Environment XP in all · since .+ · an average Home Score of 48\.8 over the last 27 days/)).toBeInTheDocument();

    expect(card.getByRole("img", { name: "Today: Home Score 69.2, kept up (39.6+ counts)" })).toBeInTheDocument();
    expect(card.getByText("×1.3 · 27 of the last 30 days kept up")).toBeInTheDocument();
    expect(
      card.getByText("A kept-up day has a Home Score of at least 39.6. Days count once they settle at 4 a.m."),
    ).toBeInTheDocument();

    const ladder = within(await screen.findByRole("region", { name: "Environment ranks" }));
    expect(ladder.getAllByRole("listitem")[2]).toHaveTextContent(
      "Lv 20–29Journeymana Home Score of 40.2+ on 52 of 90 days · 1,517 XP to reach💎 1 Gem",
    );
  });

  it("lists all 11 ranks with the one held marked and what holding each takes", async () => {
    renderPage();
    const ladder = within(await screen.findByRole("region", { name: "Health ranks" }));
    const ranks = ladder.getAllByRole("listitem");
    expect(ranks).toHaveLength(11);
    expect(ranks[0]).toHaveAttribute("aria-current", "step");
    expect(ranks[0]).toHaveTextContent("Lv 1–9Novice(you)where every domain starts");
    expect(ranks[1]).not.toHaveAttribute("aria-current");
    expect(ranks[1]).toHaveTextContent(
      "Lv 10–19Apprenticea 90-day average of 34 base XP, 48 active days of 90 (20+ action XP) · 1,476 XP to reach💎 1 Gem",
    );
    expect(ranks[10]).toHaveTextContent("Lv 100Grandmaster");
    expect(ranks[10]).toHaveTextContent("5 Gems");
  });

  it("gives domains without rules a small card with their XP", async () => {
    renderPage();
    const others = within(await screen.findByRole("region", { name: "Other domains" }));
    const cards = others.getAllByRole("listitem");
    // Environment has rules of its own (E3), so it gets a full card.
    expect(cards.map((c) => c.textContent)).toEqual([
      expect.stringContaining("Social0 XP"),
      expect.stringContaining("Finance0 XP"),
    ]);
    expect(others.getAllByText("Levels start once this domain has its own rules.")).toHaveLength(2);
  });

  it("opens the daily chart: 30 days of action and sleep, a keyboard readout and a table view", async () => {
    const user = userEvent.setup();
    renderPage();
    const card = await healthCard();

    const toggle = card.getByRole("button", { name: "Show the daily chart" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    await user.click(toggle);
    expect(card.getByRole("button", { name: "Hide the daily chart" })).toHaveAttribute("aria-expanded", "true");

    const chart = within(await card.findByRole("region", { name: "Health XP by day" }));
    expect(await chart.findAllByTestId("day-bar")).toHaveLength(30);
    expect(chart.getAllByTestId("day-column").filter((c) => c.dataset.exception === "true")).toHaveLength(1);
    expect(chart.getByText(/^\d+ of the last 30 days active$/)).toBeInTheDocument();
    expect(chart.getByText("· 1 exception day")).toBeInTheDocument();

    const plot = chart.getByRole("group", { name: /Health XP by day, last 30 days/ });
    // Focus reads today; the left arrow steps back to yesterday, a followed day at ×1.2.
    plot.focus();
    await waitFor(() => expect(plot).toHaveAccessibleDescription(/: 0 action XP · not active \(20 needed\)$/));
    await user.keyboard("{ArrowLeft}");
    await waitFor(() => expect(plot).toHaveAccessibleDescription(/: 90 action XP \(108 paid\) · active \(20\+ needed\)$/));

    await user.click(chart.getByRole("button", { name: "Table" }));
    const table = chart.getByRole("table", { name: /newest first/ });
    const rows = within(table).getAllByRole("row");
    expect(rows).toHaveLength(31);
    const cellsOf = (row: HTMLElement) => within(row).getAllByRole("cell").map((c) => c.textContent);
    expect(cellsOf(rows[1])).toEqual(["0", "0", "0", "No (20 needed)", ""]);
    expect(cellsOf(rows[2])).toEqual(["90", "0", "108", "Yes", ""]);
    // Sick 14 days ago, marked as an exception.
    expect(cellsOf(rows[15])).toEqual(["0", "0", "0", "—", "Sick with a cold"]);
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
    expect(await screen.findByTestId("rank-chart")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Health" })).not.toBeInTheDocument();
  });

  it("pays a rank time alone reached when it opens, and celebrates it once", async () => {
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
    // The settle's toast kept the best, so the page doesn't celebrate it a second time.
    expect(vi.mocked(toast).mock.calls.filter(([text]) => text === "You're now a Health Apprentice.")).toHaveLength(1);
    expect(celebrate).toHaveBeenCalledTimes(1);
  });

  it("celebrates a new best reached as days settled, once, the next time it opens", async () => {
    localStorage.setItem("lifeos.domains.best", JSON.stringify({ health_xp: { level: 8, rank: 0 } }));
    const { unmount } = renderPage();

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith("❤️ Health level 9!", {
        icon: "⬆️",
        description: "A new best level, reached as your days settled.",
      }),
    );
    expect(celebrate).toHaveBeenCalledWith("full");
    unmount();

    vi.clearAllMocks();
    const settle = vi.spyOn(writes, "settleDomains");
    renderPage();
    await healthCard();
    await waitFor(() => expect(settle).toHaveBeenCalledTimes(1));
    await act(async () => {
      await settle.mock.results[0].value;
    });
    expect(JSON.parse(localStorage.getItem("lifeos.domains.best")!)).toEqual({
      health_xp: { level: 9, rank: 0 },
      environment_xp: { level: 1, rank: 0 },
    });
    expect(toast).not.toHaveBeenCalled();
  });

  it("marks a day as an exception with a comment, and unmarks one", async () => {
    const user = userEvent.setup();
    renderPage();
    await healthCard();
    const section = within(screen.getByRole("region", { name: "Exception days" }));
    const sick = addDays(todayIso(), -14);
    expect(await section.findByRole("list", { name: "Exception days, last 90 days" })).toHaveTextContent(
      `${formatDayFull(sick)} · Sick with a cold`,
    );

    await user.click(section.getByRole("button", { name: "Mark a day as an exception" }));
    const dialog = within(await screen.findByRole("dialog", { name: "Mark a day as an exception" }));
    expect(dialog.getByRole("radio", { name: "Today" })).toBeChecked();
    // A comment of spaces alone says why it's needed.
    await user.type(dialog.getByRole("textbox", { name: "Why" }), "   ");
    await user.click(dialog.getByRole("button", { name: "Mark as exception" }));
    expect(dialog.getByRole("alert")).toHaveTextContent("Say why the day is an exception");

    await user.clear(dialog.getByRole("textbox", { name: "Why" }));
    await user.type(dialog.getByRole("textbox", { name: "Why" }), "Flying home");
    expect(dialog.getByText("11 of 140 characters")).toBeInTheDocument();
    await user.click(dialog.getByRole("button", { name: "Mark as exception" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith(`Marked ${shortDay(todayIso())} as an exception`);
    const list = within(await section.findByRole("list", { name: "Exception days, last 90 days" }));
    await waitFor(() => expect(list.getAllByRole("listitem")).toHaveLength(2));
    expect(list.getAllByRole("listitem")[0]).toHaveTextContent("Flying home");
    const card = await healthCard();
    await waitFor(() =>
      expect(
        card.getByRole("img", { name: "Today is an exception day: it doesn't count for or against you." }),
      ).toBeInTheDocument(),
    );

    await user.click(section.getByRole("button", { name: `Unmark ${formatDayFull(sick)}` }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(`Unmarked ${shortDay(sick)}: it counts again`));
    await waitFor(() => expect(list.getAllByRole("listitem")).toHaveLength(1));
    expect(list.getByRole("listitem")).toHaveTextContent("Flying home");
  });
});

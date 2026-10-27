import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DomainReports, LevelSpider, RankSpider } from "@/components/character/domain-reports";
import { addDays, formatDay, todayIso } from "@/lib/dates";
import { newBestsText, rankSummary, type RankSeries } from "@/lib/domains";
import { getState, resetStore } from "@/lib/mock/store";
import type { DomainReportRow } from "@/lib/production-ui-types";

beforeEach(() => {
  // Midday, so yesterday has settled and the periods end there.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 9, 25, 12, 0, 0));
  resetStore();
  localStorage.clear();
});

afterEach(() => {
  vi.useRealTimers();
});

function renderReports() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <DomainReports />
    </QueryClientProvider>,
  );
}

/** A row of the By domain table, by its domain's name. */
const tableRow = (name: string) =>
  within(screen.getByRole("table", { name: /Every life domain over the last/ }))
    .getAllByRole("row")
    .find((r) => within(r).queryByRole("rowheader")?.textContent?.includes(name))!;
const cells = (name: string) => within(tableRow(name)).getAllByRole("cell").map((c) => c.textContent);

const TITLES = ["Novice", "Apprentice", "Journeyman", "Adept", "Expert", "Veteran", "Elite", "Master", "Champion", "Legend", "Grandmaster"];

describe("Character Reports (xp-domains plan §7.4.10)", () => {
  it("plots Health's rank over 30 days, its level since the start, and every domain in the table", async () => {
    renderReports();

    const chart = await screen.findByTestId("rank-chart");
    expect(chart).toHaveAccessibleName(
      "Rank over the last 30 days. Health: Novice throughout, best Novice. Environment: Novice throughout, best Novice. Use the arrow keys to read a day.",
    );
    // Health and Environment, one line each.
    expect(screen.getAllByTestId("rank-line")).toHaveLength(2);
    expect(screen.getAllByTestId("best-line")).toHaveLength(2);
    // The sick day 14 days ago is hatched.
    expect(screen.getAllByTestId("exception-band")).toHaveLength(1);
    expect(within(screen.getByRole("list", { name: "Legend" })).getByText("Exception day")).toBeInTheDocument();

    const levels = within(await screen.findByRole("list", { name: "Level since the start" }));
    expect(levels.getAllByRole("listitem")[0]).toHaveTextContent("HealthLevel 9 of 100 · Novice");
    expect(levels.getAllByRole("listitem")[1]).toHaveTextContent("EnvironmentLevel 1 of 100 · Novice");
    expect(screen.getAllByTestId("level-bar")[0]).toHaveStyle({ width: "9%" });
    expect(screen.queryByTestId("best-tick")).not.toBeInTheDocument();

    expect(screen.getByRole("heading", { name: "By domain, last 30 days" })).toBeInTheDocument();
    // 29 counted days (the sick day left out), 24 of them active; today hasn't settled.
    expect(cells("Health")).toEqual([
      "29",
      "1",
      "24 active (83%)",
      "45.5 base XP",
      "1,4281,320 base",
      "1 → 9",
      "Novice",
      "Novice",
      "Level 9 (from 1)",
    ]);
    // Environment counts from its first rated day: kept-up days and the average Home Score.
    expect(cells("Environment")).toEqual(["27", "1", "27 kept up (100%)", "Home Score 48.8", "0", "1", "Novice", "Novice", "None"]);
    // Domains without rules: their XP, nothing else.
    expect(tableRow("Social")).toHaveTextContent("XP only");
    expect(cells("Social")).toEqual(["—", "—", "—", "—", "0", "—", "—", "—", "—"]);
    expect(screen.queryByTestId("spider")).not.toBeInTheDocument();
  });

  it("reads the rank by day as a table, newest first", async () => {
    const user = userEvent.setup();
    renderReports();
    await screen.findByTestId("rank-chart");

    await user.click(screen.getByRole("button", { name: "Table" }));
    const table = within(screen.getByRole("region", { name: "Rank over the last 30 days, as a table" }));
    const rows = table.getAllByRole("row");
    // A row per day for each of Health and Environment.
    expect(rows).toHaveLength(61);
    const cellsOf = (row: HTMLElement) => within(row).getAllByRole("cell").map((c) => c.textContent);
    expect(within(rows[1]).getByRole("rowheader")).toHaveTextContent(formatDay(addDays(todayIso(), -1)));
    expect(cellsOf(rows[1])).toEqual(["Health", "9", "Novice", "Novice", ""]);
    expect(cellsOf(rows[2])).toEqual(["Environment", "1", "Novice", "Novice", ""]);
    expect(within(rows[27]).getByRole("rowheader")).toHaveTextContent(formatDay(addDays(todayIso(), -14)));
    expect(cellsOf(rows[27])).toEqual(["Health", "1", "Novice", "Novice", "Yes"]);
  });

  it("switches the period and remembers the choice", async () => {
    const user = userEvent.setup();
    renderReports();
    await screen.findByTestId("rank-chart");

    await user.click(screen.getByRole("button", { name: "3 months" }));
    await screen.findByRole("heading", { name: "By domain, last 3 months" });
    expect(screen.getByTestId("rank-chart")).toHaveAccessibleName(/^Rank over the last 3 months\./);
    // Health's 31 days of history, inside a 90-day period.
    expect(cells("Health").slice(0, 3)).toEqual(["30", "1", "24 active (80%)"]);
    expect(JSON.parse(localStorage.getItem("lifeos.view.character.reports")!)).toEqual({ period: "90" });
  });

  it("lists a rank and a best level reached in the period", async () => {
    // Health XP filed on yesterday, as an import would: level 10, Apprentice, then.
    const yesterday = addDays(todayIso(), -1);
    getState().ledger.push({
      id: "imported",
      occurredAt: new Date().toISOString(),
      entry: "earn",
      provenance: "derived",
      amounts: { health_xp: 100 },
      trigger: "completed",
      awardKey: "completed:task-longrun",
      subjectId: "task-longrun",
      creditedOn: yesterday,
    });
    const user = userEvent.setup();
    renderReports();
    await screen.findByTestId("rank-chart");
    await user.click(screen.getByRole("button", { name: "7 days" }));

    await screen.findByRole("heading", { name: "By domain, last 7 days" });
    expect(screen.getByTestId("rank-chart")).toHaveAccessibleName(
      "Rank over the last 7 days. Health: Novice at the start, Apprentice at the end, best Apprentice. Environment: Novice throughout, best Novice. Use the arrow keys to read a day.",
    );
    const health = cells("Health");
    expect(health.slice(5)).toEqual([
      "2 → 10",
      "Novice → Apprentice",
      "Apprentice",
      `Level 10 (from 2) · Apprentice ${formatDay(yesterday)}`,
    ]);
  });
});

describe("report words", () => {
  const day = (rank: number, bestRank = rank, exception = false) => ({ day: "2026-10-01", level: rank * 10 + 1, rank, bestRank, exception });

  it("sums up a rank line from its first and last day", () => {
    const series = (days: ReturnType<typeof day>[]): RankSeries => ({ key: "health_xp", name: "Health", days, titles: TITLES });
    expect(rankSummary(series([day(0), day(0)]))).toBe("Health: Novice throughout, best Novice.");
    expect(rankSummary(series([day(1), day(2)]))).toBe("Health: Apprentice at the start, Journeyman at the end, best Journeyman.");
    expect(rankSummary(series([day(5, 6), day(4, 6)]))).toBe("Health: Veteran at the start, Expert at the end, best Elite.");
    expect(rankSummary(series([]))).toBe("Health: no days yet.");
  });

  it("names the bests a period reached, or nothing", () => {
    const row = { bestLevelFrom: 9, bestLevelTo: 12, ranksReached: [{ rank: 1, title: "Apprentice", qualifiedOn: "2026-10-24" }] } as DomainReportRow;
    expect(newBestsText(row)).toBe(`Level 12 (from 9) · Apprentice ${formatDay("2026-10-24")}`);
    expect(newBestsText({ ...row, bestLevelTo: 9, ranksReached: [] })).toBeNull();
  });
});

describe("spider charts, from three configured domains", () => {
  it("plots each domain's rank now, with the period's start dashed", () => {
    render(
      <RankSpider
        periodLabel="30 days"
        points={[
          { key: "health_xp", name: "Health", rankFrom: 0, rankTo: 1, titleFrom: "Novice", titleTo: "Apprentice", topRank: 10 },
          { key: "social_xp", name: "Social", rankFrom: 3, rankTo: 3, titleFrom: "Adept", titleTo: "Adept", topRank: 10 },
          { key: "finance_xp", name: "Finance", rankFrom: 0, rankTo: 0, titleFrom: "Novice", titleTo: "Novice", topRank: 10 },
        ]}
      />,
    );
    expect(screen.getByRole("img", { name: /^Rank now\./ })).toHaveAccessibleName(
      "Rank now. Health: Apprentice, from Novice. Social: Adept. Finance: Novice.",
    );
    expect(screen.getAllByTestId("spider-vertex")).toHaveLength(3);
    expect(screen.getByTestId("spider-compare")).toBeInTheDocument();
    expect(within(screen.getByRole("list", { name: "Legend" })).getByText(/30 days ago/)).toBeInTheDocument();
  });

  it("plots levels with a ring at each rank and the best dashed", () => {
    const rankLevels = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
    const { container } = render(
      <LevelSpider
        points={[
          { key: "health_xp", name: "Health", level: 12, rankTitle: "Apprentice", best: 15, bestRankTitle: "Apprentice", rankLevels },
          { key: "social_xp", name: "Social", level: 31, rankTitle: "Adept", best: 31, bestRankTitle: "Adept", rankLevels },
          { key: "finance_xp", name: "Finance", level: 4, rankTitle: "Novice", best: 4, bestRankTitle: "Novice", rankLevels },
        ]}
      />,
    );
    expect(screen.getByRole("img", { name: /^Level\. Health: level 12, Apprentice; best 15\./ })).toBeInTheDocument();
    // One ring per rank, plus the level polygon and the best's dashed one.
    expect(container.querySelectorAll("polygon")).toHaveLength(rankLevels.length + 2);
    expect(screen.getByText("Level 31 · Adept")).toBeInTheDocument();
  });
});

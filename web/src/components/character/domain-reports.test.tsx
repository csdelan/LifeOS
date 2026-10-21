import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import {
  ConsistencySpider,
  DomainReports,
  LevelSpider,
  type ConsistencyPoint,
} from "@/components/character/domain-reports";
import { formatDay, todayIso } from "@/lib/dates";
import { earnDay, resetStore } from "@/lib/mock/store";

beforeEach(() => {
  resetStore();
  localStorage.clear();
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

describe("Character Reports (xp-domains plan §3.13)", () => {
  it("plots Health's consistency over 30 days as a bar, its level since the start, and every domain in the table", async () => {
    renderReports();

    const consistency = within(await screen.findByRole("list", { name: "Consistency, last 30 days" }));
    expect(consistency.getByRole("listitem")).toHaveTextContent("Health80% · all 30 days");
    expect(screen.getByTestId("consistency-bar")).toHaveAttribute("data-history", "full");

    const levels = within(await screen.findByRole("list", { name: "Level since the start" }));
    expect(levels.getByRole("listitem")).toHaveTextContent("HealthLevel 9 of 100 · Novice");
    expect(screen.getByTestId("level-bar")).toHaveStyle({ width: "9%" });

    expect(screen.getByRole("heading", { name: "By domain, last 30 days" })).toBeInTheDocument();
    expect(cells("Health")).toEqual(["all 30 days", "24", "80%", "2,5702,295 base", "76.5", "4 → 9", "—"]);
    // Domains without rules: their XP, nothing else.
    expect(tableRow("Social")).toHaveTextContent("XP only");
    expect(cells("Social")).toEqual(["—", "—", "—", "0", "—", "—", "—"]);
    expect(screen.queryByTestId("spider")).not.toBeInTheDocument();
  });

  it("draws a period longer than Health's history hollow, says how much backs it, and remembers the choice", async () => {
    const user = userEvent.setup();
    renderReports();
    await screen.findByRole("list", { name: "Consistency, last 30 days" });

    await user.click(screen.getByRole("button", { name: "3 months" }));
    const consistency = within(await screen.findByRole("list", { name: "Consistency, last 3 months" }));
    expect(consistency.getByRole("listitem")).toHaveTextContent("Health77% · 44 of 90 days");
    expect(screen.getByTestId("consistency-bar")).toHaveAttribute("data-history", "partial");
    expect(within(screen.getByRole("list", { name: "Legend" })).getByText("Less history than the period")).toBeInTheDocument();
    expect(cells("Health").slice(0, 3)).toEqual(["44 of 90 days", "34", "77%"]);
    expect(JSON.parse(localStorage.getItem("lifeos.view.character.reports")!)).toMatchObject({ period: "90" });
  });

  it("compares a second period as a tick across the bar, named under it", async () => {
    const user = userEvent.setup();
    renderReports();
    await screen.findByRole("list", { name: "Consistency, last 30 days" });

    const compare = screen.getByRole("combobox", { name: "Compare with" });
    expect(within(compare).queryByRole("option", { name: "30 days" })).not.toBeInTheDocument();
    await user.selectOptions(compare, "365");

    expect(await screen.findByText("12 months: 77% · 44 of 365 days")).toBeInTheDocument();
    // 34 successful of 44 days counted.
    expect(screen.getByTestId("compare-tick").style.left).toMatch(/^77\.27/);
    expect(within(screen.getByRole("list", { name: "Legend" })).getByText(/Last 12 months/)).toBeInTheDocument();
  });

  it("lists a rank reached in the period, and the levels gained", async () => {
    earnDay("habit-mobility", "followed", todayIso());
    const user = userEvent.setup();
    renderReports();
    await screen.findByRole("list", { name: "Consistency, last 30 days" });
    await user.click(screen.getByRole("button", { name: "7 days" }));

    await screen.findByRole("heading", { name: "By domain, last 7 days" });
    const health = cells("Health");
    // Today succeeded, so the week runs to today.
    expect(health[0]).toBe("all 7 days");
    expect(health[5]).toMatch(/^\d → 10$/);
    expect(health[6]).toBe(`Apprentice (${formatDay(todayIso())})`);
  });
});

describe("spider charts, from three configured domains", () => {
  const points: ConsistencyPoint[] = [
    { key: "health_xp", name: "Health", value: 0.8, daysCounted: 30, days: 30, compare: { value: 0.6, daysCounted: 30, days: 365, label: "12 months" } },
    { key: "social_xp", name: "Social", value: 0.88, daysCounted: 12, days: 30, compare: { value: 0.9, daysCounted: 12, days: 365, label: "12 months" } },
    { key: "finance_xp", name: "Finance", value: null, daysCounted: 0, days: 30 },
  ];

  it("plots consistency with each value's history in words, hollow where it's short, and the comparison dashed", () => {
    render(<ConsistencySpider points={points} periodLabel="30 days" />);

    const spider = screen.getByRole("img", { name: /^Last 30 days\./ });
    expect(spider).toHaveAccessibleName(
      "Last 30 days. Health: 80% of days successful, over all 30 days. Social: 88% of days successful, over 12 of 30 days. Finance: no days yet in the last 30 days.",
    );
    // Finance has no days yet, so no vertex; Social's is hollow.
    const vertices = screen.getAllByTestId("spider-vertex");
    expect(vertices.map((v) => v.getAttribute("data-history"))).toEqual(["full", "partial"]);
    expect(screen.getByTestId("spider-compare")).toBeInTheDocument();
    expect(screen.getByText("88% · 12 of 30 days")).toBeInTheDocument();
    const legend = within(screen.getByRole("list", { name: "Legend" }));
    expect(legend.getByText(/Less history than the period/)).toBeInTheDocument();
    expect(legend.getByText(/Last 12 months/)).toBeInTheDocument();
  });

  it("plots levels with a ring at each rank", () => {
    const rankLevels = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
    const { container } = render(
      <LevelSpider
        points={[
          { key: "health_xp", name: "Health", level: 12, rankTitle: "Apprentice", rankLevels },
          { key: "social_xp", name: "Social", level: 31, rankTitle: "Adept", rankLevels },
          { key: "finance_xp", name: "Finance", level: 4, rankTitle: "Novice", rankLevels },
        ]}
      />,
    );
    expect(screen.getByRole("img", { name: /^Level\. Health: level 12, Apprentice\./ })).toBeInTheDocument();
    // One ring per rank, plus the period polygon.
    expect(container.querySelectorAll("polygon")).toHaveLength(rankLevels.length + 1);
    expect(screen.getByText("Level 31 · Adept")).toBeInTheDocument();
  });
});

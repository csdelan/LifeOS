import { describe, expect, it } from "vitest";
import type { LedgerDay, LedgerRow } from "@/lib/production-ui-types";
import {
  chartAnchor,
  dayPeriod,
  dollarsMoved,
  entryDetail,
  entryTitle,
  filterLedger,
  groupByDay,
  heatLevel,
  historyRange,
  monthCells,
  monthPeriod,
  netEarned,
  niceMax,
  periodsEnding,
  totalsFor,
  totalsInWords,
  weekPeriod,
} from "@/lib/rewards-history";

const day = (date: string, earned = {}, reversed = {}, spent = {}): LedgerDay => ({
  date,
  earned,
  reversed,
  spent,
  net: {},
});

/** A ledger row at a local wall-clock time, so grouping reads the same in any zone. */
const row = (
  id: string,
  local: [number, number, number, number],
  entry: LedgerRow["entry"],
  amounts: LedgerRow["amounts"],
  extra: Partial<LedgerRow> = {},
): LedgerRow => {
  const [y, m, d, h] = local;
  return { id, occurredAt: new Date(y, m - 1, d, h).toISOString(), entry, provenance: "derived", amounts, ...extra };
};

describe("periods", () => {
  it("weeks start on Sunday and months on the 1st; each ends where the next begins", () => {
    expect(weekPeriod("2026-10-14")).toEqual({ kind: "week", start: "2026-10-11", end: "2026-10-18" });
    expect(monthPeriod("2026-12-31")).toEqual({ kind: "month", start: "2026-12-01", end: "2027-01-01" });
    expect(dayPeriod("2026-10-31")).toEqual({ kind: "day", start: "2026-10-31", end: "2026-11-01" });
  });

  it("charts 8 weeks or 6 months, oldest first, ending on the one holding the anchor", () => {
    const weeks = periodsEnding("week", "2026-10-14");
    expect(weeks).toHaveLength(8);
    expect([weeks[0].start, weeks.at(-1)!.start]).toEqual(["2026-08-23", "2026-10-11"]);
    expect(periodsEnding("month", "2026-03-31").map((m) => m.start)).toEqual([
      "2025-10-01",
      "2025-11-01",
      "2025-12-01",
      "2026-01-01",
      "2026-02-01",
      "2026-03-01",
    ]);
  });

  it("anchors the chart on today in the current month, else on the month's last day", () => {
    expect(chartAnchor("2026-10-01", "2026-10-20")).toBe("2026-10-20");
    expect(chartAnchor("2026-09-01", "2026-10-20")).toBe("2026-09-30");
  });

  it("reads one range covering the month and both charts", () => {
    expect(historyRange("2026-10-01", "2026-10-20")).toEqual({ from: "2026-05-01", to: "2026-11-01" });
    // September's last week runs into October, so the read runs to that week's end.
    expect(historyRange("2026-09-01", "2026-10-20")).toEqual({ from: "2026-04-01", to: "2026-10-04" });
  });

  it("lays a month out Sunday first, padded to whole weeks", () => {
    const october = monthCells("2026-10-01");
    expect(october).toHaveLength(35);
    expect(october.slice(0, 5)).toEqual([null, null, null, null, "2026-10-01"]);
    expect(october.at(-1)).toBe("2026-10-31");
    expect(monthCells("2026-02-01")).toHaveLength(28);
  });
});

describe("totals", () => {
  const days = [
    day("2026-10-10", { xp: 5 }),
    day("2026-10-11", { xp: 10, coins: 2 }, { xp: 3 }),
    day("2026-10-17", { xp: 1 }, {}, { freedom: 45 }),
    day("2026-10-18", { xp: 50 }),
  ];

  it("rolls the days inside a period up, in display order", () => {
    expect(totalsFor(days, weekPeriod("2026-10-14"))).toMatchObject({
      earned: { coins: 2, xp: 11 },
      reversed: { xp: 3 },
      spent: { freedom: 45 },
    });
  });

  it("counts earned net of what was given back", () => {
    expect(netEarned(days[1], "xp")).toBe(7);
    expect(netEarned(days[1], "gems")).toBe(0);
  });

  it("says a period in words", () => {
    expect(totalsInWords(totalsFor(days, weekPeriod("2026-10-14")))).toBe(
      "earned 2 Coins, 11 XP; gave back 3 XP; spent 45 Freedom",
    );
    expect(totalsInWords(day("2026-10-01"))).toBe("nothing earned or spent");
  });

  it("buckets heat in four steps and rounds the axis up to 1, 2 or 5", () => {
    expect([0, 1, 50, 51, 100].map((v) => heatLevel(v, 100))).toEqual([0, 1, 2, 3, 4]);
    expect(heatLevel(-5, 100)).toBe(0);
    expect([0, 1, 3, 7, 95, 101, 250].map(niceMax)).toEqual([1, 1, 5, 10, 100, 200, 500]);
  });
});

describe("the ledger behind a period", () => {
  const rows = [
    row("a", [2026, 10, 14, 18], "earn", { xp: 10, coins: 2 }, { trigger: "completed", subject: { id: "t", urn: "u", type: "Task", title: "Fix the gate" } }),
    row("b", [2026, 10, 14, 9], "reversal", { xp: -5 }, { trigger: "followed" }),
    row("c", [2026, 10, 13, 20], "redeem", { coins: -10 }, { note: "First deposit", dollars: 5, subject: { id: "r", urn: "u", type: "Reward", title: "Vacation fund" } }),
    row("d", [2026, 10, 12, 8], "redeem", { freedom: -45 }, { note: "Season finale", dollars: null }),
    row("e", [2026, 10, 10, 7], "open", {}),
  ];

  it("filters by kind and by currency; the opening shows only unfiltered", () => {
    expect(filterLedger(rows, "all", "all").map((r) => r.id)).toEqual(["a", "b", "c", "d", "e"]);
    expect(filterLedger(rows, "redeem", "all").map((r) => r.id)).toEqual(["c", "d"]);
    expect(filterLedger(rows, "all", "coins").map((r) => r.id)).toEqual(["a", "c"]);
    expect(filterLedger(rows, "earn", "freedom")).toEqual([]);
  });

  it("groups by local day, newest first, keeping each day's order", () => {
    expect(groupByDay(rows).map((g) => [g.date, g.rows.map((r) => r.id)])).toEqual([
      ["2026-10-14", ["a", "b"]],
      ["2026-10-13", ["c"]],
      ["2026-10-12", ["d"]],
      ["2026-10-10", ["e"]],
    ]);
  });

  it("says what each entry was for", () => {
    expect(rows.map(entryDetail)).toEqual([
      "completed",
      "given back · day followed undone",
      "redeemed — First deposit",
      "custom redemption",
      "nothing done before this pays",
    ]);
    expect(entryTitle(rows[3])).toBe("Season finale");
    expect(entryTitle(rows[4])).toBe("Wallet opened");
  });

  it("adds up the dollars that moved", () => {
    expect(dollarsMoved([...rows, { ...rows[2], id: "f", dollars: 2.5 }])).toBe(7.5);
  });
});

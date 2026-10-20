import { beforeEach, describe, expect, it } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import { getState, mockLedgerDaily, mockWallet, resetStore, setStatus } from "@/lib/mock/store";
import { sumAmounts, viewerTimeZone } from "@/lib/rewards-history";

describe("mockLedgerDaily (LedgerDays, GET /api/ledger/daily)", () => {
  beforeEach(() => {
    resetStore();
  });

  it("files an entry on the date it reads in the viewer's zone", () => {
    // 11:30 pm in Chicago on Jan 14 is already Jan 15 in UTC.
    getState().ledger.push({
      id: "late-earn",
      occurredAt: "2026-01-15T05:30:00.000Z",
      entry: "earn",
      provenance: "derived",
      amounts: { xp: 5 },
    });

    expect(mockLedgerDaily({ from: "2026-01-14", to: "2026-01-16", tz: "America/Chicago" })).toEqual([
      { date: "2026-01-14", earned: { xp: 5 }, reversed: {}, spent: {}, net: { xp: 5 } },
    ]);
    expect(mockLedgerDaily({ from: "2026-01-14", to: "2026-01-16", tz: "UTC" }).map((d) => d.date)).toEqual([
      "2026-01-15",
    ]);
    // The range ends before its last day.
    expect(mockLedgerDaily({ from: "2026-01-14", to: "2026-01-15", tz: "UTC" })).toEqual([]);
  });

  it("totals what each day earned, gave back and spent — and the days add up to the wallet", () => {
    setStatus("task-shoes", "Completed");
    setStatus("task-shoes", "In progress");
    const today = todayIso();
    const days = mockLedgerDaily({ from: addDays(today, -400), to: addDays(today, 1), tz: viewerTimeZone() });

    // Today holds the completion and its reversal (and, late at night, a seeded habit day).
    const todays = days.at(-1)!;
    expect(todays.date).toBe(today);
    expect(todays.reversed).toEqual({ freedom: 10, xp: 10 });
    expect(todays.earned.xp).toBeGreaterThanOrEqual(10);
    const wallet = mockWallet();
    const total = (pick: "earned" | "reversed" | "spent") => sumAmounts(...days.map((d) => d[pick]));
    for (const line of wallet.balances) {
      expect([line.currency, total("earned")[line.currency] ?? 0]).toEqual([line.currency, line.earned]);
      expect([line.currency, total("reversed")[line.currency] ?? 0]).toEqual([line.currency, line.reversed]);
      expect([line.currency, total("spent")[line.currency] ?? 0]).toEqual([line.currency, line.spent]);
    }
  });

  it("refuses what the kernel refuses, in its words", () => {
    expect(() => mockLedgerDaily({ from: "2026-01-14", to: "2026-01-14", tz: "UTC" })).toThrow(
      "The history range must end after it starts.",
    );
    expect(() => mockLedgerDaily({ from: "2024-01-01", to: "2026-01-02", tz: "UTC" })).toThrow(
      "The history range can span at most 731 days; this one spans 732.",
    );
    expect(() => mockLedgerDaily({ from: "2026-01-14", to: "2026-01-15", tz: "Mars/Olympus" })).toThrow(
      "Unknown time zone 'Mars/Olympus'.",
    );
  });
});

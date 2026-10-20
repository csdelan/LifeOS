import { describe, expect, it } from "vitest";
import {
  amountsFrom,
  coinsToUsd,
  formatAmounts,
  ledgerLabel,
  normalizeRedemption,
  normalizeReward,
  priceProblem,
  redeemProblem,
  redemptionMessage,
  sameAmounts,
  shortfall,
  targetProblem,
} from "@/lib/rewards";
import type { RewardRow, Wallet } from "@/lib/production-ui-types";

const wallet: Wallet = {
  openedAt: "2026-10-10T00:00:00Z",
  coinValueUsd: 0.5,
  balances: [
    { currency: "coins", label: "Coins", spendable: true, earned: 20, reversed: 0, spent: 7, balance: 13 },
    { currency: "freedom", label: "Freedom", spendable: true, earned: 60, reversed: 0, spent: 0, balance: 60 },
    { currency: "gems", label: "Gems", spendable: true, earned: 0, reversed: 0, spent: 0, balance: 0 },
    { currency: "xp", label: "XP", spendable: false, earned: 115, reversed: 0, spent: 0, balance: 115 },
  ],
};

function reward(partial: Partial<RewardRow>): RewardRow {
  return {
    id: "r1",
    urn: "urn:bsk:reward:movie-night-r1",
    name: "Movie night",
    kind: "instant",
    price: { freedom: 120 },
    redemptions: 0,
    coinsRedeemed: 0,
    dollarsRedeemed: 0,
    archived: false,
    ...partial,
  };
}

describe("rewards formatting", () => {
  it("lists amounts in the Domain's currency order", () => {
    expect(formatAmounts({ karma: 2, freedom: 30 })).toBe("30 Freedom · 2 Karma");
    expect(formatAmounts({ xp: 10, coins: -2 }, { signed: true })).toBe("−2 Coins +10 XP");
  });

  it("prices Coins in dollars to the cent", () => {
    expect(coinsToUsd(3, 0.5)).toBe(1.5);
    expect(coinsToUsd(1, 0.333)).toBe(0.33);
  });

  it("says what a redemption did", () => {
    const savings = reward({ name: "Vacation fund", kind: "savings", price: {} });
    expect(
      redemptionMessage({ id: "e", amounts: { coins: 4 }, dollars: 2, reward: savings, wallet }),
    ).toBe("Put 4 Coins ($2.00) toward Vacation fund");
    expect(redemptionMessage({ id: "e", amounts: { freedom: 45 }, reward: reward({}), wallet })).toBe(
      "Redeemed Movie night: 45 Freedom",
    );
  });

  it("labels ledger rows by what they were for", () => {
    const subject = { id: "h", urn: "urn:bsk:habit:x", type: "Habit" as const, title: "Morning mobility" };
    const base = { id: "l", occurredAt: "", provenance: "derived", amounts: {} };
    expect(ledgerLabel({ ...base, entry: "earn", trigger: "streak_7", subject })).toBe(
      "Morning mobility · 7-day streak",
    );
    expect(ledgerLabel({ ...base, entry: "reversal", subject })).toBe("Given back: Morning mobility");
    expect(ledgerLabel({ ...base, entry: "redeem", note: "Coffee out" })).toBe("Coffee out");
  });
});

describe("redeem rules (mirroring RedeemAsync)", () => {
  it("names the first currency the wallet can't cover, in the kernel's words", () => {
    expect(shortfall(wallet, { coins: 5, freedom: 120 })).toBe("You have 60 Freedom; this needs 120.");
    expect(shortfall(wallet, { coins: 13 })).toBeNull();
  });

  it("refuses tracked currencies and amounts that aren't whole numbers above zero", () => {
    expect(redeemProblem({ amounts: { xp: 5 }, note: "x" }, wallet)).toBe(
      "XP is tracked, not spent; it can't be redeemed.",
    );
    expect(redeemProblem({ amounts: { freedom: 1.5 }, note: "x" }, wallet)).toBe(
      "Use a whole number of Freedom above zero.",
    );
  });

  it("needs a note only without a Reward", () => {
    expect(redeemProblem({ amounts: { freedom: 5 }, note: "  " }, wallet)).toBe(
      "Say what this redemption is for: a note is required.",
    );
    expect(redeemProblem({ reward: reward({ price: { freedom: 45 } }), amounts: {}, note: "" }, wallet)).toBeNull();
  });

  it("charges the catalog price unless amounts are given for this time", () => {
    const movie = reward({});
    expect(redeemProblem({ reward: movie, amounts: {}, note: "" }, wallet)).toBe(
      "You have 60 Freedom; this needs 120.",
    );
    expect(redeemProblem({ reward: movie, amounts: { freedom: 45 }, note: "" }, wallet)).toBeNull();
  });

  it("takes only Coins into a savings goal, and refuses archived Rewards", () => {
    const fund = reward({ name: "Vacation fund", kind: "savings", price: {} });
    expect(redeemProblem({ reward: fund, amounts: { freedom: 5 }, note: "" }, wallet)).toBe(
      "'Vacation fund' is a savings goal: only Coins go toward it.",
    );
    expect(redeemProblem({ reward: fund, amounts: { coins: 4 }, note: "" }, wallet)).toBeNull();
    expect(redeemProblem({ reward: reward({ archived: true }), amounts: {}, note: "" }, wallet)).toBe(
      "'Movie night' is archived; restore it to redeem against it.",
    );
  });

  it("checks catalog prices and savings targets", () => {
    expect(priceProblem({ freedom: 120, karma: 2 })).toBeNull();
    expect(priceProblem({ resolve: 1 })).toBe("Resolve is tracked, not spent; it can't be a price.");
    expect(targetProblem("")).toBeNull();
    expect(targetProblem("1250.50")).toBeNull();
    expect(targetProblem("0")).toBe("A savings target is a dollar amount above zero.");
  });
});

describe("amount drafts", () => {
  it("drops blank and zero boxes and keeps the rest as typed", () => {
    expect(amountsFrom({ coins: "", freedom: "45", gems: "0", karma: "-1" })).toEqual({ freedom: 45, karma: -1 });
    expect(sameAmounts({ freedom: 45 }, { freedom: 45, gems: 0 })).toBe(true);
    expect(sameAmounts({ freedom: 45 }, { freedom: 30 })).toBe(false);
  });
});

describe("API normalization", () => {
  it("turns the API's int64 / decimal strings into numbers", () => {
    const row = normalizeReward({
      ...reward({ kind: "savings", price: {} }),
      targetUsd: "500.00" as unknown as number,
      dollarsRedeemed: "5.00" as unknown as number,
      percentOfTarget: "1.0" as unknown as number,
      redemptions: "1" as unknown as number,
    });
    expect([row.targetUsd, row.dollarsRedeemed, row.percentOfTarget, row.redemptions]).toEqual([500, 5, 1, 1]);

    const redemption = normalizeRedemption({
      id: "e",
      amounts: { coins: "4" as unknown as number },
      dollars: "2.00" as unknown as number,
      coinRate: null,
      reward: null,
      wallet: { ...wallet, coinValueUsd: "0.50" as unknown as number },
    });
    expect([redemption.amounts.coins, redemption.dollars, redemption.coinRate, redemption.wallet.coinValueUsd]).toEqual(
      [4, 2, null, 0.5],
    );
  });
});

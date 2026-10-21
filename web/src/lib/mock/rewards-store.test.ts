import { beforeEach, describe, expect, it } from "vitest";
import {
  applyAttrs,
  archiveSubject,
  getState,
  mockAddReward,
  mockLedger,
  mockPriceReward,
  mockRedeem,
  mockRewards,
  mockTargetReward,
  mockWallet,
  renameSubject,
  resetStore,
} from "@/lib/mock/store";
import { balanceOf } from "@/lib/rewards";

const reward = (id: string) => mockRewards(true).find((r) => r.id === id)!;

describe("mock rewards (v_wallet / v_ledger / v_reward and RedeemAsync)", () => {
  beforeEach(() => {
    resetStore();
  });

  it("folds the seeded ledger into every currency's balance", () => {
    const wallet = mockWallet();
    expect(wallet.balances.map((b) => [b.currency, b.balance])).toEqual([
      ["coins", 13],
      ["freedom", 60],
      ["gems", 0],
      ["karma", 3],
      ["xp", 115],
      ["resolve", 2],
      ["health_xp", 0],
      ["social_xp", 0],
      ["environment_xp", 0],
      ["finance_xp", 0],
    ]);
    expect(wallet.balances.find((b) => b.currency === "freedom")).toMatchObject({ earned: 105, spent: 45 });
    expect(wallet.openedAt).toBeTruthy();
  });

  it("redeems an instant reward at its price and logs it newest first", () => {
    const redemption = mockRedeem({ reward: "reward-episode", note: "Season finale, part two" });

    expect(redemption.amounts).toEqual({ freedom: 45 });
    expect(redemption.reward?.redemptions).toBe(2);
    expect(balanceOf(redemption.wallet, "freedom")).toBe(15);
    const [latest] = mockLedger({ limit: 1 });
    expect(latest).toMatchObject({
      entry: "redeem",
      amounts: { freedom: -45 },
      note: "Season finale, part two",
      subject: { id: "reward-episode", type: "Reward" },
    });
  });

  it("takes a different amount for this time, and refuses an overdraft without writing", () => {
    const entries = getState().ledger.length;

    expect(() => mockRedeem({ reward: "reward-movie" })).toThrow("You have 60 Freedom; this needs 120.");
    expect(getState().ledger.length).toBe(entries);

    expect(mockRedeem({ reward: "Movie night", amounts: { freedom: 50 } }).amounts).toEqual({ freedom: 50 });
  });

  it("stamps the dollars and the rate on a savings deposit and moves its progress", () => {
    const deposit = mockRedeem({ reward: "reward-vacation", amounts: { coins: 4 } });

    expect([deposit.dollars, deposit.coinRate]).toEqual([2, 0.5]);
    expect(deposit.reward).toMatchObject({ dollarsRedeemed: 7, percentOfTarget: 1.4, remainingUsd: 493 });
    expect(() => mockRedeem({ reward: "reward-vacation", amounts: { freedom: 5 } })).toThrow(
      "'Vacation fund' is a savings goal: only Coins go toward it.",
    );
    expect(() => mockRedeem({ reward: "reward-vacation" })).toThrow("Say how many Coins");
  });

  it("refuses what the kernel refuses", () => {
    expect(() => mockRedeem({ amounts: { freedom: 5 } })).toThrow("a note is required");
    expect(() => mockRedeem({ amounts: { xp: 5 }, note: "x" })).toThrow("Life XP is tracked, not spent");
    expect(() => mockRedeem({ reward: "task-types" })).toThrow("is a Task, not a Reward");
    archiveSubject("reward-takeout", true);
    expect(() => mockRedeem({ reward: "reward-takeout" })).toThrow("is archived; restore it");
  });

  it("adds, reprices and retargets catalog rewards", () => {
    const pizza = mockAddReward({ title: "Pizza night", price: { freedom: 30, karma: 1 }, description: "Friday" });
    expect(pizza).toMatchObject({ kind: "instant", price: { freedom: 30, karma: 1 }, description: "Friday" });
    expect(mockPriceReward(pizza.id, { gems: 1 }).price).toEqual({ gems: 1 });

    const bike = mockAddReward({ title: "Bike fund", kind: "savings", targetUsd: 300 });
    expect(bike).toMatchObject({ kind: "savings", targetUsd: 300, percentOfTarget: 0, remainingUsd: 300 });
    expect(mockTargetReward(bike.id, null).targetUsd).toBeNull();

    expect(() => mockAddReward({ title: "Oops", kind: "savings", price: { coins: 1 } })).toThrow(
      "A savings goal has no price",
    );
    expect(() => mockTargetReward(pizza.id, 10)).toThrow("it has a price, not a target");
    expect(() => mockPriceReward(bike.id, { coins: 1 })).toThrow("so it has no price");
  });

  it("keeps a reward's price through ordinary edits and renames", () => {
    applyAttrs("reward-movie", { description: "Two films" });
    renameSubject("reward-movie", "Double feature");

    expect(reward("reward-movie")).toMatchObject({
      name: "Double feature",
      description: "Two films",
      price: { freedom: 120 },
    });
  });

  it("lists archived rewards only when asked", () => {
    archiveSubject("reward-roadtrip", true);
    expect(mockRewards().some((r) => r.id === "reward-roadtrip")).toBe(false);
    expect(reward("reward-roadtrip").archived).toBe(true);
  });
});

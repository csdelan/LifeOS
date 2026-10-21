import { afterEach, describe, expect, it, vi } from "vitest";
import { toast } from "sonner";
import { celebrate, chime } from "@/lib/celebrate";
import { announceAwards, summarizeAwards } from "@/lib/earn-feedback";
import type { Award } from "@/lib/production-ui-types";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

vi.mock("@/lib/celebrate", () => ({
  celebrate: vi.fn(() => ({ confetti: true, sound: true })),
  chime: vi.fn(() => true),
}));

afterEach(() => {
  vi.clearAllMocks();
});

const subject = (title: string) => ({ id: title, urn: `urn:bsk:x:${title}`, type: "Habit" as const, title });

function award(trigger: string, amounts: Award["amounts"], entry: Award["entry"] = "earn", title = "Morning mobility"): Award {
  return { id: `${trigger}-${title}`, entry, trigger, awardKey: `${trigger}:${title}`, subject: subject(title), amounts };
}

describe("summarizeAwards", () => {
  it("sums what was earned and given back, item by item", () => {
    const summary = summarizeAwards([
      award("followed", { coins: 1, freedom: 5, xp: 5 }),
      award("streak_7", { coins: 5, freedom: 15, xp: 25 }),
      award("completed", { coins: -2, xp: -10 }, "reversal", "Buy race shoes"),
    ]);
    expect(summary.earned).toEqual({ coins: 6, freedom: 20, xp: 30 });
    expect(summary.givenBack).toEqual({ coins: -2, xp: -10 });
    expect(summary.earnedFor).toBe("Morning mobility — day followed, 7-day streak");
    expect(summary.givenBackFor).toBe("Buy race shoes — completed");
    expect(summary.big).toBe(true);
  });

  it("names time on the timer beside completing", () => {
    const summary = summarizeAwards([
      award("completed", { freedom: 10, xp: 10 }, "earn", "Write the report"),
      award("timed", { xp: 4 }, "earn", "Write the report"),
    ]);
    expect(summary.earned).toEqual({ freedom: 10, xp: 14 });
    expect(summary.earnedFor).toBe("Write the report — completed, time on the timer");
    expect(summary.big).toBe(false);
  });

  it("names a rank-up by its note, since it belongs to no item, and celebrates it", () => {
    const rankUp: Award = {
      id: "rank",
      entry: "earn",
      trigger: "rank",
      awardKey: "rank:health_xp:10",
      subject: null,
      note: "Health Apprentice",
      amounts: { gems: 1 },
    };
    const summary = summarizeAwards([award("followed", { health_xp: 70 }), rankUp]);
    expect(summary.earned).toEqual({ health_xp: 70, gems: 1 });
    expect(summary.earnedFor).toBe("Morning mobility — day followed · Health Apprentice — rank-up");
    expect(summary.big).toBe(true);
  });

  it("shows how domain XP paid at a multiplier was worked out", () => {
    const multiplied: Award = {
      ...award("followed", { freedom: 5, health_xp: 78 }),
      base: { health_xp: 60 },
      multipliers: { health_xp: 1.3 },
    };
    const summary = summarizeAwards([multiplied, award("completed", { health_xp: 25 }, "earn", "Stretch")]);
    expect(summary.earned).toEqual({ freedom: 5, health_xp: 103 });
    expect(summary.earnedFor).toBe("Morning mobility — day followed (❤️ 60 × 1.3) · Stretch — completed");
  });

  it("is big for Gems, a streak milestone or establishing — not an ordinary day", () => {
    expect(summarizeAwards([award("followed", { xp: 5 })]).big).toBe(false);
    expect(summarizeAwards([award("completed", { gems: 1 })]).big).toBe(true);
    expect(summarizeAwards([award("established", { xp: 100 })]).big).toBe(true);
    expect(summarizeAwards([award("streak_7", { xp: -25 }, "reversal")]).big).toBe(false);
  });
});

describe("announceAwards", () => {
  it("toasts an earn and chimes", () => {
    announceAwards([award("completed", { coins: 2, freedom: 10, xp: 10 }, "earn", "Buy race shoes")]);
    expect(toast).toHaveBeenCalledWith("Earned +2 Coins +10 Freedom +10 Life XP", {
      icon: "🪙",
      description: "Buy race shoes — completed",
    });
    expect(chime).toHaveBeenCalledTimes(1);
    expect(celebrate).not.toHaveBeenCalled();
  });

  it("celebrates the big ones instead of chiming", () => {
    announceAwards([award("established", { coins: 25, gems: 1, xp: 100 })]);
    expect(celebrate).toHaveBeenCalledTimes(1);
    expect(chime).not.toHaveBeenCalled();
  });

  it("gives back quietly", () => {
    announceAwards([award("completed", { coins: -2 }, "reversal", "Buy race shoes")]);
    expect(toast).toHaveBeenCalledWith("Gave back −2 Coins", {
      icon: "↩",
      description: "Buy race shoes — completed",
    });
    expect(chime).not.toHaveBeenCalled();
    expect(celebrate).not.toHaveBeenCalled();
  });

  it("says nothing when nothing was paid", () => {
    announceAwards([]);
    announceAwards(undefined);
    expect(toast).not.toHaveBeenCalled();
  });
});

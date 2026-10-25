import { beforeEach, describe, expect, it } from "vitest";
import { keepBest, keptBest, newBest } from "@/lib/domain-best";

describe("the last best celebrated (xp-domains plan §7.4.7)", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("keeps the first best it sees without celebrating it", () => {
    expect(newBest("health_xp", { level: 9, rank: 0 })).toBeNull();
    expect(keptBest("health_xp")).toEqual({ level: 9, rank: 0 });
  });

  it("celebrates a new best rank over a new best level, once", () => {
    keepBest("health_xp", { level: 9, rank: 0 });
    expect(newBest("health_xp", { level: 12, rank: 1 })).toBe("rank");
    expect(newBest("health_xp", { level: 12, rank: 1 })).toBeNull();
    expect(newBest("health_xp", { level: 13, rank: 1 })).toBe("level");
    expect(newBest("health_xp", { level: 13, rank: 1 })).toBeNull();
  });

  it("keeps each domain apart, and reads storage it can't parse as nothing kept", () => {
    keepBest("health_xp", { level: 9, rank: 0 });
    keepBest("social_xp", { level: 3, rank: 0 });
    expect(keptBest("health_xp")).toEqual({ level: 9, rank: 0 });
    localStorage.setItem("lifeos.domains.best", "{not json");
    expect(keptBest("health_xp")).toBeNull();
    expect(newBest("health_xp", { level: 9, rank: 0 })).toBeNull();
  });
});

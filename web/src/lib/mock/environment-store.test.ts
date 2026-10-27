import { beforeEach, describe, expect, it } from "vitest";
import { addDays, todayIso, weekdayIndex } from "@/lib/dates";
import { HOME_PARTS } from "@/lib/environment";
import { homeOn, toUnits } from "@/lib/home-score";
import {
  SEEDED_WALK_THROUGHS,
  clearMockEnvironment,
  mockAddSpace,
  mockArchiveSpace,
  mockDeleteSpaceRating,
  mockEditSpaceRating,
  mockEnvironment,
  mockHomeDays,
  mockRateSpaces,
  mockSetSpace,
  mockSpaceRatings,
  mockSpaces,
  resetEnvironmentStore,
} from "@/lib/mock/environment-store";

const today = todayIso();

function spaceId(name: string): string {
  return mockSpaces().find((s) => s.name === name)!.id;
}

beforeEach(() => resetEnvironmentStore());

describe("the mock's home", () => {
  it("holds plan §2.1's nine spaces by name, their shares adding up to 100", () => {
    const spaces = mockSpaces();
    expect(spaces.map((s) => s.name)).toEqual([
      "Bedroom",
      "Exercise Room",
      "Garage",
      "Kitchen",
      "Living Area",
      "Master Bath",
      "Office",
      "Other Baths",
      "Stairway/Foyer/Halls",
    ]);
    expect(spaces.find((s) => s.name === "Office")).toMatchObject({ weight: 5, pace: 5, share: 18.5 });
    expect(spaces.reduce((sum, s) => sum + (s.share ?? 0), 0)).toBeCloseTo(100, 0);
  });

  it("seeds a month of Sunday walk-throughs that climb from messy, with quick checks between", () => {
    const { ratings } = mockSpaceRatings({ days: 60 });
    const sundays = new Set(ratings.filter((r) => weekdayIndex(r.day) === 0).map((r) => r.day));
    expect(sundays.size).toBe(SEEDED_WALK_THROUGHS);
    const checks = ratings.filter((r) => weekdayIndex(r.day) === 3);
    expect(checks.length).toBeGreaterThan(0);
    expect(new Set(checks.map((r) => r.space))).toEqual(new Set(["Office", "Kitchen"]));
    expect(checks.every((r) => r.aesthetics == null && r.tidiness != null && r.cleanliness != null)).toBe(true);

    const days = mockHomeDays(60).days;
    const [first, last] = [[...sundays].sort()[0], [...sundays].sort().at(-1)!];
    const scoreOn = (day: string) => days.find((d) => d.day === day)!.homeScore!;
    expect(scoreOn(last)).toBeGreaterThan(scoreOn(first) + 20);
  });

  it("is the same on every load", () => {
    const first = mockEnvironment();
    resetEnvironmentStore();
    expect(mockEnvironment()).toEqual(first);
  });

  it("scores today with the kernel's sums, and lists Fix next biggest first", () => {
    const home = mockEnvironment();
    expect(home.today).toBe(today);
    expect(home.settledThrough).toBe(addDays(today, -1));
    expect(home.homeScore).not.toBeNull();
    // Each space's parts add up, at their weights, to its score's drag on the home.
    const total = home.spaces.reduce((sum, s) => sum + (s.score == null ? 0 : s.weight), 0);
    for (const space of home.spaces.filter((s) => s.score != null)) {
      for (const p of HOME_PARTS) expect(space.parts[p].rated).not.toBeNull();
    }
    expect(total).toBe(27);
    expect(home.fixNext).toHaveLength(3);
    const drags = home.fixNext.map((x) => x.drag);
    expect([...drags].sort((a, b) => b - a)).toEqual(drags);
    // The garage was skipped on the last walk-through.
    const garage = home.spaces.find((s) => s.name === "Garage")!;
    expect(garage.lastRatedOn! < home.spaces.find((s) => s.name === "Office")!.lastRatedOn!).toBe(true);
    // The API's decimals come back to whole ten-thousandths.
    expect(Number.isInteger(toUnits(home.homeScore!))).toBe(true);
  });

  it("answers the Home Score's days with the exception days' comments, and pauses fades on them", () => {
    const yesterday = addDays(today, -1);
    const plain = mockHomeDays(30);
    expect(plain.days).toHaveLength(30);
    expect(plain.from).toBe(addDays(today, -29));
    expect(plain.days.at(-1)!.day).toBe(today);

    const marked = mockHomeDays(30, [{ day: yesterday, comment: "Away for the weekend" }]);
    expect(marked.days.find((d) => d.day === yesterday)!.exception).toBe("Away for the weekend");
    // A paused day keeps today's score at or above the unpaused one.
    expect(marked.days.at(-1)!.homeScore!).toBeGreaterThanOrEqual(plain.days.at(-1)!.homeScore!);
    expect(() => mockHomeDays(0)).toThrow("Ask for 1 to 731 days; got 0.");
  });

  it("has nothing to score once cleared", () => {
    clearMockEnvironment();
    const home = mockEnvironment();
    expect(home).toMatchObject({ homeScore: null, spaces: [], fixNext: [] });
    expect(homeOn([], [], [], today).score).toBeNull();
  });
});

describe("rating spaces", () => {
  it("saves a walk-through in one write and lifts the score", () => {
    const before = mockEnvironment().homeScore!;
    const result = mockRateSpaces(mockSpaces().map((s) => ({ space: s.id, tidiness: 5, cleanliness: 5, aesthetics: 5 })));
    expect(result.ratings).toHaveLength(9);
    expect(result).toMatchObject({ awards: [], progress: [] });
    expect(result.ratings[0]).toMatchObject({ day: today, tidiness: 5, cleanliness: 5, aesthetics: 5 });
    const after = mockEnvironment();
    expect(after.homeScore).toBe(100);
    expect(after.homeScore!).toBeGreaterThan(before);
    expect(after.fixNext).toEqual([]);
  });

  it("rates a space by name, leaving parts out, with a note", () => {
    const [row] = mockRateSpaces([{ space: "office", tidiness: 4.5, note: "  Desk clear  " }]).ratings;
    expect(row).toMatchObject({ space: "Office", tidiness: 4.5, cleanliness: null, aesthetics: null, note: "Desk clear" });
    expect(mockEnvironment().spaces.find((s) => s.name === "Office")!.parts.tidiness).toMatchObject({
      rated: 4.5,
      ratedOn: today,
      now: 87.5,
    });
  });

  it("repeats the last rating for Same as last time", () => {
    const office = mockEnvironment().spaces.find((s) => s.name === "Office")!;
    const [row] = mockRateSpaces([{ space: office.id, same: true }]).ratings;
    expect([row.tidiness, row.cleanliness, row.aesthetics]).toEqual(HOME_PARTS.map((p) => office.parts[p].rated));
  });

  it("refuses in the kernel's words, writing nothing", () => {
    const count = mockSpaceRatings().ratings.length;
    const office = spaceId("Office");
    expect(() => mockRateSpaces([])).toThrow("Rate at least one space.");
    expect(() => mockRateSpaces([{ space: office, tidiness: 4 }], addDays(today, 1))).toThrow(
      `A rating is for today or earlier; ${addDays(today, 1)} hasn't happened yet (today is ${today}).`,
    );
    expect(() => mockRateSpaces([{ space: office, tidiness: 3.25 }])).toThrow(
      "Tidiness is rated 1 to 5 in half points (1, 1.5 … 5); got 3.25.",
    );
    expect(() => mockRateSpaces([{ space: office, cleanliness: 6 }])).toThrow(
      "Cleanliness is rated 1 to 5 in half points (1, 1.5 … 5); got 6.",
    );
    expect(() => mockRateSpaces([{ space: office }])).toThrow(
      "Rate at least one part of 'Office': tidiness, cleanliness or aesthetics.",
    );
    expect(() => mockRateSpaces([{ space: office, tidiness: 4 }, { space: "Office", tidiness: 3 }])).toThrow(
      "'Office' is rated twice: rate each space once.",
    );
    expect(() => mockRateSpaces([{ space: office, tidiness: 4, same: true }])).toThrow(
      "'Same as last time' repeats Office's last rating, so it takes no new ones: leave them out.",
    );
    expect(() => mockRateSpaces([{ space: office, tidiness: 4, note: "x".repeat(141) }])).toThrow(
      "A rating's note is up to 140 characters; that one is 141.",
    );
    expect(() => mockRateSpaces([{ space: "Attic", tidiness: 4 }])).toThrow("No subject matches 'Attic'.");

    const attic = mockAddSpace({ name: "Attic" });
    expect(() => mockRateSpaces([{ space: attic.id, same: true }])).toThrow(
      `'Attic' hasn't been rated by ${today}, so there's nothing to repeat: rate it instead.`,
    );
    mockArchiveSpace(attic.urn, true);
    // A good rating ahead of the refused one isn't written either.
    expect(() => mockRateSpaces([{ space: office, tidiness: 5 }, { space: attic.id, tidiness: 4 }])).toThrow(
      "'Attic' is archived: bring it back before rating it.",
    );
    expect(mockSpaceRatings().ratings).toHaveLength(count);
  });

  it("edits and deletes a rating", () => {
    const [row] = mockRateSpaces([{ space: "Kitchen", tidiness: 3 }]).ratings;
    const [edited] = mockEditSpaceRating(row.id, { tidiness: 4, note: "Dishes done" }).ratings;
    expect(edited).toMatchObject({ id: row.id, tidiness: 4, note: "Dishes done" });
    expect(mockEditSpaceRating(row.id, { note: "" }).ratings[0].note).toBeNull();
    expect(() => mockEditSpaceRating(row.id, { day: addDays(today, 2) })).toThrow(/hasn't happened yet/);
    expect(mockDeleteSpaceRating(row.id).ratings).toEqual([]);
    expect(() => mockDeleteSpaceRating(row.id)).toThrow(`No rating has the id ${row.id} (it may have been deleted).`);
  });
});

describe("the spaces editor", () => {
  it("adds a space at weight and pace 3, and changes it", () => {
    const attic = mockAddSpace({ name: "  Attic " });
    expect(attic).toMatchObject({ name: "Attic", weight: 3, pace: 3, archivedOn: null });
    expect(attic.share).toBeCloseTo(10, 0);
    expect(mockSetSpace(attic.id, { name: "Loft", weight: 2, pace: 1 })).toMatchObject({ name: "Loft", weight: 2, pace: 1 });
  });

  it("refuses in the kernel's words", () => {
    expect(() => mockAddSpace({ name: " " })).toThrow("Give the space a name, e.g. Office.");
    expect(() => mockAddSpace({ name: "office" })).toThrow("You already have a space called 'Office'.");
    expect(() => mockAddSpace({ name: "Attic", weight: 11 })).toThrow(
      "A space's weight is a whole number from 1 to 10; got 11.",
    );
    expect(() => mockAddSpace({ name: "Attic", pace: 0 })).toThrow(
      "A space's pace is a whole number from 1 (slow to get messy) to 5 (fast); got 0.",
    );
    expect(() => mockAddSpace({ name: "x".repeat(61) })).toThrow("A space's name is up to 60 characters; that one is 61.");
    expect(() => mockSetSpace(spaceId("Office"), {})).toThrow("Say what to change: a name, a weight or a pace.");
    expect(() => mockSetSpace(spaceId("Office"), { name: "Kitchen" })).toThrow("You already have a space called 'Kitchen'.");
  });

  it("archives a space out of the Home Score from today, and brings it back", () => {
    const garage = mockSpaces().find((s) => s.name === "Garage")!;
    expect(mockArchiveSpace("urn:bsk:task:not-a-space", true)).toBe(false);
    expect(mockArchiveSpace(garage.urn, true)).toBe(true);
    const archived = mockSpaces().find((s) => s.id === garage.id)!;
    expect(archived).toMatchObject({ archivedOn: today, share: null });
    expect(mockEnvironment().spaces.some((s) => s.id === garage.id)).toBe(false);
    // Yesterday it still counted.
    const days = mockHomeDays(2).days;
    expect(days[0].spaces.some((s) => s.spaceId === garage.id)).toBe(true);
    expect(days[1].spaces.some((s) => s.spaceId === garage.id)).toBe(false);

    mockArchiveSpace(garage.urn, false);
    expect(mockEnvironment().spaces.some((s) => s.id === garage.id)).toBe(true);
  });
});

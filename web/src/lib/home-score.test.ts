import { describe, expect, it } from "vitest";
import {
  FLOOR_POINTS,
  fixNext,
  HOME_PARTS,
  HOME_RULES,
  homeDays,
  homeOn,
  lastRatedOn,
  partValue,
  ratingPoints,
  shownPoints,
  toPoints,
  toUnits,
  type HomeDay,
  type HomePart,
  type HomeRating,
  type HomeSpace,
} from "@/lib/home-score";

// The same worked examples as the kernel's HomeScoreTests, to the digit.
const D0 = "2026-10-01";

function day(offset: number): string {
  const d = new Date(`${D0}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + offset);
  return d.toISOString().slice(0, 10);
}

function space(id: string, weight: number, pace: number, archivedOn: string | null = null): HomeSpace {
  return { id, weight, pace, archivedOn };
}

let next = 0;
function rating(s: HomeSpace, on: string, t: number | null, c: number | null, a: number | null, minute = 0): HomeRating {
  return {
    id: `r${++next}`,
    spaceId: s.id,
    day: on,
    tidiness: t,
    cleanliness: c,
    aesthetics: a,
    ratedAt: new Date(minute * 60_000).toISOString(),
  };
}

function of(d: HomeDay, s: HomeSpace) {
  const found = d.spaces.find((x) => x.spaceId === s.id);
  if (!found) throw new Error(`${s.id} isn't on ${d.day}`);
  return found;
}

function part(d: HomeDay, s: HomeSpace, p: HomePart) {
  return of(d, s).parts.find((x) => x.part === p)!;
}

describe("HOME_RULES", () => {
  it("hold together", () => {
    expect(HOME_PARTS.reduce((sum, p) => sum + HOME_RULES.weights[p], 0)).toBe(100);
    expect(HOME_RULES.fadeDays.tidiness).toBeLessThan(HOME_RULES.fadeDays.cleanliness);
    expect(HOME_RULES.fadeDays.cleanliness).toBeLessThan(HOME_RULES.fadeDays.aesthetics);
    expect(ratingPoints(1)).toBe(0);
    expect(ratingPoints(5)).toBe(HOME_RULES.full);
    expect(FLOOR_POINTS).toBe(250_000);
    expect(ratingPoints(1.5)).toBe(HOME_RULES.halfStep);
  });
});

describe("partValue", () => {
  it("fades by the space's pace down to the floor", () => {
    // Tidiness at pace 5: half a point every 3 × 3/5 days, so 69,444 a day.
    expect(partValue("tidiness", 4.5, 5, 1)).toBe(805_556);
    expect(partValue("tidiness", 4.5, 1, 2)).toBe(847_223);
    // Cleanliness at pace 3: half a point a week.
    expect(partValue("cleanliness", 4, 3, 7)).toBe(625_000);
    // Aesthetics ignores pace: half a point in 90 days either way.
    expect(partValue("aesthetics", 5, 5, 90)).toBe(875_000);
    expect(partValue("aesthetics", 5, 1, 90)).toBe(875_000);
    // Never below a rating of 2, and a rating below 2 doesn't fade.
    expect(partValue("tidiness", 5, 3, 100)).toBe(250_000);
    expect(partValue("tidiness", 2, 5, 100)).toBe(250_000);
    expect(partValue("tidiness", 1.5, 5, 100)).toBe(125_000);
    expect(partValue("cleanliness", 1, 5, 100)).toBe(0);
    expect(partValue("tidiness", 5, 5, 0)).toBe(1_000_000);
  });
});

describe("homeDays", () => {
  it("scores the worked example's two spaces to the digit", () => {
    const office = space("office", 5, 5);
    const kitchen = space("kitchen", 4, 5);
    const ratings = [rating(office, D0, 4.5, 4, 3.5), rating(kitchen, D0, 3, 2.5, 3)];

    const days = homeDays([kitchen, office], ratings, [], D0, day(10));
    expect(days).toHaveLength(11);

    // Day 0: as rated.
    expect(of(days[0], office).score).toBe(781_250);
    expect(of(days[0], kitchen).score).toBe(456_250);
    expect(days[0].score).toBe(636_805);
    // Day 1 is the grace day; day 2 is the first fade.
    expect(days[1].score).toBe(636_805);
    expect(part(days[2], office, "tidiness").value).toBe(805_556);

    // Day 10: nine days of fade.
    const day10 = days[10];
    expect(of(day10, office).parts.map((p) => p.value)).toEqual([250_000, 482_143, 612_500]);
    expect(of(day10, kitchen).parts.map((p) => p.value)).toEqual([250_000, 250_000, 487_500]);
    expect(of(day10, office).score).toBe(403_750);
    expect(of(day10, kitchen).score).toBe(297_500);
    expect(day10.score).toBe(356_527);
    expect(of(day10, office).parts.map((p) => p.drag)).toEqual([187_500, 100_694, 43_055]);
    expect(of(day10, kitchen).parts.map((p) => p.drag)).toEqual([150_000, 116_666, 45_555]);
    expect(fixNext(day10).map((x) => [x.spaceId, x.part.part, x.part.drag])).toEqual([
      ["office", "tidiness", 187_500],
      ["kitchen", "tidiness", 150_000],
      ["kitchen", "cleanliness", 116_666],
    ]);

    // Each part says what it was rated and when.
    const tidiness = part(day10, office, "tidiness");
    expect([tidiness.rated, tidiness.ratedOn]).toEqual([4.5, D0]);
    expect(lastRatedOn(of(day10, office))).toBe(D0);
  });

  it("pauses fades on exception days, and the rating's own day never counts", () => {
    const office = space("office", 5, 5);
    const ratings = [rating(office, D0, 4.5, 4, 3.5)];

    // Days 2 and 3 marked: day 3 hasn't faded yet, and day 4 has faded one day.
    const marked = homeDays([office], ratings, [day(2), day(3)], D0, day(4));
    expect(part(marked[3], office, "tidiness").value).toBe(875_000);
    expect(part(marked[4], office, "tidiness").value).toBe(805_556);

    // An exception on the rating's own day changes nothing.
    const onTheDay = homeDays([office], ratings, [D0], day(2), day(2));
    expect(part(onTheDay[0], office, "tidiness").value).toBe(805_556);
  });

  it("counts a space once all three parts are rated, and until it's archived", () => {
    const office = space("office", 5, 5);
    const kitchen = space("kitchen", 4, 5, day(2));
    const ratings = [
      // A quick check leaves Aesthetics out; it's rated the next day.
      rating(office, D0, 4, 4, null),
      rating(office, day(1), null, null, 3),
      // On the same day, the rating written later wins.
      rating(kitchen, D0, 5, null, null, 2),
      rating(kitchen, D0, 3, 3, 3, 1),
    ];

    const days = homeDays([kitchen, office], ratings, [], D0, day(2));

    expect(of(days[0], office).score).toBeNull();
    expect(part(days[0], office, "tidiness").drag).toBe(0);
    expect(part(days[0], office, "aesthetics").value).toBeNull();
    expect(of(days[0], kitchen).score).toBe(725_000);
    expect(days[0].score).toBe(725_000);

    expect(of(days[1], office).score).toBe(700_000);
    expect(days[1].score).toBe(711_111);

    // From the day it's archived, the kitchen leaves.
    expect(days[2].spaces.map((s) => s.spaceId)).toEqual(["office"]);
    expect(days[2].score).toBe(658_333);
  });

  it("gives no score without ratings", () => {
    const office = space("office", 5, 5);
    const d = homeOn([office], [], [], D0);
    expect(d.score).toBeNull();
    expect(lastRatedOn(of(d, office))).toBeNull();
    expect(fixNext(d)).toEqual([]);
    expect(homeDays([office], [], [], D0, day(-1))).toEqual([]);
  });

  it("keeps the order the ratings were given in when they tie on day and time", () => {
    const office = space("office", 5, 5);
    const first = rating(office, D0, 2, 2, 2);
    const second = rating(office, D0, 4, 4, 4);
    // Same day, same moment: the later id wins, whichever order they arrive in.
    expect(homeOn([office], [second, first], [], D0).score).toBe(homeOn([office], [first, second], [], D0).score);
  });
});

describe("shownPoints", () => {
  it("floors to a tenth, as the kernel shows", () => {
    expect(shownPoints(toPoints(636_805))).toBe("63.6");
    expect(shownPoints(toPoints(100_694))).toBe("10.0");
    expect(shownPoints(toPoints(HOME_RULES.full))).toBe("100.0");
    expect(shownPoints(0)).toBe("0.0");
    // The API's decimals come back to the same whole units.
    expect(toUnits(63.6805)).toBe(636_805);
    expect(shownPoints(35.6527)).toBe("35.6");
  });
});

import { describe, expect, it } from "vitest";
import { exerciseXp, sleepXp, stepsXp } from "@/lib/activity-score";
import { ENVIRONMENT, HEALTH, levelFor, totalFor } from "@/lib/domain-fold";
import { exerciseVerdict } from "@/lib/exercise-rules";
import { fade, partDrag, partValue, ratingPoints, spaceScore } from "@/lib/home-score";
import type { HomePart, ReviewKind } from "@/lib/production-ui-types";
import { reviewPeriod } from "@/lib/reviews";

/**
 * The TypeScript copies of kernel rules give the kernel's answers (code review CR8). The cases
 * and the answers are in `tests/rules/*.json`, which `RuleParityTests` checks against the C#
 * rules; a rule changed on one side only fails one of the two.
 */
interface RuleCase {
  rule: string;
  args: unknown[];
  out: unknown;
}

interface RuleFile {
  about: string;
  cases: RuleCase[];
}

const files = import.meta.glob<RuleFile>("../../../tests/rules/*.json", { eager: true, import: "default" });

const n = (v: unknown) => v as number;
const s = (v: unknown) => v as string;
const curve = (domain: unknown) => (domain === "health" ? HEALTH : ENVIRONMENT).curve;

const rules: Record<string, Record<string, (...args: unknown[]) => unknown>> = {
  activity: {
    stepsXp: (steps) => stepsXp(n(steps)),
    sleepXp: (score) => sleepXp(n(score)),
    exerciseXp: (seconds) => exerciseXp(n(seconds)),
    exerciseVerdict: (source, activity) => exerciseVerdict(source as string | null, activity as string | null),
  },
  home: {
    points: (rating) => ratingPoints(n(rating)),
    fade: (part, pace, days) => fade(s(part) as HomePart, n(pace), n(days)),
    partValue: (part, rating, pace, days) => partValue(s(part) as HomePart, n(rating), n(pace), n(days)),
    spaceScore: (t, c, a) => spaceScore(n(t), n(c), n(a)),
    partDrag: (weight, part, value, total) => partDrag(n(weight), s(part) as HomePart, n(value), n(total)),
  },
  reviews: {
    period: (kind, day) => {
      const { date, start, end, planStart, planEnd } = reviewPeriod(s(kind) as ReviewKind, s(day));
      return { date, start, end, planStart, planEnd };
    },
  },
  levels: {
    totalFor: (domain, level) => totalFor(curve(domain), n(level)),
    levelFor: (domain, xp) => levelFor(curve(domain), n(xp)),
  },
};

function answer(file: string, c: RuleCase): unknown {
  const rule = rules[file]?.[c.rule];
  if (!rule) throw new Error(`tests/rules/${file}.json names a rule this test doesn't know: ${c.rule}.`);
  try {
    return rule(...c.args);
  } catch (e) {
    if (e instanceof RangeError) return { throws: true };
    throw e;
  }
}

describe("the web's copies of kernel rules", () => {
  it("finds the shared cases", () => {
    expect(Object.keys(files).map((path) => path.split("/").pop()).sort()).toEqual(
      ["activity.json", "home.json", "levels.json", "reviews.json"],
    );
  });

  for (const [path, doc] of Object.entries(files)) {
    const file = path.split("/").pop()!.replace(/\.json$/, "");
    it(`gives the kernel's answers in tests/rules/${file}.json`, () => {
      const wrong = doc.cases
        .map((c) => ({ c, actual: answer(file, c) }))
        .filter(({ c, actual }) => JSON.stringify(actual) !== JSON.stringify(c.out))
        .map(({ c, actual }) => `${c.rule}(${JSON.stringify(c.args)}) is ${JSON.stringify(actual)}, not ${JSON.stringify(c.out)}`);
      expect(wrong).toEqual([]);
    });
  }
});

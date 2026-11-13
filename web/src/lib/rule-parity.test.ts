import { describe, expect, it } from "vitest";
import { exerciseXp, sleepXp, stepsXp } from "@/lib/activity-score";
import { caloriesPartFor, drinkFactorFor, fastingFor, guessMealType, isScore, scoreDay } from "@/lib/diet-score";
import { ENVIRONMENT, HEALTH, levelFor, totalFor } from "@/lib/domain-fold";
import { exerciseVerdict } from "@/lib/exercise-rules";
import { fade, partDrag, partValue, ratingPoints, spaceScore } from "@/lib/home-score";
import { STATUS_BY_TYPE, initialStatus, isTerminal } from "@/lib/production-ui-types";
import type { HomePart, ReviewKind, SubjectType } from "@/lib/production-ui-types";
import { reviewPeriod } from "@/lib/reviews";
import { addDays } from "@/lib/dates";
import { isOccurrenceDate, parseRecurrence } from "@/lib/recurrence";
import { DIASTOLIC_KEY, SYSTOLIC_KEY, WEIGHT_KEY } from "@/lib/measurements";
import { METRIC_CATALOG } from "@/lib/mock/metric-catalog";
import { CURRENCIES } from "@/lib/rewards";

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
  diet: {
    isScore: (score) => isScore(n(score)),
    caloriesPart: (calories) => caloriesPartFor(n(calories)),
    fasting: (seconds) => fastingFor(n(seconds)),
    drinkFactor: (drinks) => drinkFactorFor(n(drinks)),
    score: (calories, scoreCalories, fast, drinks) => {
      const day = scoreDay({
        calories: n(calories),
        scoreCalories: n(scoreCalories),
        longestFastSeconds: fast as number | null,
        drinks: n(drinks),
      });
      return { foodAward: day.foodAward, fastAward: day.fastAward, fastAwardUncut: day.fastAwardUncut, total: day.total };
    },
    mealType: (hour) => guessMealType(new Date(2026, 0, 5, n(hour), 30)),
  },
  recurrence: {
    // The kernel answers with bsk.recurrence_occurrences (RecurrenceTests); the web says day by
    // day whether each date is one.
    occurrences: (recur, anchor, from, to) => {
      const spec = parseRecurrence(JSON.stringify(recur));
      if (!spec) throw new Error(`tests/rules/recurrence.json has a pattern the web can't read: ${JSON.stringify(recur)}.`);
      const dates: string[] = [];
      for (let day = s(from); day <= s(to); day = addDays(day, 1)) {
        if (isOccurrenceDate(spec, day, s(anchor))) dates.push(day);
      }
      return dates;
    },
  },
  catalogs: {
    // The keys, labels and order the web copies; the symbols and hints are its own.
    currencies: () =>
      CURRENCIES.map((c) => ({ key: c.key, label: c.label, spendable: c.spendable, lifeDomain: c.group === "domain" })),
    // The mock's copy: the live app reads the kernel's from GET /api/measurements/catalog.
    metricCatalog: () =>
      METRIC_CATALOG.map((t) => ({
        key: t.key,
        name: t.name,
        unit: t.unit,
        decimals: t.decimals,
        better: t.better,
        low: t.low,
        high: t.high,
        panel: t.panel,
      })),
  },
  statuses: {
    statuses: (type) => STATUS_BY_TYPE[s(type) as SubjectType] ?? [],
    initial: (type) => initialStatus(s(type) as SubjectType),
    terminal: (status) => isTerminal(status as string | null),
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
      [
        "activity.json",
        "catalogs.json",
        "diet.json",
        "home.json",
        "levels.json",
        "recurrence.json",
        "reviews.json",
        "statuses.json",
      ],
    );
  });

  it("names only keys on the kernel's starter list", () => {
    const doc = Object.entries(files).find(([path]) => path.endsWith("/catalogs.json"))![1];
    const kernel = doc.cases.find((c) => c.rule === "metricCatalog")!.out as { key: string }[];
    expect([WEIGHT_KEY, SYSTOLIC_KEY, DIASTOLIC_KEY].filter((key) => !kernel.some((t) => t.key === key))).toEqual([]);
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

import { describe, expect, it } from "vitest";
import { matchSubjects } from "./subject-search";

const titles = ["Water the garden", "Buy a hose", "Garden shed", "Paint", "garden party"];
const byTitle = (t: string) => t;

describe("matchSubjects", () => {
  it("matches anywhere, ignoring case, with labels that start with the words first", () => {
    expect(matchSubjects(titles, "GARDEN", byTitle, 10).shown).toEqual([
      "garden party",
      "Garden shed",
      "Water the garden",
    ]);
  });

  it("shows at most the limit and counts the rest", () => {
    const many = Array.from({ length: 75 }, (_, i) => `Task ${i}`);
    const result = matchSubjects(many, "", byTitle, 50);
    expect(result.shown).toHaveLength(50);
    expect(result.more).toBe(25);
    // Past the first page is a search away, not out of reach.
    expect(matchSubjects(many, "task 74", byTitle, 50).shown).toEqual(["Task 74"]);
  });

  it("keeps the given order when nothing is typed", () => {
    expect(matchSubjects(titles, "  ", byTitle, 10).shown).toEqual(titles);
  });
});

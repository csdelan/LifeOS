import { describe, expect, it } from "vitest";
import { normalizeTag, splitTags, suggestTags } from "@/lib/tag-suggest";

const universe = [
  { tag: "health", itemCount: 3 },
  { tag: "focus", itemCount: 7 },
  { tag: "deep-focus", itemCount: 9 },
  { tag: "family", itemCount: 1 },
];

describe("tag normalization", () => {
  it("matches the kernel: trimmed, lowercased, no leading #", () => {
    expect(normalizeTag("  #Health ")).toBe("health");
  });

  it("splits comma text into distinct tags", () => {
    expect(splitTags("Health, #health,  focus ,,")).toEqual(["health", "focus"]);
  });
});

describe("suggestTags", () => {
  it("offers the most-used tags before anything is typed", () => {
    expect(suggestTags(universe, "").map((t) => t.tag)).toEqual(["deep-focus", "focus", "health", "family"]);
  });

  it("puts tags that start with the text ahead of ones that only contain it", () => {
    expect(suggestTags(universe, "Fo").map((t) => t.tag)).toEqual(["focus", "deep-focus"]);
  });

  it("never offers a tag the item already has", () => {
    expect(suggestTags(universe, "", ["#Focus"]).map((t) => t.tag)).not.toContain("focus");
  });

  it("caps the list", () => {
    expect(suggestTags(universe, "", [], 2)).toHaveLength(2);
  });
});

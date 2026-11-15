import { describe, expect, it } from "vitest";
import { TONES } from "@/lib/tones";

/**
 * Every card and every section of an item's detail is a `surface` in a tone (index.css,
 * `lib/tones.ts`), so they stand out the same way against the page and their colour says what
 * they hold. A card built by hand from the old classes, rounded-xl and a faint ring, was the same
 * colour as the page in the dark; these find one coming back, and a tone that isn't one.
 */
const sources = import.meta.glob<string>(["/src/**/*.tsx", "!/src/**/*.test.tsx"], {
  query: "?raw",
  import: "default",
  eager: true,
});

/** Boxes with the old card's classes that aren't cards, and why. */
const NOT_CARDS: Record<string, string> = {
  "/src/components/activity/activity-banner.tsx": "the Activity banner is a picture with its own sky, not a card",
};

/** Each quoted class string in `text`. */
function classStrings(text: string): string[] {
  return [...text.matchAll(/"([^"\n]*)"|`([^`]*)`/g)].map((m) => m[1] ?? m[2]);
}

describe("cards and sections are surfaces", () => {
  it("builds no card by hand from rounded-xl and a faint ring", () => {
    const handBuilt = Object.entries(sources)
      .filter(([path]) => !(path in NOT_CARDS))
      .filter(([, text]) =>
        classStrings(text).some((s) => {
          const classes = s.split(/\s+/);
          // A dialog or a menu floats over the page on the popover's colour; it isn't a card.
          return classes.includes("rounded-xl") && classes.includes("ring-foreground/10") && !classes.includes("bg-popover");
        }),
      )
      .map(([path]) => path)
      .sort();
    expect(handBuilt).toEqual([]);
  });

  it("names only tones there are", () => {
    const named = Object.entries(sources).flatMap(([path, text]) =>
      [...text.matchAll(/data-tone=(?:"([^"]*)"|\{([^}]*)\})/g)].flatMap((m) =>
        m[1] !== undefined ? [[path, m[1]]] : [...m[2].matchAll(/"([^"]*)"/g)].map((q) => [path, q[1]]),
      ),
    );
    expect(named.length).toBeGreaterThan(40);
    expect(named.filter(([, tone]) => !(TONES as readonly string[]).includes(tone))).toEqual([]);
  });

  it("keeps each exception's reason current", () => {
    for (const path of Object.keys(NOT_CARDS)) expect(Object.keys(sources)).toContain(path);
  });
});

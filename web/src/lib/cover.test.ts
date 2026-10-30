import { describe, expect, it } from "vitest";
import {
  canHaveCover,
  clampPosition,
  coverQuery,
  coverSearchMiss,
  coverSrc,
  coverSrcSet,
  creditText,
  dragPosition,
  keyPosition,
  nextCoverPage,
  normalizeCover,
  normalizeCoverSearch,
  objectPosition,
  positionText,
} from "@/lib/cover";
import type { CoverSearchRead } from "@/lib/production-ui-types";

const RAW = "https://images.unsplash.com/photo-1417325384643-aac51acc9e5d?ixid=M3w2NTQ3";

function page(over: Partial<CoverSearchRead> = {}): CoverSearchRead {
  return {
    configured: true,
    query: "mountain",
    page: 1,
    total: 30,
    totalPages: 2,
    unsplashLink: "https://unsplash.com/?utm_source=lifeos&utm_medium=referral",
    photos: [
      {
        id: "a",
        thumbUrl: RAW,
        color: "#112233",
        width: 3000,
        height: 2000,
        photographer: "Joe",
        photographerLink: "",
      },
    ],
    ...over,
  };
}

describe("which subjects have covers", () => {
  it("is identity statements, goals, projects and habits only", () => {
    expect(canHaveCover("Value")).toBe(true);
    expect(canHaveCover("Goal")).toBe(true);
    expect(canHaveCover("Project")).toBe(true);
    expect(canHaveCover("Task")).toBe(false);
    expect(canHaveCover("Habit")).toBe(true);
    expect(canHaveCover("Season")).toBe(false);
    expect(canHaveCover(undefined)).toBe(false);
  });
});

describe("sizing a photo", () => {
  it("asks Unsplash for the width, keeping its ixid", () => {
    const src = new URL(coverSrc(RAW, 900));
    expect(src.hostname).toBe("images.unsplash.com");
    expect(src.searchParams.get("ixid")).toBe("M3w2NTQ3");
    expect(src.searchParams.get("w")).toBe("900");
    expect(src.searchParams.get("q")).toBe("75");
    expect(src.searchParams.get("auto")).toBe("format");
    expect(src.searchParams.get("fit")).toBe("max");
  });

  it("gives a 1x and 2x srcset", () => {
    const set = coverSrcSet(RAW, 400)!;
    expect(set).toMatch(/w=400.* 1x, .*w=800.* 2x$/);
  });

  it("uses anything not on Unsplash's image host as it is", () => {
    for (const url of ["/src/lib/mock/covers/meadow.svg", "data:image/svg+xml,%3csvg", "http://images.unsplash.com/x", "https://images.unsplash.com.evil.example/x"]) {
      expect(coverSrc(url, 900)).toBe(url);
      expect(coverSrcSet(url, 900)).toBeUndefined();
    }
  });
});

describe("positions", () => {
  it("clamps to whole numbers from 0 to 100, the middle when it isn't a number", () => {
    expect(clampPosition(-4)).toBe(0);
    expect(clampPosition(104)).toBe(100);
    expect(clampPosition(33.6)).toBe(34);
    expect(clampPosition(Number.NaN)).toBe(50);
    expect(objectPosition(30)).toBe("50% 30%");
  });

  it("moves the photo the way the arrow points, 5 a press", () => {
    expect(keyPosition(50, "ArrowUp")).toBe(55);
    expect(keyPosition(50, "ArrowDown")).toBe(45);
    expect(keyPosition(98, "ArrowUp")).toBe(100);
    expect(keyPosition(50, "PageUp")).toBe(70);
    expect(keyPosition(50, "Home")).toBe(0);
    expect(keyPosition(50, "End")).toBe(100);
    expect(keyPosition(50, "Enter")).toBeNull();
  });

  it("follows a drag by the part of the photo hidden above and below", () => {
    // A 3:2 photo in a 600 x 144 box is 400 px tall: 256 px are hidden.
    const box = { boxWidth: 600, boxHeight: 144, imageWidth: 3000, imageHeight: 2000 };
    expect(dragPosition({ ...box, startY: 50, dy: -128 })).toBe(100);
    expect(dragPosition({ ...box, startY: 50, dy: 64 })).toBe(25);
    expect(dragPosition({ ...box, startY: 50, dy: 0 })).toBe(50);
  });

  it("doesn't move a photo no taller than its box", () => {
    expect(dragPosition({ startY: 40, dy: 80, boxWidth: 600, boxHeight: 144, imageWidth: 3000, imageHeight: 500 })).toBe(40);
    expect(dragPosition({ startY: 40, dy: 80, boxWidth: 0, boxHeight: 0, imageWidth: 0, imageHeight: 0 })).toBe(40);
  });

  it("says what the banner shows", () => {
    expect(positionText(0)).toBe("Showing the top of the photo");
    expect(positionText(100)).toBe("Showing the bottom of the photo");
    expect(positionText(35)).toBe("Showing the photo 35% of the way down");
  });
});

describe("the picker's words", () => {
  it("starts from the title, squeezed and cut to 100 characters", () => {
    expect(coverQuery("  Run   a half-marathon ")).toBe("Run a half-marathon");
    expect(coverQuery("x".repeat(140))).toHaveLength(100);
  });

  it("says when Unsplash isn't set up, and when nothing matched", () => {
    expect(coverSearchMiss(undefined)).toBeNull();
    expect(coverSearchMiss(page())).toBeNull();
    expect(coverSearchMiss(page({ configured: false, photos: [] }))).toMatch(/^Unsplash isn't set up on this server\./);
    expect(coverSearchMiss(page({ query: "xyz", photos: [] }))).toBe("No photos for 'xyz'. Try fewer or simpler words.");
  });

  it("asks for the next page until the last is in", () => {
    expect(nextCoverPage(page())).toBe(2);
    expect(nextCoverPage(page({ page: 2 }))).toBeUndefined();
    expect(nextCoverPage(page({ configured: false }))).toBeUndefined();
  });

  it("credits a thumbnail in words", () => {
    expect(creditText({ photographer: "Annie Spratt" })).toBe("Photo by Annie Spratt on Unsplash");
  });
});

describe("reading the API", () => {
  it("makes numbers numbers and keeps a cover it can show", () => {
    const cover = normalizeCover({
      source: "unsplash",
      photoId: "Dwu85P9SOIk",
      url: RAW,
      color: "#6E633A",
      width: "2448",
      height: 3264,
      photographer: "Joe Example",
      photographerLink: "https://unsplash.com/@joe?utm_source=lifeos&utm_medium=referral",
      photoLink: "https://unsplash.com/photos/x?utm_source=lifeos&utm_medium=referral",
      unsplashLink: "https://unsplash.com/?utm_source=lifeos&utm_medium=referral",
      positionY: "30",
      setAt: "2026-10-23T18:04:00Z",
    })!;
    expect(cover.width).toBe(2448);
    expect(cover.positionY).toBe(30);
    expect(cover.color).toBe("#6e633a");
    expect(cover.alt).toBeNull();
  });

  it("reads a cover with no image or credit as none", () => {
    expect(normalizeCover(null)).toBeNull();
    expect(normalizeCover({ url: RAW })).toBeNull();
    expect(normalizeCover({ photographer: "Joe" })).toBeNull();
    expect(normalizeCover({ url: RAW, photographer: "Joe", color: "red", positionY: "x" })).toMatchObject({
      color: "#808080",
      positionY: 50,
    });
  });

  it("leaves out search results it can't show", () => {
    const read = normalizeCoverSearch({
      configured: true,
      query: "hills",
      page: "1",
      total: "3",
      totalPages: "1",
      photos: [{ id: "a", thumbUrl: RAW }, { id: "b" }, null],
    });
    expect(read.page).toBe(1);
    expect(read.total).toBe(3);
    expect(read.photos.map((p) => p.id)).toEqual(["a"]);
    expect(normalizeCoverSearch(undefined)).toMatchObject({ configured: false, photos: [] });
  });
});

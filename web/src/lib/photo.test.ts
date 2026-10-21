import { describe, expect, it } from "vitest";
import { fitWithin, PHOTO_MAX_EDGE } from "@/lib/photo";

describe("fitWithin", () => {
  it("scales the long side to 1,200 px, keeping the shape", () => {
    expect(fitWithin(4032, 3024)).toEqual({ width: PHOTO_MAX_EDGE, height: 900 });
    expect(fitWithin(3024, 4032)).toEqual({ width: 900, height: PHOTO_MAX_EDGE });
    expect(fitWithin(5000, 5000)).toEqual({ width: 1200, height: 1200 });
  });

  it("never enlarges a small photo, and never reaches zero", () => {
    expect(fitWithin(640, 480)).toEqual({ width: 640, height: 480 });
    expect(fitWithin(12000, 3)).toEqual({ width: 1200, height: 1 });
  });
});

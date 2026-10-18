import { describe, expect, it } from "vitest";
import { borderPoint, boxCenter, type Box } from "@/lib/graph-geometry";

const card: Box = { x: 0, y: 0, width: 200, height: 80 };

describe("borderPoint", () => {
  it("leaves through the side wall when the other card is beside it", () => {
    expect(borderPoint(card, { x: 500, y: 40 })).toEqual({ x: 200, y: 40 });
    expect(borderPoint(card, { x: -500, y: 40 })).toEqual({ x: 0, y: 40 });
  });

  it("leaves through the top or bottom when the other card is above or below", () => {
    expect(borderPoint(card, { x: 100, y: -300 })).toEqual({ x: 100, y: 0 });
    expect(borderPoint(card, { x: 100, y: 300 })).toEqual({ x: 100, y: 80 });
  });

  it("stays on the line between the two centres on a diagonal", () => {
    // Centre (100, 40) toward (400, 340): 45°, so it meets the bottom wall first.
    expect(borderPoint(card, { x: 400, y: 340 })).toEqual({ x: 140, y: 80 });
  });

  it("pushes the point out by the gap", () => {
    expect(borderPoint(card, { x: 500, y: 40 }, 4)).toEqual({ x: 204, y: 40 });
  });

  it("returns the centre when both centres coincide", () => {
    expect(borderPoint(card, boxCenter(card))).toEqual({ x: 100, y: 40 });
  });
});

import { describe, expect, it } from "vitest";
import { isDetailDirty } from "@/lib/subject-forms";

describe("read-only → edit dirty logic", () => {
  it("is never dirty while read-only", () => {
    expect(isDetailDirty(false, true)).toBe(false);
    expect(isDetailDirty(false, false)).toBe(false);
  });

  it("is dirty only after Edit when the form has changes", () => {
    expect(isDetailDirty(true, false)).toBe(false);
    expect(isDetailDirty(true, true)).toBe(true);
  });
});

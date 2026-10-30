import { beforeEach, describe, expect, it } from "vitest";
import { MOCK_PAGE_SIZE, MOCK_SCENES, mockSearchScenes } from "@/lib/mock/covers";
import { mockReads } from "@/lib/mock/api";
import {
  applyAttrs,
  getState,
  mockClearCover,
  mockMoveCover,
  mockSetCover,
  resetStore,
} from "@/lib/mock/store";

function storedCover(id: string): Record<string, unknown> | undefined {
  const attrs = JSON.parse(getState().details[id]?.attributes ?? "{}") as Record<string, unknown>;
  return attrs.cover as Record<string, unknown> | undefined;
}

describe("the mock's Unsplash search", () => {
  it("finds the scenes whose keywords match the words", () => {
    const read = mockSearchScenes("  forest   walk ");
    expect(read.configured).toBe(true);
    expect(read.query).toBe("forest walk");
    expect(read.photos.map((p) => p.id)).toEqual(["mock-mountain-dawn", "mock-forest-path"]);
    expect(read.photos[0].photographerLink).toContain("utm_source=lifeos&utm_medium=referral");
  });

  it("falls back to every scene, a page at a time", () => {
    const first = mockSearchScenes("zzz qqq");
    expect(first.total).toBe(MOCK_SCENES.length);
    expect(first.totalPages).toBe(2);
    expect(first.photos).toHaveLength(MOCK_PAGE_SIZE);
    const second = mockSearchScenes("zzz qqq", 2);
    expect(second.photos).toHaveLength(MOCK_SCENES.length - MOCK_PAGE_SIZE);
  });

  it("refuses a blank or long search, and a page out of range, in the server's words", () => {
    expect(() => mockSearchScenes("   ")).toThrow("Say what to search for, e.g. mountain sunrise.");
    expect(() => mockSearchScenes("a".repeat(101))).toThrow("Search for at most 100 characters; got 101.");
    expect(() => mockSearchScenes("hills", 51)).toThrow("Pages run from 1 to 50; got 51.");
  });
});

describe("the mock's covers", () => {
  beforeEach(() => resetStore());

  it("seeds a cover on an identity statement, a goal and a project, read with its credit links", async () => {
    for (const id of ["value-steward", "goal-lifeos", "proj-web"]) {
      const detail = await mockReads.getSubject(id);
      expect(detail?.cover?.photographer).toBeTruthy();
      expect(detail?.cover?.photoLink).toContain("utm_medium=referral");
      expect((await mockReads.getListItem(id))?.cover?.url).toBe(detail?.cover?.url);
    }
    expect((await mockReads.getSubject("goal-half"))?.cover).toBeNull();
  });

  it("sets a scene by id, moves it, and clears it", async () => {
    const set = mockSetCover({ subject: "goal-half", photoId: "mock-ocean-calm", positionY: 30 });
    expect(set.cover.photographer).toBe("Teo Marin");
    expect(set.cover.positionY).toBe(30);
    expect(set.downloadCounted).toBe(true);
    expect(storedCover("goal-half")).toMatchObject({ photo_id: "mock-ocean-calm", position_y: 30 });

    const moved = mockMoveCover("goal-half", 75);
    expect(moved.cover.positionY).toBe(75);
    expect(moved.downloadCounted).toBe(false);
    const goals = await mockReads.listSubjects({ type: "Goal" });
    expect(goals.find((g) => g.id === "goal-half")?.cover?.positionY).toBe(75);

    expect(mockClearCover("goal-half").removed).toBe(true);
    expect(storedCover("goal-half")).toBeUndefined();
    expect(mockClearCover("goal-half").removed).toBe(false);
  });

  it("refuses in the server's words", () => {
    expect(() => mockSetCover({ subject: "task-longrun", photoId: "mock-meadow" })).toThrow(
      /is a Task; only identity statements \(Value\), goals, projects and habits have covers\./,
    );
    expect(() => mockSetCover({ subject: "goal-half", photoId: "nope" })).toThrow("That photo is no longer on Unsplash.");
    expect(() => mockSetCover({ subject: "goal-half", photoId: "mock-meadow", positionY: 101 })).toThrow(
      "A cover's position runs from 0 (top) to 100 (bottom); got 101.",
    );
    expect(() => mockMoveCover("goal-half", 20)).toThrow(/has no cover to move\./);
    expect(() => applyAttrs("goal-half", { cover: "{}" })).toThrow(
      "Cover is set with `bsk cover` (a photo from Unsplash), not `bsk set`.",
    );
  });

  it("keeps the cover through other attribute edits", () => {
    applyAttrs("goal-lifeos", { description: "Still a command center." });
    expect(storedCover("goal-lifeos")).toMatchObject({ photo_id: "mock-city-night" });
  });
});

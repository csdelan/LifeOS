import { beforeEach, describe, expect, it } from "vitest";
import { mockReads as reads } from "@/lib/mock/api";
import { mockRemovePersonPhoto, mockSetPersonPhoto, personPhotoOf } from "@/lib/mock/people-store";
import { applyAttrs, getArtifact, resetStore } from "@/lib/mock/store";

const jpeg = (size = 2_000) => new Blob([new Uint8Array(size)], { type: "image/jpeg" });

describe("the mock's Person photos (GEN-15)", () => {
  beforeEach(() => resetStore());

  it("sets a photo, shows it on the People list and the Person's read, replaces it and takes it off", async () => {
    const set = mockSetPersonPhoto("person-sam", jpeg(), "sam.jpg");

    expect(set.person).toMatchObject({ id: "person-sam", type: "Person", title: "Sam" });
    const photo = set.photoArtifactId!;
    expect(getArtifact(photo)).toMatchObject({ contentType: "image/jpeg", filename: "sam.jpg", hasBytes: true });
    expect((await reads.people()).find((p) => p.id === "person-sam")?.photoArtifactId).toBe(photo);
    expect((await reads.getSubject("person-sam"))?.photoArtifactId).toBe(photo);
    expect((await reads.people()).find((p) => p.id === "person-maya")?.photoArtifactId).toBeNull();

    const next = mockSetPersonPhoto("urn:bsk:person:sam-ppl005", jpeg(3_000)).photoArtifactId;
    expect(next).not.toBe(photo);
    expect(personPhotoOf("person-sam")).toBe(next);

    expect(mockRemovePersonPhoto("person-sam").photoArtifactId).toBeNull();
    expect((await reads.getSubject("person-sam"))?.photoArtifactId).toBeNull();
    // The record keeps the bytes.
    expect(getArtifact(next!)?.hasBytes).toBe(true);
  });

  it("takes a photo only for a Person, and only a JPEG, PNG or WebP", () => {
    expect(() => mockSetPersonPhoto("goal-lifeos", jpeg())).toThrow(/only People and AI agents have a photo/);
    expect(() => mockSetPersonPhoto("person-sam", new Blob(["GIF89a"], { type: "image/gif" }))).toThrow(
      "That isn't a photo: send a JPEG, PNG or WebP image.",
    );
    expect(() => mockSetPersonPhoto("person-sam", new Blob([], { type: "image/jpeg" }))).toThrow("That photo is empty.");
    expect(personPhotoOf("person-sam")).toBeNull();
  });

  it("refuses a Person's photo through the generic attribute write, as the kernel does", () => {
    expect(() => applyAttrs("person-sam", { photo: "anything" })).toThrow(
      "A Person's photo is set with `bsk person photo` or on its page, not `bsk set`.",
    );
  });
});

import { describe, expect, it } from "vitest";
import {
  formatBytes,
  formatDuration,
  iconForContentType,
  normalizeArtifact,
  normalizeBinaryCapture,
} from "@/lib/artifacts";
import { inboxItemTitle } from "@/lib/capture";
import type { InboxItem } from "@/lib/production-ui-types";

describe("formatBytes / formatDuration", () => {
  it("formats sizes and durations for chips", () => {
    expect(formatBytes(400)).toBe("400 B");
    expect(formatBytes(2048)).toBe("2.0 KB");
    expect(formatDuration(42)).toBe("0:42");
    expect(formatDuration(65)).toBe("1:05");
  });

  it("picks an audio icon for voice content types", () => {
    expect(iconForContentType("audio/webm")).toBeTruthy();
    expect(iconForContentType("application/pdf")).toBeTruthy();
  });
});

describe("inboxItemTitle", () => {
  it("labels a failed transcription", () => {
    const item: InboxItem = {
      itemId: "x",
      itemKind: "event",
      triagedAt: new Date().toISOString(),
      eventKind: "voice",
      needsTranscription: true,
    };
    expect(inboxItemTitle(item)).toBe("Needs transcription");
  });
});

describe("normalizeArtifact / normalizeBinaryCapture", () => {
  it("turns the live API's int64 strings into numbers", () => {
    const artifact = normalizeArtifact({
      id: "a1",
      eventId: "e1",
      eventKind: "note",
      filename: "statement.pdf",
      contentType: "application/pdf",
      byteSize: "2048",
      sha256: null,
      hasBytes: true,
    });
    expect(artifact.byteSize).toBe(2048);
    expect(artifact.filename).toBe("statement.pdf");
  });

  it("reads a transcript-only voice note as having no file", () => {
    const result = normalizeBinaryCapture({
      eventId: "e2",
      artifactId: null,
      kind: "voice",
      byteSize: null,
      subject: null,
      needsTranscription: false,
      deduplicated: false,
    });
    expect(result.artifactId).toBeNull();
    expect(result.byteSize).toBeUndefined();
    expect(result.deduplicated).toBe(false);
  });
});

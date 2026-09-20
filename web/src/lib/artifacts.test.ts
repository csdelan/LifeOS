import { describe, expect, it } from "vitest";
import { formatBytes, formatDuration, iconForContentType } from "@/lib/artifacts";
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

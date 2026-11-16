import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { RecordedAudio } from "@/components/artifacts/recorded-audio";

/** The player with a duration jsdom can't work out itself, and a currentTime the test can read. */
function renderPlayer() {
  render(<RecordedAudio src="blob:voice" label="Play voice.webm" />);
  const audio = screen.getByLabelText("Play voice.webm") as HTMLAudioElement;
  const media = { duration: NaN, currentTime: 0 };
  Object.defineProperty(audio, "duration", { get: () => media.duration });
  Object.defineProperty(audio, "currentTime", {
    get: () => media.currentTime,
    set: (t: number) => {
      media.currentTime = t;
    },
  });
  return { audio, media };
}

describe("RecordedAudio (CAP-2)", () => {
  it("has a recording with no length in it read through, then goes back to the start", () => {
    const { audio, media } = renderPlayer();
    media.duration = Infinity;
    fireEvent(audio, new Event("loadedmetadata"));
    expect(media.currentTime).toBe(Number.MAX_SAFE_INTEGER);

    media.duration = 42.5;
    media.currentTime = 42.5;
    fireEvent(audio, new Event("durationchange"));
    expect(media.currentTime).toBe(0);

    // Seeking later is the listener's own business.
    media.currentTime = 10;
    fireEvent(audio, new Event("durationchange"));
    expect(media.currentTime).toBe(10);
  });

  it("leaves a recording that says its length alone", () => {
    const { audio, media } = renderPlayer();
    media.duration = 18;
    fireEvent(audio, new Event("loadedmetadata"));
    fireEvent(audio, new Event("durationchange"));
    expect(media.currentTime).toBe(0);
  });
});

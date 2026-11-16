import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { VoiceCaptureDialog } from "@/components/create/voice-capture-dialog";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), {
    success: vi.fn(),
    error: vi.fn(),
    message: vi.fn(),
  }),
}));

const { captureVoice } = vi.hoisted(() => ({ captureVoice: vi.fn() }));
vi.mock("@/lib/queries", () => ({ useWrites: () => ({ captureVoice }) }));

const REFLECTION = {
  id: "review-1",
  title: "Dictate reflection",
  description: "For the Daily review, Sun Nov 15.",
};

function renderVoice(about?: typeof REFLECTION) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 } },
  });
  return render(
    <QueryClientProvider client={client}>
      <VoiceCaptureDialog open onOpenChange={() => {}} about={about} />
    </QueryClientProvider>,
  );
}

/** A phone or a desktop, as the dialog asks the browser (`(pointer: coarse) and (hover: none)`). */
function stubDevice(phone: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: phone && query.includes("pointer: coarse"),
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  }));
}

/** A recorder that hands its recording over when stopped, as a browser's does. */
class FakeRecorder {
  state = "inactive";
  mimeType = "audio/webm";
  ondataavailable: ((ev: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  start() {
    this.state = "recording";
  }
  stop() {
    this.state = "inactive";
    this.onstop?.();
  }
  static isTypeSupported() {
    return true;
  }
}

/** Speech recognition that starts and never hears a word: Chrome on Android while the page records. */
class DeafRecognition {
  continuous = false;
  interimResults = false;
  lang = "";
  onresult = null;
  onerror = null;
  onend = null;
  start() {}
  stop() {}
  abort() {}
}

describe("VoiceCaptureDialog", () => {
  beforeEach(() => {
    captureVoice.mockReset().mockResolvedValue({ eventId: "e1", kind: "voice", deduplicated: false });
    stubDevice(false);
    vi.stubGlobal("MediaRecorder", FakeRecorder);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("explains microphone permission denial", async () => {
    vi.stubGlobal("navigator", {
      ...navigator,
      mediaDevices: {
        getUserMedia: () =>
          Promise.reject(new DOMException("denied", "NotAllowedError")),
      },
    });
    renderVoice();
    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(/Microphone access was denied/i),
    );
    expect(screen.getByRole("button", { name: "Close" })).toBeInTheDocument();
  });

  it("on a phone, takes the keyboard's dictation: no recording, and Capture waits for words", async () => {
    stubDevice(true);
    const getUserMedia = vi.fn();
    vi.stubGlobal("navigator", { ...navigator, mediaDevices: { getUserMedia } });
    renderVoice(REFLECTION);

    const box = await screen.findByRole("textbox", { name: "Transcript" });
    expect(screen.getByRole("status")).toHaveTextContent("Tap the mic on your keyboard and talk");
    expect(screen.getByText(/your phone writes the words here/)).toBeInTheDocument();
    const capture = screen.getByRole("button", { name: "Capture" });
    expect(capture).toBeDisabled();
    expect(screen.queryByRole("checkbox", { name: "Keep audio" })).toBeNull();

    await userEvent.type(box, "A good day, and an early night.");
    expect(capture).toBeEnabled();
    await userEvent.click(capture);

    await waitFor(() => expect(captureVoice).toHaveBeenCalledTimes(1));
    expect(captureVoice).toHaveBeenCalledWith(
      expect.objectContaining({
        audioBlob: undefined,
        transcript: "A good day, and an early night.",
        keepAudio: false,
        type: "Note",
        concerning: "review-1",
      }),
    );
    expect(getUserMedia).not.toHaveBeenCalled();
  });

  it("says when no words came through, and that the sweep files only words", async () => {
    vi.stubGlobal("navigator", {
      ...navigator,
      mediaDevices: { getUserMedia: () => Promise.resolve({ getTracks: () => [{ stop() {} }] }) },
    });
    vi.stubGlobal("webkitSpeechRecognition", DeafRecognition);
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:recorded");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
    renderVoice(REFLECTION);

    await userEvent.click(await screen.findByRole("button", { name: "Stop" }));

    expect(
      await screen.findByText(/No words came through, so only the\s+audio will be kept\./),
    ).toHaveTextContent("The nightly sweep files only words: type them here to have it filed.");
    // The recording is still the only copy, so Capture keeps it (CAP-2).
    await userEvent.click(screen.getByRole("button", { name: "Capture" }));
    await waitFor(() =>
      expect(captureVoice).toHaveBeenCalledWith(
        expect.objectContaining({ transcript: "", keepAudio: true, concerning: "review-1" }),
      ),
    );
  });
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { VoiceCaptureDialog } from "@/components/create/voice-capture-dialog";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), {
    success: vi.fn(),
    error: vi.fn(),
    message: vi.fn(),
  }),
}));

function renderVoice() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 } },
  });
  return render(
    <QueryClientProvider client={client}>
      <VoiceCaptureDialog open onOpenChange={() => {}} />
    </QueryClientProvider>,
  );
}

describe("VoiceCaptureDialog", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "MediaRecorder",
      class {
        state = "inactive";
        start() {
          this.state = "recording";
        }
        stop() {
          this.state = "inactive";
        }
        ondataavailable: ((ev: { data: Blob }) => void) | null = null;
        onstop: (() => void) | null = null;
        static isTypeSupported() {
          return true;
        }
      },
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
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
});

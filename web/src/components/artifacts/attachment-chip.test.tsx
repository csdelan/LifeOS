import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AttachmentChip } from "@/components/artifacts/attachment-chip";
import type { ArtifactRecord } from "@/lib/production-ui-types";

const artifactBytesUrl = vi.fn<(id: string) => Promise<string | null>>();

vi.mock("@/lib/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/client")>()),
  getReads: () => ({ artifactBytesUrl }),
}));

afterEach(() => {
  vi.clearAllMocks();
  vi.restoreAllMocks();
});

/** A voice capture's record as `GET /api/inbox` answers it: no URL for its bytes. */
const VOICE: ArtifactRecord = {
  id: "art-1",
  eventId: "evt-1",
  eventKind: "voice",
  filename: "voice-2026-11-15.webm",
  contentType: "audio/webm",
  byteSize: 48_211,
  sha256: "ab".repeat(32),
  hasBytes: true,
};

function renderChip(artifact: ArtifactRecord, compact?: boolean) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <AttachmentChip artifact={artifact} compact={compact} />
    </QueryClientProvider>,
  );
}

const open = () => screen.getByRole("button", { name: `Open ${VOICE.filename}` });
const download = () => screen.getByRole("button", { name: `Download ${VOICE.filename}` });

describe("AttachmentChip (CAP-2 / CAP-4)", () => {
  it("fetches the bytes of a record that carries no URL, then plays, opens and downloads them", async () => {
    let answer!: (url: string) => void;
    artifactBytesUrl.mockReturnValue(new Promise((resolve) => (answer = resolve)));
    const opened = vi.spyOn(window, "open").mockReturnValue(null);
    const clicked = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    renderChip(VOICE);

    expect(artifactBytesUrl).toHaveBeenCalledWith("art-1");
    expect(open()).toBeDisabled();
    expect(download()).toBeDisabled();
    expect(screen.queryByLabelText(`Play ${VOICE.filename}`)).toBeNull();

    answer("blob:voice-1");
    await waitFor(() => expect(open()).toBeEnabled());
    expect(download()).toBeEnabled();
    expect(screen.getByLabelText(`Play ${VOICE.filename}`)).toHaveAttribute("src", "blob:voice-1");

    await userEvent.click(open());
    expect(opened).toHaveBeenCalledWith("blob:voice-1", "_blank", "noopener,noreferrer");
    await userEvent.click(download());
    const anchor = clicked.mock.instances[0] as unknown as HTMLAnchorElement;
    expect(anchor.href).toBe("blob:voice-1");
    expect(anchor.download).toBe(VOICE.filename);
  });

  it("offers Retry when the bytes can't be read, and opens them once they load", async () => {
    artifactBytesUrl.mockRejectedValueOnce(new Error("offline")).mockResolvedValueOnce("blob:voice-2");
    renderChip(VOICE);

    const retry = await screen.findByRole("button", { name: `Retry loading ${VOICE.filename}` });
    expect(screen.getByText(/Couldn't load it/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: `Open ${VOICE.filename}` })).toBeNull();

    await userEvent.click(retry);
    await waitFor(() => expect(open()).toBeEnabled());
    expect(artifactBytesUrl).toHaveBeenCalledTimes(2);
  });

  it("says so when the server has no bytes for it", async () => {
    artifactBytesUrl.mockResolvedValue(null);
    renderChip(VOICE);

    expect(await screen.findByText(/Its bytes aren't stored/)).toBeInTheDocument();
    expect(open()).toBeDisabled();
    expect(download()).toBeDisabled();
  });

  it("asks for no bytes for a record without them, or for a list row's chip", () => {
    renderChip({ ...VOICE, hasBytes: false });
    expect(screen.getByText(/Its bytes aren't stored/)).toBeInTheDocument();
    expect(open()).toBeDisabled();
    renderChip(VOICE, true);
    expect(artifactBytesUrl).not.toHaveBeenCalled();
  });
});

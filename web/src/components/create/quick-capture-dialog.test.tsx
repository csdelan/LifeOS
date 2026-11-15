import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { QuickCaptureDialog } from "@/components/create/quick-capture-dialog";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), warning: vi.fn(), message: vi.fn() }),
}));

let writes: LifeOsWriteClient;

vi.mock("@/lib/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/client")>()),
  createWriteClient: () => writes,
}));

function renderDialog(onOpenChange = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <QuickCaptureDialog open onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return onOpenChange;
}

describe("QuickCaptureDialog (fifth review R1)", () => {
  it("captures once when Enter is pressed again while the first capture runs", async () => {
    writes = createMockWriteClient();
    let finish: () => void = () => {};
    const capture = vi
      .spyOn(writes, "capture")
      .mockImplementation(() => new Promise<void>((resolve) => (finish = resolve)));
    const onOpenChange = renderDialog();

    const box = screen.getByLabelText("Note");
    fireEvent.change(box, { target: { value: "call the dentist" } });
    fireEvent.keyDown(box, { key: "Enter" });
    fireEvent.keyDown(box, { key: "Enter" });
    expect(capture).toHaveBeenCalledTimes(1);

    finish();
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(capture).toHaveBeenCalledTimes(1);
  });

  it("sends a retry of the same text with the same request id, and changed text with a new one", async () => {
    writes = createMockWriteClient();
    const capture = vi.spyOn(writes, "capture").mockRejectedValue(new Error("The network went away."));
    renderDialog();

    const box = screen.getByLabelText("Note");
    fireEvent.change(box, { target: { value: "call the dentist" } });
    fireEvent.keyDown(box, { key: "Enter" });
    await screen.findByRole("alert");
    fireEvent.keyDown(box, { key: "Enter" });
    await waitFor(() => expect(capture).toHaveBeenCalledTimes(2));
    fireEvent.change(box, { target: { value: "call the dentist on Monday" } });
    fireEvent.keyDown(box, { key: "Enter" });
    await waitFor(() => expect(capture).toHaveBeenCalledTimes(3));

    const ids = capture.mock.calls.map((call) => call[2]);
    expect(ids[0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(ids[1]).toBe(ids[0]);
    expect(ids[2]).not.toBe(ids[0]);
  });
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockReads } from "@/lib/mock/api";
import { captureNote, getState, removeInbox, resetStore } from "@/lib/mock/store";
import { InboxPage } from "@/pages/inbox-page";

const search = vi.hoisted(() => ({ current: {} as Record<string, unknown> }));

vi.mock("@tanstack/react-router", () => ({
  useSearch: () => search.current,
  useNavigate: () => vi.fn(),
}));

beforeEach(() => {
  resetStore();
  localStorage.clear();
});

function renderInbox(view?: "resolved") {
  search.current = view ? { view } : {};
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <InboxPage />
    </QueryClientProvider>,
  );
}

/** Resolves `count` fresh captures, so Resolved holds more than one page. */
function resolveMany(count: number) {
  for (let i = 0; i < count; i++) {
    captureNote(`note ${i}`);
    removeInbox(getState().inbox[0].itemId);
  }
}

describe("Inbox: Resolved's Load older (code review CR76)", () => {
  it("reads the newest fifty, and Load older reads further back", async () => {
    resolveMany(60);
    const total = (await mockReads.inboxResolved(1000)).length;
    expect(total).toBeGreaterThan(50);
    const user = userEvent.setup();
    renderInbox("resolved");

    const older = await screen.findByRole("button", { name: "Load older" });
    const list = () => within(screen.getByRole("list", { name: "Resolved items" })).getAllByRole("listitem");
    expect(list()).toHaveLength(50);

    await user.click(older);

    await screen.findByText("note 0");
    expect(list()).toHaveLength(total);
    expect(screen.queryByRole("button", { name: "Load older" })).not.toBeInTheDocument();
  });

  it("offers no Load older when everything resolved is shown", async () => {
    renderInbox("resolved");
    const rows = await mockReads.inboxResolved();
    expect(rows.length).toBeLessThan(50);
    await screen.findAllByRole("listitem");
    expect(screen.queryByRole("button", { name: "Load older" })).not.toBeInTheDocument();
  });
});

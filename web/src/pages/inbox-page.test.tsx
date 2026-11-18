import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { inboxItemTitle } from "@/lib/capture";
import { mockReads } from "@/lib/mock/api";
import { MOCK_AGENT_VIA } from "@/lib/mock/seed";
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

describe("Inbox: the preview when another item opens (code review CR91)", () => {
  function mountOn(itemId: string) {
    search.current = { item: itemId };
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const page = () => (
      <QueryClientProvider client={client}>
        <InboxPage />
      </QueryClientProvider>
    );
    const view = render(page());
    return (next: string) => {
      search.current = { item: next };
      view.rerender(page());
    };
  }

  it("starts the next item with nothing typed", async () => {
    const [first, second] = getState().inbox;
    const user = userEvent.setup();
    const show = mountOn(first.itemId);
    const tag = await screen.findByRole("combobox", { name: "Add tag" });
    await user.type(tag, "half-typed");
    const title = screen.getByRole("textbox", { name: "Promote title" });
    await user.clear(title);
    await user.type(title, "Typed for the first item");

    show(second.itemId);

    await screen.findByRole("heading", { name: inboxItemTitle(second) });
    expect(screen.getByRole("combobox", { name: "Add tag" })).toHaveValue("");
    expect(screen.getByRole("textbox", { name: "Promote title" })).toHaveValue(inboxItemTitle(second));
  });

  it("keeps a typed title when the item is tagged", async () => {
    const [first] = getState().inbox;
    const user = userEvent.setup();
    mountOn(first.itemId);
    const title = await screen.findByRole("textbox", { name: "Promote title" });
    await user.clear(title);
    await user.type(title, "My own title");

    await user.type(screen.getByRole("combobox", { name: "Add tag" }), "kept");
    await user.click(screen.getByRole("button", { name: "Tag" }));

    expect(await screen.findByText("kept", { selector: "span" }, { timeout: 4000 })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Promote title" })).toHaveValue("My own title");
  });
});

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

// "via Claude" (agent-access plan §5.6 step 2.4): an item an agent put in the Inbox says which, and
// so does one an agent took out; Chris's own say nothing.
describe("Inbox: the agent that put an item there or took it out", () => {
  it("says via the agent's Person on the item it captured, and nothing on Chris's", async () => {
    renderInbox();
    const agents = await screen.findAllByRole("button", { name: /via Compass/ });
    expect(agents).toHaveLength(1);
    expect(agents[0]).toHaveTextContent("Camping logistics has no next action");
    for (const chris of screen.getAllByRole("button", { name: /Lower back after long coding days/ })) {
      expect(chris).not.toHaveTextContent("via");
    }
  });

  it("says via the agent on an item it dismissed", async () => {
    getState().resolved[0].row.via = MOCK_AGENT_VIA;
    renderInbox("resolved");
    const row = await screen.findByRole("button", { name: /cheaper phone plan/ });
    expect(row).toHaveTextContent(/Dismissed .* · via Compass/);
  });
});

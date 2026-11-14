import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ConnectedAgents } from "@/components/people/connected-agents";
import type { AgentRecord } from "@/lib/agents";
import { CLAUDE_CALLBACK, mockAgentsClient, resetMockAgents } from "@/lib/mock/agents";
import { mockSetPersonPhoto } from "@/lib/mock/people-store";
import { resetStore } from "@/lib/mock/store";

const { overrides } = vi.hoisted(() => ({
  overrides: {
    mcpEnabled: true,
    grantsError: null as Error | null,
    writes: "off",
    claude: {} as Partial<AgentRecord>,
  },
}));

// The mock agents, with the MCP switch and Supabase's answer under the test's control.
vi.mock("@/lib/client", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/client")>();
  const mock = await import("@/lib/mock/agents");
  return {
    ...real,
    getAgentsClient: () => {
      const client = mock.mockAgentsClient();
      return {
        ...client,
        overview: async () => {
          const real = await client.overview();
          return {
            ...real,
            mcpEnabled: overrides.mcpEnabled,
            writes: overrides.writes,
            agents: real.agents.map((a) => (a.name === "Claude" ? { ...a, ...overrides.claude } : a)),
          };
        },
        grants: async () => {
          if (overrides.grantsError) throw overrides.grantsError;
          return client.grants();
        },
      };
    },
  };
});

function renderSection() {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queries}>
      <ConnectedAgents />
    </QueryClientProvider>,
  );
}

function card(name: string) {
  return screen.getByRole("article", { name });
}

describe("ConnectedAgents", () => {
  beforeEach(() => {
    resetMockAgents();
    resetStore();
    overrides.mcpEnabled = true;
    overrides.grantsError = null;
    overrides.writes = "off";
    overrides.claude = {};
  });

  it("shows each agent, whether LifeOS lets it in, and that agents only read", async () => {
    renderSection();
    expect(await screen.findByRole("article", { name: "Claude" })).toBeInTheDocument();
    expect(within(card("Claude")).getByText("Connected")).toBeInTheDocument();
    expect(within(card("Claude")).getByText(/calls this week/)).toBeInTheDocument();
    expect(within(card("Research helper")).getByText("Not allowed")).toBeInTheDocument();
    expect(within(card("Research helper")).getByText(/LifeOS refuses it until you allow it here/)).toBeInTheDocument();
    expect(screen.getByText("Read-only: agents can't change anything in LifeOS.")).toBeInTheDocument();
    expect(screen.queryByText(/MCP server is off/)).not.toBeInTheDocument();
    // Waiting on Chris comes first.
    const names = screen.getAllByRole("article").map((a) => within(a).getByRole("heading").textContent);
    expect(names).toEqual(["Research helper", "Claude"]);
  });

  it("says a client was replaced when its agent connected again, and offers to remove its leftover grant", async () => {
    // Claude connected again; the consent page replaced the earlier client but Supabase still lists it.
    await mockAgentsClient().allow("mock-claude-2", "Claude", "https://claude.ai", {
      redirectUri: CLAUDE_CALLBACK,
      replaces: ["mock-claude"],
    });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("switch", { name: "Show revoked (1)" }));
    await waitFor(() => expect(screen.getAllByRole("article", { name: "Claude" })).toHaveLength(2));
    const [replaced] = screen
      .getAllByRole("article", { name: "Claude" })
      .filter((a) => within(a).queryByText(/when Claude connected again/));
    expect(within(replaced).getByText("Revoked")).toBeInTheDocument();
    expect(within(replaced).queryByRole("button", { name: "Allow" })).not.toBeInTheDocument();
    await user.click(within(replaced).getByRole("button", { name: "Remove" }));
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(within(replaced).queryByRole("button", { name: "Remove" })).not.toBeInTheDocument());
  });

  it("allows an agent Supabase lists only after asking", async () => {
    const user = userEvent.setup();
    renderSection();
    await user.click(await within(await screen.findByRole("article", { name: "Research helper" })).findByRole("button", { name: "Allow" }));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog).toHaveTextContent("Only allow an agent you connected yourself.");
    await user.click(within(dialog).getByRole("button", { name: "Allow" }));
    await waitFor(() => expect(within(card("Research helper")).getByText("Connected")).toBeInTheDocument());
    expect(within(card("Research helper")).getByRole("combobox", { name: "Person Research helper acts as" })).toBeInTheDocument();
  });

  it("revokes a connected agent, which then waits under Show revoked", async () => {
    const user = userEvent.setup();
    renderSection();
    await user.click(await within(await screen.findByRole("article", { name: "Claude" })).findByRole("button", { name: "Revoke" }));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog).toHaveTextContent("LifeOS refuses its next call");
    await user.click(within(dialog).getByRole("button", { name: "Revoke" }));
    expect(await screen.findByRole("status")).toHaveTextContent("Revoked Claude. Show revoked lists it.");
    await waitFor(() => expect(screen.queryByRole("article", { name: "Claude" })).not.toBeInTheDocument());
    await user.click(screen.getByRole("switch", { name: "Show revoked (1)" }));
    expect(within(card("Claude")).getByText("Revoked")).toBeInTheDocument();
    expect(within(card("Claude")).queryByRole("button", { name: "Revoke" })).not.toBeInTheDocument();
  });

  it("hides revoked agents until asked, and says so when they're all there is", async () => {
    // Revoked as the page does it: off LifeOS's list and out of Supabase's.
    await mockAgentsClient().revoke("mock-claude");
    await mockAgentsClient().removeGrant("mock-claude");
    const user = userEvent.setup();
    const first = renderSection();
    const toggle = await screen.findByRole("switch", { name: "Show revoked (1)" });
    expect(toggle).not.toBeChecked();
    // The helper still waits on Chris, so it stays; revoked Claude doesn't.
    expect(screen.getByRole("article", { name: "Research helper" })).toBeInTheDocument();
    expect(screen.queryByRole("article", { name: "Claude" })).not.toBeInTheDocument();
    await user.click(toggle);
    expect(within(card("Claude")).getByText("Revoked")).toBeInTheDocument();
    await user.click(toggle);
    expect(screen.queryByRole("article", { name: "Claude" })).not.toBeInTheDocument();

    // Once the helper's gone too, nothing can call, and the page says so rather than show nothing.
    await mockAgentsClient().removeGrant("mock-helper");
    first.unmount();
    renderSection();
    expect(await screen.findByText(/No agent can call LifeOS now: every one here is revoked/)).toBeInTheDocument();
    expect(screen.queryByText("No agents yet")).not.toBeInTheDocument();
  });

  it("shows the photo of the Person an agent acts as, and a bot without one", async () => {
    const photo = mockSetPersonPhoto("person-agent", new Blob([new Uint8Array(100)], { type: "image/jpeg" })).photoArtifactId;
    overrides.claude = { personId: "person-agent", personName: "Cursor Grok" };
    renderSection();
    const claude = await screen.findByRole("article", { name: "Claude" });
    await waitFor(() => expect(claude.querySelector("img")).not.toBeNull());
    expect(claude.querySelector("img")?.getAttribute("src")).toMatch(/^blob:/);
    expect(photo).toBeTruthy();
    // The helper acts as no one yet, so a bot stands in.
    expect(card("Research helper").querySelector("img")).toBeNull();
  });

  it("clears what it said about a revoke once Show revoked is used", async () => {
    const user = userEvent.setup();
    renderSection();
    await user.click(await within(await screen.findByRole("article", { name: "Claude" })).findByRole("button", { name: "Revoke" }));
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Revoke" }));
    await screen.findByRole("status");
    await user.click(await screen.findByRole("switch", { name: "Show revoked (1)" }));
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("shows no toggle while nothing is revoked", async () => {
    renderSection();
    await screen.findByRole("article", { name: "Claude" });
    expect(screen.queryByRole("switch", { name: /Show revoked/ })).not.toBeInTheDocument();
  });

  it("removes an agent it doesn't allow from Supabase", async () => {
    const user = userEvent.setup();
    renderSection();
    await user.click(await within(await screen.findByRole("article", { name: "Research helper" })).findByRole("button", { name: "Remove" }));
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Remove" }));
    // LifeOS never allowed it, so it only knows the client by the calls it refused, among the revoked.
    await waitFor(() => expect(screen.queryByRole("article", { name: "Research helper" })).not.toBeInTheDocument());
    await user.click(screen.getByRole("switch", { name: "Show revoked (1)" }));
    const unknown = card("An unknown agent");
    expect(within(unknown).getByText("Refused")).toBeInTheDocument();
    expect(within(unknown).getByText(/Never allowed in LifeOS/)).toBeInTheDocument();
    expect(within(unknown).getByText("mock-helper")).toBeInTheDocument();
  });

  it("opens an agent's recent calls, newest first, with their arguments", async () => {
    const user = userEvent.setup();
    renderSection();
    const claude = await screen.findByRole("article", { name: "Claude" });
    const toggle = within(claude).getByRole("button", { name: "Recent calls" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    const list = await within(claude).findByRole("list", { name: "Recent calls, newest first" });
    const lines = within(list).getAllByRole("listitem").map((li) => li.textContent);
    expect(lines[0]).toContain("life_snapshot");
    expect(lines[1]).toContain('{"query":"shed"}');
    expect(lines[2]).toContain("error");
  });

  // Code review CR64: the page said read-only while agents were capturing to the Inbox.
  it("says agents may write when writes are on, and which one does as whom", async () => {
    overrides.writes = "capture";
    overrides.claude = { mayWrite: true, readOnlyBecause: null, personId: "p-1", personName: "Claude agent" };
    renderSection();
    expect(await screen.findByText(/Agents you let write can capture to the Inbox and the journal/)).toBeInTheDocument();
    expect(screen.queryByText(/Read-only/)).not.toBeInTheDocument();
    expect(
      within(card("Claude")).getByText(/It can read LifeOS and capture to the Inbox and the journal as Claude agent\./),
    ).toBeInTheDocument();
  });

  it("says why an allowed agent only reads while writes are on", async () => {
    overrides.writes = "capture";
    overrides.claude = { mayWrite: false, readOnlyBecause: "Mcp:WriteClients doesn't name it" };
    renderSection();
    expect(
      await within(await screen.findByRole("article", { name: "Claude" })).findByText(
        /It can read LifeOS, but not write: Mcp:WriteClients doesn't name it\./,
      ),
    ).toBeInTheDocument();
  });

  it("says when the MCP server is off", async () => {
    overrides.mcpEnabled = false;
    renderSection();
    expect(await screen.findByText(/The MCP server is off/)).toBeInTheDocument();
  });

  it("shows LifeOS's list when Supabase doesn't answer", async () => {
    overrides.grantsError = new Error("OAuth server is disabled");
    renderSection();
    expect(await screen.findByRole("alert")).toHaveTextContent("OAuth server is disabled");
    expect(await screen.findByRole("article", { name: "Claude" })).toBeInTheDocument();
    expect(within(card("Claude")).getByText("Disconnected")).toBeInTheDocument();
  });
});

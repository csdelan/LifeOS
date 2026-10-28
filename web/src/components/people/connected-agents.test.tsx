import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ConnectedAgents } from "@/components/people/connected-agents";
import { CLAUDE_CALLBACK, mockAgentsClient, resetMockAgents } from "@/lib/agents";

const { overrides } = vi.hoisted(() => ({
  overrides: { mcpEnabled: true, grantsError: null as Error | null },
}));

// The mock agents, with the MCP switch and Supabase's answer under the test's control.
vi.mock("@/lib/agents", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/agents")>();
  return {
    ...real,
    getAgentsClient: () => {
      const client = real.mockAgentsClient();
      return {
        ...client,
        overview: async () => ({ ...(await client.overview()), mcpEnabled: overrides.mcpEnabled }),
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
    overrides.mcpEnabled = true;
    overrides.grantsError = null;
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
    await screen.findAllByRole("article", { name: "Claude" });
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

  it("revokes a connected agent", async () => {
    const user = userEvent.setup();
    renderSection();
    await user.click(await within(await screen.findByRole("article", { name: "Claude" })).findByRole("button", { name: "Revoke" }));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog).toHaveTextContent("LifeOS refuses its next call");
    await user.click(within(dialog).getByRole("button", { name: "Revoke" }));
    await waitFor(() => expect(within(card("Claude")).getByText("Revoked")).toBeInTheDocument());
    expect(within(card("Claude")).queryByRole("button", { name: "Revoke" })).not.toBeInTheDocument();
  });

  it("removes an agent it doesn't allow from Supabase", async () => {
    const user = userEvent.setup();
    renderSection();
    await user.click(await within(await screen.findByRole("article", { name: "Research helper" })).findByRole("button", { name: "Remove" }));
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Remove" }));
    // LifeOS never allowed it, so it only knows the client by the calls it refused.
    await waitFor(() => expect(screen.queryByRole("article", { name: "Research helper" })).not.toBeInTheDocument());
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

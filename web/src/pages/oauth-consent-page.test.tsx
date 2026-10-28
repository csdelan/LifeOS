import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { isLiveApi } from "@/lib/api-base";
import { ApiRequestError } from "@/lib/live/http";
import { fetchMe } from "@/lib/me";
import { rememberPendingConsent, takePendingConsent, type ConsentClient, type ConsentRequest } from "@/lib/oauth-consent";
import { isSupabaseConfigured } from "@/lib/supabase";
import { OAuthConsentPage } from "@/pages/oauth-consent-page";

vi.mock("next-themes", () => ({
  useTheme: () => ({ resolvedTheme: "dark", setTheme: vi.fn() }),
}));

vi.mock("@/lib/api-base", () => ({
  isLiveApi: vi.fn(() => false),
  apiBaseUrl: () => "http://localhost:5280",
}));

vi.mock("@/lib/supabase", () => ({
  isSupabaseConfigured: vi.fn(() => true),
  getSupabase: () => {
    throw new Error("supabase should not be created in these tests");
  },
}));

const { signInWithGoogle, signOut, authState, client, browser, agents } = vi.hoisted(() => ({
  signInWithGoogle: vi.fn(async () => {}),
  signOut: vi.fn(async () => {}),
  authState: {
    session: null as { access_token: string; user: { email?: string } } | null,
    ready: true,
  },
  client: {
    lookup: vi.fn<ConsentClient["lookup"]>(),
    approve: vi.fn<ConsentClient["approve"]>(),
    deny: vi.fn<ConsentClient["deny"]>(),
  },
  browser: { go: vi.fn<(url: string) => void>(), replace: vi.fn<(url: string) => void>() },
  agents: { allow: vi.fn<(clientId: string, name: string, clientUri?: string | null) => Promise<unknown>>() },
}));

vi.mock("@/lib/agents", () => ({
  getAgentsClient: () => agents,
}));

vi.mock("@/components/auth/auth-provider", () => ({
  useAuth: () => ({
    session: authState.session,
    user: authState.session?.user ?? null,
    ready: authState.ready,
    signInWithGoogle,
    signOut,
  }),
}));

vi.mock("@/lib/me", () => ({
  meQueryKey: ["me"],
  fetchMe: vi.fn(),
}));

vi.mock("@/lib/oauth-consent", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/oauth-consent")>()),
  getConsentClient: () => client,
  browser,
}));

const CALLBACK = "https://claude.ai/api/mcp/auth_callback";

const request: ConsentRequest = {
  authorizationId: "auth-1",
  clientId: "client-1",
  clientName: "Claude",
  clientUri: "https://claude.ai",
  redirectUri: CALLBACK,
  scopes: ["openid", "email"],
  email: "chris@example.com",
};

function at(search: string) {
  window.history.replaceState({}, "", `/oauth/consent${search}`);
}

function renderPage() {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={queries}>
      <OAuthConsentPage />
    </QueryClientProvider>,
  );
}

function liveAs(email: string) {
  vi.mocked(isLiveApi).mockReturnValue(true);
  authState.session = { access_token: "tok", user: { email } };
}

describe("OAuthConsentPage", () => {
  beforeEach(() => {
    at("?authorization_id=auth-1");
    sessionStorage.clear();
    authState.session = null;
    authState.ready = true;
    signInWithGoogle.mockClear();
    signOut.mockClear();
    vi.mocked(isLiveApi).mockReturnValue(false);
    vi.mocked(isSupabaseConfigured).mockReturnValue(true);
    vi.mocked(fetchMe).mockReset();
    client.lookup.mockReset().mockResolvedValue({ kind: "ask", request });
    client.approve.mockReset().mockResolvedValue(`${CALLBACK}?code=c&state=s`);
    client.deny.mockReset().mockResolvedValue(`${CALLBACK}?error=access_denied&state=s`);
    browser.go.mockReset();
    browser.replace.mockReset();
    agents.allow.mockReset().mockResolvedValue({});
  });

  afterEach(() => window.history.replaceState({}, "", "/"));

  it("says so when the link has no request in it", () => {
    at("");
    renderPage();
    expect(screen.getByRole("heading", { name: /this link has no request in it/i })).toBeInTheDocument();
    expect(client.lookup).not.toHaveBeenCalled();
  });

  it("shows who's asking and what for", async () => {
    renderPage();
    expect(await screen.findByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeInTheDocument();
    expect(screen.getByText("chris@example.com")).toBeInTheDocument();
    expect(screen.getAllByText("claude.ai")).toHaveLength(2);
    expect(screen.getByText("Confirm who you are")).toBeInTheDocument();
    expect(screen.getByText("Your email address")).toBeInTheDocument();
    expect(screen.getByText(/agents can only read/i)).toBeInTheDocument();
    expect(client.lookup).toHaveBeenCalledWith("auth-1");
  });

  it("approves and sends the browser back to the agent", async () => {
    rememberPendingConsent("auth-1");
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    await waitFor(() => expect(browser.go).toHaveBeenCalledWith(`${CALLBACK}?code=c&state=s`));
    expect(client.approve).toHaveBeenCalledWith("auth-1");
    expect(client.deny).not.toHaveBeenCalled();
    expect(screen.getByRole("status")).toHaveTextContent("Returning you to claude.ai");
    expect(takePendingConsent()).toBeNull();
  });

  it("adds the agent to LifeOS's list before approving it at Supabase", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    await waitFor(() => expect(client.approve).toHaveBeenCalledWith("auth-1"));
    expect(agents.allow).toHaveBeenCalledWith("client-1", "Claude", "https://claude.ai");
    expect(agents.allow.mock.invocationCallOrder[0]).toBeLessThan(client.approve.mock.invocationCallOrder[0]);
  });

  it("doesn't approve when LifeOS can't add the agent", async () => {
    agents.allow.mockRejectedValueOnce(new Error("API 500"));
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "LifeOS couldn't add Claude to its agents, so it wasn't approved: API 500",
    );
    expect(client.approve).not.toHaveBeenCalled();
    expect(browser.go).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
  });

  it("denies and tells the agent so", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Deny" }));
    await waitFor(() => expect(browser.go).toHaveBeenCalledWith(`${CALLBACK}?error=access_denied&state=s`));
    expect(client.approve).not.toHaveBeenCalled();
    expect(agents.allow).not.toHaveBeenCalled();
  });

  it("shows why approving failed and lets Chris try again", async () => {
    client.approve.mockRejectedValueOnce(new Error("authorization expired"));
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("authorization expired");
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
    expect(browser.go).not.toHaveBeenCalled();
  });

  it("won't follow an address that isn't a web address", async () => {
    client.approve.mockResolvedValueOnce("javascript:alert(1)");
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/isn't a web address/);
    expect(browser.go).not.toHaveBeenCalled();
  });

  it("says so when the request has expired", async () => {
    client.lookup.mockRejectedValue(new Error("authorization not found"));
    renderPage();
    expect(await screen.findByRole("heading", { name: /this request can't be found/i })).toBeInTheDocument();
    expect(screen.getByText(/authorization not found/)).toBeInTheDocument();
  });

  it("goes straight back for a request approved before", async () => {
    client.lookup.mockResolvedValue({ kind: "redirect", url: `${CALLBACK}?code=again` });
    renderPage();
    await waitFor(() => expect(browser.replace).toHaveBeenCalledWith(`${CALLBACK}?code=again`));
    expect(browser.go).not.toHaveBeenCalled();
  });

  it("won't follow a request approved before that points somewhere odd", async () => {
    client.lookup.mockResolvedValue({ kind: "redirect", url: "javascript:alert(1)" });
    renderPage();
    expect(await screen.findByRole("heading", { name: /won't follow this request/i })).toBeInTheDocument();
    expect(browser.replace).not.toHaveBeenCalled();
  });

  describe("live", () => {
    it("needs Supabase's settings", () => {
      vi.mocked(isLiveApi).mockReturnValue(true);
      vi.mocked(isSupabaseConfigured).mockReturnValue(false);
      renderPage();
      expect(screen.getByRole("heading", { name: /missing auth configuration/i })).toBeInTheDocument();
    });

    it("signed out, keeps the request and offers Google sign-in", async () => {
      vi.mocked(isLiveApi).mockReturnValue(true);
      const user = userEvent.setup();
      renderPage();
      expect(screen.getByRole("heading", { name: /connect an agent to lifeos/i })).toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: /sign in with google/i }));
      expect(signInWithGoogle).toHaveBeenCalled();
      expect(takePendingConsent()).toBe("auth-1");
      expect(client.lookup).not.toHaveBeenCalled();
    });

    it("asks once LifeOS knows the account", async () => {
      liveAs("chris@example.com");
      vi.mocked(fetchMe).mockResolvedValue({ email: "chris@example.com" });
      renderPage();
      expect(await screen.findByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeInTheDocument();
    });

    it("turns away an account LifeOS doesn't allow, and lets it deny the request", async () => {
      liveAs("someone@example.com");
      vi.mocked(fetchMe).mockRejectedValue(new ApiRequestError("Forbidden", 403));
      const user = userEvent.setup();
      renderPage();
      expect(
        await screen.findByRole("heading", { name: /this account can't connect agents to lifeos/i }),
      ).toBeInTheDocument();
      expect(screen.getByText(/someone@example.com is signed in/)).toBeInTheDocument();
      expect(client.lookup).not.toHaveBeenCalled();
      expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();

      await user.click(screen.getByRole("button", { name: /turn the request down/i }));
      await waitFor(() => expect(browser.go).toHaveBeenCalledWith(`${CALLBACK}?error=access_denied&state=s`));
      expect(client.approve).not.toHaveBeenCalled();
    });

    it("lets a turned-away account sign out", async () => {
      liveAs("someone@example.com");
      vi.mocked(fetchMe).mockRejectedValue(new ApiRequestError("Forbidden", 403));
      const user = userEvent.setup();
      renderPage();
      await user.click(await screen.findByRole("button", { name: /sign out/i }));
      expect(signOut).toHaveBeenCalled();
    });

    it("signs out when the API no longer takes the session", async () => {
      liveAs("chris@example.com");
      vi.mocked(fetchMe).mockRejectedValue(new ApiRequestError("Unauthorized", 401));
      renderPage();
      await waitFor(() => expect(signOut).toHaveBeenCalled());
      expect(client.lookup).not.toHaveBeenCalled();
    });

    it("offers a retry when the API can't be reached", async () => {
      liveAs("chris@example.com");
      vi.mocked(fetchMe).mockRejectedValueOnce(new Error("Failed to fetch"));
      vi.mocked(fetchMe).mockResolvedValue({ email: "chris@example.com" });
      const user = userEvent.setup();
      renderPage();
      await user.click(await screen.findByRole("button", { name: /try again/i }));
      expect(await screen.findByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeInTheDocument();
    });
  });
});

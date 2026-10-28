import { beforeEach, describe, expect, it, vi } from "vitest";
import { isLiveApi } from "@/lib/api-base";
import {
  MOCK_EXPIRED_ID,
  PENDING_CONSENT_MS,
  authorizationIdFrom,
  consentUrl,
  forgetPendingConsent,
  getConsentClient,
  hostOf,
  isConsentPath,
  isFollowable,
  liveConsentClient,
  mockConsentClient,
  rememberPendingConsent,
  scopeWords,
  takePendingConsent,
} from "@/lib/oauth-consent";

vi.mock("@/lib/api-base", () => ({
  isLiveApi: vi.fn(() => false),
  apiBaseUrl: () => "http://localhost:5280",
}));

const { oauth } = vi.hoisted(() => ({
  oauth: {
    getAuthorizationDetails: vi.fn(),
    approveAuthorization: vi.fn(),
    denyAuthorization: vi.fn(),
  },
}));

vi.mock("@/lib/supabase", () => ({
  isSupabaseConfigured: () => true,
  getSupabase: () => ({ auth: { oauth } }),
}));

describe("consent page addresses", () => {
  it("knows its own path, with or without a trailing slash", () => {
    expect(isConsentPath("/oauth/consent")).toBe(true);
    expect(isConsentPath("/oauth/consent/")).toBe(true);
    expect(isConsentPath("/oauth/consents")).toBe(false);
    expect(isConsentPath("/oauth")).toBe(false);
    expect(isConsentPath("/")).toBe(false);
  });

  it("reads the request id and refuses a malformed one", () => {
    expect(authorizationIdFrom("?authorization_id=3f2a-b9_c.d~e")).toBe("3f2a-b9_c.d~e");
    expect(authorizationIdFrom("?authorization_id=%20abc%20")).toBe("abc");
    expect(authorizationIdFrom("")).toBeNull();
    expect(authorizationIdFrom("?authorization_id=")).toBeNull();
    expect(authorizationIdFrom("?authorization_id=a%20b")).toBeNull();
    expect(authorizationIdFrom("?authorization_id=%3Cscript%3E")).toBeNull();
    expect(authorizationIdFrom(`?authorization_id=${"a".repeat(201)}`)).toBeNull();
  });

  it("builds the page's address for a request", () => {
    expect(consentUrl("abc-123")).toBe("/oauth/consent?authorization_id=abc-123");
  });
});

describe("a request waiting for sign-in", () => {
  beforeEach(() => sessionStorage.clear());

  it("is handed back once", () => {
    rememberPendingConsent("abc-123", 1_000);
    expect(takePendingConsent(2_000)).toBe("abc-123");
    expect(takePendingConsent(2_000)).toBeNull();
  });

  it("is dropped once it's stale", () => {
    rememberPendingConsent("abc-123", 1_000);
    expect(takePendingConsent(1_000 + PENDING_CONSENT_MS + 1)).toBeNull();
  });

  it("is dropped when it's dated in the future", () => {
    rememberPendingConsent("abc-123", 10 * 60_000);
    expect(takePendingConsent(0)).toBeNull();
  });

  it("is dropped when what's stored isn't a request", () => {
    sessionStorage.setItem("lifeos.oauth.pendingAuthorization", "not json");
    expect(takePendingConsent()).toBeNull();
    sessionStorage.setItem("lifeos.oauth.pendingAuthorization", JSON.stringify({ id: "a b", at: Date.now() }));
    expect(takePendingConsent()).toBeNull();
    sessionStorage.setItem("lifeos.oauth.pendingAuthorization", JSON.stringify({ id: 5, at: Date.now() }));
    expect(takePendingConsent()).toBeNull();
  });

  it("can be forgotten", () => {
    rememberPendingConsent("abc-123");
    forgetPendingConsent();
    expect(takePendingConsent()).toBeNull();
  });
});

describe("where the page sends the browser", () => {
  it("shows an address's host", () => {
    expect(hostOf("https://claude.ai/api/mcp/auth_callback?code=x")).toBe("claude.ai");
    expect(hostOf("http://localhost:6274/oauth/callback")).toBe("localhost:6274");
    expect(hostOf("not a url")).toBe("not a url");
  });

  it("follows web addresses only", () => {
    expect(isFollowable("https://claude.ai/api/mcp/auth_callback?code=x")).toBe(true);
    expect(isFollowable("http://localhost:6274/oauth/callback")).toBe(true);
    expect(isFollowable("javascript:alert(1)")).toBe(false);
    expect(isFollowable("data:text/html,hi")).toBe(false);
    expect(isFollowable("cursor://anysphere.cursor-retrieval/oauth")).toBe(false);
  });

  it("says the scopes in words, and unknown ones as they are", () => {
    expect(scopeWords(["openid", "email", "profile", "phone", "offline_access", "custom"])).toEqual([
      "Confirm who you are",
      "Your email address",
      "Your name and picture",
      "Your phone number",
      "Stay connected without asking again",
      "custom",
    ]);
  });
});

describe("liveConsentClient", () => {
  beforeEach(() => {
    oauth.getAuthorizationDetails.mockReset();
    oauth.approveAuthorization.mockReset();
    oauth.denyAuthorization.mockReset();
  });

  it("maps Supabase's request details", async () => {
    oauth.getAuthorizationDetails.mockResolvedValue({
      data: {
        authorization_id: "auth-1",
        redirect_uri: "https://claude.ai/api/mcp/auth_callback",
        client: { id: "client-1", name: " Claude ", uri: "https://claude.ai", logo_uri: "" },
        user: { id: "user-1", email: "chris@example.com" },
        scope: "openid  email profile",
      },
      error: null,
    });
    await expect(liveConsentClient().lookup("auth-1")).resolves.toEqual({
      kind: "ask",
      request: {
        authorizationId: "auth-1",
        clientId: "client-1",
        clientName: "Claude",
        clientUri: "https://claude.ai",
        redirectUri: "https://claude.ai/api/mcp/auth_callback",
        scopes: ["openid", "email", "profile"],
        email: "chris@example.com",
      },
    });
    expect(oauth.getAuthorizationDetails).toHaveBeenCalledWith("auth-1");
  });

  it("names an agent that gave no name", async () => {
    oauth.getAuthorizationDetails.mockResolvedValue({
      data: {
        authorization_id: "auth-1",
        redirect_uri: "http://localhost:6274/oauth/callback",
        client: { id: "client-1", name: "", uri: "", logo_uri: "" },
        user: { id: "user-1", email: "chris@example.com" },
        scope: "",
      },
      error: null,
    });
    const found = await liveConsentClient().lookup("auth-1");
    expect(found).toMatchObject({ kind: "ask", request: { clientName: "An unnamed agent", clientUri: undefined, scopes: [] } });
  });

  it("passes on the way back for a request approved before", async () => {
    oauth.getAuthorizationDetails.mockResolvedValue({
      data: { redirect_url: "https://claude.ai/api/mcp/auth_callback?code=c" },
      error: null,
    });
    await expect(liveConsentClient().lookup("auth-1")).resolves.toEqual({
      kind: "redirect",
      url: "https://claude.ai/api/mcp/auth_callback?code=c",
    });
  });

  it("throws Supabase's error", async () => {
    oauth.getAuthorizationDetails.mockResolvedValue({ data: null, error: { message: "authorization not found" } });
    await expect(liveConsentClient().lookup("auth-1")).rejects.toThrow("authorization not found");
  });

  it("approves and denies without letting supabase-js leave the page", async () => {
    oauth.approveAuthorization.mockResolvedValue({ data: { redirect_url: "https://a/cb?code=c" }, error: null });
    oauth.denyAuthorization.mockResolvedValue({ data: { redirect_url: "https://a/cb?error=access_denied" }, error: null });
    await expect(liveConsentClient().approve("auth-1")).resolves.toBe("https://a/cb?code=c");
    await expect(liveConsentClient().deny("auth-1")).resolves.toBe("https://a/cb?error=access_denied");
    expect(oauth.approveAuthorization).toHaveBeenCalledWith("auth-1", { skipBrowserRedirect: true });
    expect(oauth.denyAuthorization).toHaveBeenCalledWith("auth-1", { skipBrowserRedirect: true });
  });

  it("throws when approving fails", async () => {
    oauth.approveAuthorization.mockResolvedValue({ data: null, error: { message: "expired" } });
    await expect(liveConsentClient().approve("auth-1")).rejects.toThrow("expired");
  });
});

describe("the consent client in use", () => {
  it("is the pretend one in mock mode and Supabase's when live", async () => {
    vi.mocked(isLiveApi).mockReturnValue(false);
    await expect(getConsentClient().lookup("mock-1")).resolves.toMatchObject({ kind: "ask", request: { clientName: "Claude" } });
    expect(oauth.getAuthorizationDetails).not.toHaveBeenCalled();

    vi.mocked(isLiveApi).mockReturnValue(true);
    oauth.getAuthorizationDetails.mockResolvedValue({ data: { redirect_url: "https://a/cb" }, error: null });
    await expect(getConsentClient().lookup("auth-1")).resolves.toEqual({ kind: "redirect", url: "https://a/cb" });
  });

  it("pretends a request expired", async () => {
    await expect(mockConsentClient().lookup(MOCK_EXPIRED_ID)).rejects.toThrow();
  });
});

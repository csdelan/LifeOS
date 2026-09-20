import { beforeEach, describe, expect, it, vi } from "vitest";
import { bearerAuthorization, getAccessToken } from "@/lib/auth-token";
import { isLiveApi } from "@/lib/api-base";
import { getSupabase, isSupabaseConfigured } from "@/lib/supabase";

const { getSession, refreshSession } = vi.hoisted(() => ({
  getSession: vi.fn(),
  refreshSession: vi.fn(),
}));

vi.mock("@/lib/api-base", () => ({
  isLiveApi: vi.fn(() => true),
}));

vi.mock("@/lib/supabase", () => ({
  isSupabaseConfigured: vi.fn(() => true),
  getSupabase: vi.fn(() => ({
    auth: {
      getSession,
      refreshSession,
    },
  })),
}));

describe("getAccessToken", () => {
  beforeEach(() => {
    vi.mocked(isLiveApi).mockReturnValue(true);
    vi.mocked(isSupabaseConfigured).mockReturnValue(true);
    vi.mocked(getSupabase).mockClear();
    getSession.mockReset();
    refreshSession.mockReset();
  });

  it("returns null when not in live mode", async () => {
    vi.mocked(isLiveApi).mockReturnValue(false);
    await expect(getAccessToken()).resolves.toBeNull();
    expect(getSupabase).not.toHaveBeenCalled();
  });

  it("returns the current access token when it is not near expiry", async () => {
    const expiresAt = Math.floor(Date.now() / 1000) + 3600;
    getSession.mockResolvedValue({
      data: { session: { access_token: "live-token", expires_at: expiresAt } },
      error: null,
    });
    await expect(getAccessToken()).resolves.toBe("live-token");
    expect(refreshSession).not.toHaveBeenCalled();
  });

  it("refreshes when the token expires within a minute", async () => {
    const expiresAt = Math.floor(Date.now() / 1000) + 30;
    getSession.mockResolvedValue({
      data: { session: { access_token: "stale-token", expires_at: expiresAt } },
      error: null,
    });
    refreshSession.mockResolvedValue({
      data: { session: { access_token: "fresh-token" } },
      error: null,
    });
    await expect(getAccessToken()).resolves.toBe("fresh-token");
  });

  it("builds a Bearer header only when a token is present", () => {
    expect(bearerAuthorization("abc")).toEqual({ Authorization: "Bearer abc" });
    expect(bearerAuthorization(null)).toEqual({});
  });
});

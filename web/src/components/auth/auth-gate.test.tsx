import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AuthGate } from "@/components/auth/auth-gate";
import { ApiRequestError } from "@/lib/live/http";
import { fetchMe } from "@/lib/me";
import { isLiveApi } from "@/lib/api-base";
import { isSupabaseConfigured } from "@/lib/supabase";

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

const { signInWithGoogle, signOut, authState } = vi.hoisted(() => ({
  signInWithGoogle: vi.fn(async () => {}),
  signOut: vi.fn(async () => {}),
  authState: {
    session: null as { access_token: string; user: { email?: string } } | null,
    ready: true,
  },
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

function renderGate() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 } },
  });
  return render(
    <QueryClientProvider client={client}>
      <AuthGate>
        <p>App shell</p>
      </AuthGate>
    </QueryClientProvider>,
  );
}

describe("AuthGate", () => {
  beforeEach(() => {
    authState.session = null;
    authState.ready = true;
    signInWithGoogle.mockClear();
    signOut.mockClear();
    vi.mocked(isLiveApi).mockReturnValue(false);
    vi.mocked(isSupabaseConfigured).mockReturnValue(true);
    vi.mocked(fetchMe).mockReset();
  });

  it("skips the guard in mock mode", () => {
    renderGate();
    expect(screen.getByText("App shell")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /sign in with google/i })).not.toBeInTheDocument();
  });

  it("shows Sign in with Google when live and there is no session", () => {
    vi.mocked(isLiveApi).mockReturnValue(true);
    renderGate();
    expect(screen.getByRole("button", { name: /sign in with google/i })).toBeInTheDocument();
    expect(screen.queryByText("App shell")).not.toBeInTheDocument();
  });

  it("loads the app when /api/me succeeds for the signed-in user", async () => {
    vi.mocked(isLiveApi).mockReturnValue(true);
    authState.session = { access_token: "tok", user: { email: "owner@example.com" } };
    vi.mocked(fetchMe).mockResolvedValue({ email: "owner@example.com" });
    renderGate();
    expect(await screen.findByText("App shell")).toBeInTheDocument();
  });

  it("shows the unauthorized screen when the API returns 403", async () => {
    vi.mocked(isLiveApi).mockReturnValue(true);
    authState.session = { access_token: "tok", user: { email: "intruder@example.com" } };
    vi.mocked(fetchMe).mockRejectedValue(new ApiRequestError("Forbidden", 403));
    const user = userEvent.setup();
    renderGate();
    expect(
      await screen.findByRole("heading", { name: /this account isn't authorized/i }),
    ).toBeInTheDocument();
    expect(screen.queryByText("App shell")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /sign out/i }));
    await waitFor(() => expect(signOut).toHaveBeenCalled());
  });
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AuthProvider } from "@/components/auth/auth-provider";
import { useAuth } from "@/components/auth/auth-context";

const { signInWithOAuth, signOut } = vi.hoisted(() => ({
  signInWithOAuth: vi.fn(async () => ({ data: {}, error: null })),
  signOut: vi.fn(async () => ({ error: null })),
}));

vi.mock("@/lib/api-base", () => ({
  isLiveApi: () => true,
}));

vi.mock("@/lib/supabase", () => ({
  isSupabaseConfigured: () => true,
  getSupabase: async () => ({
    auth: {
      signInWithOAuth,
      signOut,
      getSession: async () => ({ data: { session: null } }),
      onAuthStateChange: () => ({ data: { subscription: { unsubscribe: () => {} } } }),
    },
  }),
}));

function AuthButtons() {
  const { signInWithGoogle, signOut: leave } = useAuth();
  return (
    <>
      <button onClick={() => void signInWithGoogle()}>Sign in</button>
      <button onClick={() => void leave()}>Sign out</button>
    </>
  );
}

function renderProvider() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider>
        <AuthButtons />
      </AuthProvider>
    </QueryClientProvider>,
  );
}

/** Every file of the app, to find a sign-out that would end the agents' sessions too. */
const sources = import.meta.glob<string>(["/src/**/*.{ts,tsx}", "!/src/**/*.test.{ts,tsx}"], {
  query: "?raw",
  import: "default",
  eager: true,
});

describe("AuthProvider", () => {
  it("asks Google for the account chooser so a wrong account can be swapped", async () => {
    renderProvider();

    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    await waitFor(() =>
      expect(signInWithOAuth).toHaveBeenCalledWith(
        expect.objectContaining({
          provider: "google",
          options: expect.objectContaining({ queryParams: { prompt: "select_account" } }),
        }),
      ),
    );
  });

  it("signs out this browser only, so Chris's connected agents stay signed in", async () => {
    renderProvider();

    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));

    await waitFor(() => expect(signOut).toHaveBeenCalledWith({ scope: "local" }));
  });

  it("finds no sign-out anywhere in the app that ends every session", () => {
    // Supabase's signOut() with no scope is global: it would end the claude.ai connector's
    // session as well (2026-11-16). Each call names a scope, and only "local" is wanted.
    const global = Object.entries(sources)
      .filter(([, source]) => /\.signOut\((?!\s*\{\s*scope:\s*"local"\s*\})/.test(source))
      .map(([file]) => file);
    expect(global).toEqual([]);
  });
});

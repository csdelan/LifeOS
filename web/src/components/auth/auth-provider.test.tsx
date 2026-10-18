import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AuthProvider, useAuth } from "@/components/auth/auth-provider";

const { signInWithOAuth } = vi.hoisted(() => ({
  signInWithOAuth: vi.fn(async () => ({ data: {}, error: null })),
}));

vi.mock("@/lib/api-base", () => ({
  isLiveApi: () => true,
}));

vi.mock("@/lib/supabase", () => ({
  isSupabaseConfigured: () => true,
  getSupabase: () => ({
    auth: {
      signInWithOAuth,
      getSession: async () => ({ data: { session: null } }),
      onAuthStateChange: () => ({ data: { subscription: { unsubscribe: () => {} } } }),
    },
  }),
}));

function SignInButton() {
  const { signInWithGoogle } = useAuth();
  return <button onClick={() => void signInWithGoogle()}>Sign in</button>;
}

describe("AuthProvider", () => {
  it("asks Google for the account chooser so a wrong account can be swapped", async () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <AuthProvider>
          <SignInButton />
        </AuthProvider>
      </QueryClientProvider>,
    );

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
});

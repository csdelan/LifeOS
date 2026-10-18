import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { Session, User } from "@supabase/supabase-js";
import { isLiveApi } from "@/lib/api-base";
import { getSupabase, isSupabaseConfigured } from "@/lib/supabase";

type AuthContextValue = {
  session: Session | null;
  user: User | null;
  ready: boolean;
  signInWithGoogle: () => Promise<void>;
  signOut: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const live = isLiveApi() && isSupabaseConfigured();
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(!live);
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!live) return;
    const supabase = getSupabase();
    let cancelled = false;
    void supabase.auth
      .getSession()
      .then(({ data }) => {
        if (!cancelled) {
          setSession(data.session);
          setReady(true);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setSession(null);
          setReady(true);
        }
      });
    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((_event, next) => {
      setSession(next);
    });
    return () => {
      cancelled = true;
      subscription.unsubscribe();
    };
  }, [live]);

  const signInWithGoogle = useCallback(async () => {
    const { error } = await getSupabase().auth.signInWithOAuth({
      provider: "google",
      // Signing out of LifeOS leaves Google signed in; without the chooser Google
      // silently reuses that account, so a wrong (non-allowlisted) one can't be swapped.
      options: { redirectTo: window.location.origin, queryParams: { prompt: "select_account" } },
    });
    if (error) throw error;
  }, []);

  const signOut = useCallback(async () => {
    if (live) {
      await getSupabase().auth.signOut();
    }
    setSession(null);
    queryClient.clear();
  }, [live, queryClient]);

  const value = useMemo<AuthContextValue>(
    () => ({
      session,
      user: session?.user ?? null,
      ready,
      signInWithGoogle,
      signOut,
    }),
    [session, ready, signInWithGoogle, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return ctx;
}

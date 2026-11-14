import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { Session } from "@supabase/supabase-js";
import { AuthContext, type AuthContextValue } from "@/components/auth/auth-context";
import { isLiveApi } from "@/lib/api-base";
import { getSupabase, isSupabaseConfigured } from "@/lib/supabase";


export function AuthProvider({ children }: { children: ReactNode }) {
  const live = isLiveApi() && isSupabaseConfigured();
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(!live);
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!live) return;
    let cancelled = false;
    let unsubscribe = () => {};
    void getSupabase()
      .then(async (supabase) => {
        if (cancelled) return;
        const {
          data: { subscription },
        } = supabase.auth.onAuthStateChange((_event, next) => {
          setSession(next);
        });
        unsubscribe = () => subscription.unsubscribe();
        const { data } = await supabase.auth.getSession();
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
    return () => {
      cancelled = true;
      unsubscribe();
    };
  }, [live]);

  const signInWithGoogle = useCallback(async () => {
    const supabase = await getSupabase();
    const { error } = await supabase.auth.signInWithOAuth({
      provider: "google",
      // Signing out of LifeOS leaves Google signed in; without the chooser Google
      // silently reuses that account, so a wrong (non-allowlisted) one can't be swapped.
      options: { redirectTo: window.location.origin, queryParams: { prompt: "select_account" } },
    });
    if (error) throw error;
  }, []);

  const signOut = useCallback(async () => {
    if (live) {
      await (await getSupabase()).auth.signOut();
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

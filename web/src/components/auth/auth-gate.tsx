import { useQuery } from "@tanstack/react-query";
import { useEffect, type ReactNode } from "react";
import { useAuth } from "@/components/auth/auth-provider";
import {
  AuthConfigError,
  AuthReachError,
  AuthSplash,
  SignInScreen,
  UnauthorizedScreen,
} from "@/components/auth/auth-screens";
import { isLiveApi } from "@/lib/api-base";
import { ApiRequestError } from "@/lib/live/http";
import { fetchMe, meQueryKey } from "@/lib/me";
import { isSupabaseConfigured } from "@/lib/supabase";

export function AuthGate({ children }: { children: ReactNode }) {
  const live = isLiveApi();
  const { session, ready, signInWithGoogle, signOut } = useAuth();

  const me = useQuery({
    queryKey: meQueryKey,
    queryFn: fetchMe,
    enabled: live && ready && !!session && isSupabaseConfigured(),
    retry: false,
  });

  useEffect(() => {
    if (me.error instanceof ApiRequestError && me.error.status === 401) {
      void signOut();
    }
  }, [me.error, signOut]);

  if (!live) return children;
  if (!isSupabaseConfigured()) return <AuthConfigError />;
  if (!ready) return <AuthSplash />;
  if (!session) return <SignInScreen onSignIn={signInWithGoogle} />;

  const status = me.error instanceof ApiRequestError ? me.error.status : undefined;
  if (status === 403) {
    return (
      <UnauthorizedScreen email={session.user.email} onSignOut={() => void signOut()} />
    );
  }
  if (me.isError && status !== 401) {
    return (
      <AuthReachError
        message={me.error instanceof Error ? me.error.message : "The API did not respond."}
        onRetry={() => void me.refetch()}
      />
    );
  }
  if (!me.data) return <AuthSplash />;

  return children;
}

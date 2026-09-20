import { isLiveApi } from "@/lib/api-base";
import { getSupabase, isSupabaseConfigured } from "@/lib/supabase";

const REFRESH_SKEW_SECONDS = 60;

/**
 * Current Supabase access token, refreshing when expiry is within a minute.
 * supabase-js also auto-refreshes in the background; this covers the request path.
 */
export async function getAccessToken(): Promise<string | null> {
  if (!isLiveApi() || !isSupabaseConfigured()) return null;
  const { data, error } = await getSupabase().auth.getSession();
  if (error) return null;
  const session = data.session;
  if (!session?.access_token) return null;

  const expiresAt = session.expires_at ?? 0;
  const now = Math.floor(Date.now() / 1000);
  if (expiresAt > 0 && expiresAt - now < REFRESH_SKEW_SECONDS) {
    const refreshed = await getSupabase().auth.refreshSession();
    if (refreshed.data.session?.access_token) {
      return refreshed.data.session.access_token;
    }
  }

  return session.access_token;
}

export function bearerAuthorization(token: string | null): Record<string, string> {
  return token ? { Authorization: `Bearer ${token}` } : {};
}

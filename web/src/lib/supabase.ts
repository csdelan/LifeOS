import type { SupabaseClient } from "@supabase/supabase-js";
import { supabaseAnonKey, supabaseUrl } from "@/lib/public-config";

let client: Promise<SupabaseClient> | null = null;

export function isSupabaseConfigured(): boolean {
  return Boolean(supabaseUrl() && supabaseAnonKey());
}

/**
 * Browser client. The anon key is public by design; the API still enforces the allowlist.
 *
 * supabase-js loads on first use, in a chunk of its own, so the first page's code leaves it
 * out and mock mode never fetches it (code review CR101). Live, `main.tsx` starts it as the app
 * loads and `AuthProvider` reads the session from it. The app's routes aren't drawn until that
 * answers, so the client still finds a sign-in's redirect in the address bar.
 */
export function getSupabase(): Promise<SupabaseClient> {
  if (client) return client;
  const url = supabaseUrl();
  const anonKey = supabaseAnonKey();
  if (!url || !anonKey) {
    return Promise.reject(
      new Error(
        "VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY are required when VITE_API_BASE_URL is set.",
      ),
    );
  }
  const loading = import("@supabase/supabase-js").then(({ createClient }) =>
    createClient(url, anonKey, {
      auth: {
        autoRefreshToken: true,
        persistSession: true,
        detectSessionInUrl: true,
      },
    }),
  );
  client = loading;
  // A chunk that didn't load (offline, or gone after a deploy) is fetched again next time.
  loading.catch(() => {
    if (client === loading) client = null;
  });
  return loading;
}

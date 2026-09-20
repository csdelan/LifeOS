export type PublicConfig = {
  apiBaseUrl?: string;
  supabaseUrl?: string;
  supabaseAnonKey?: string;
};

declare global {
  interface Window {
    __LIFEOS_PUBLIC_CONFIG__?: PublicConfig;
  }
}

/** Runtime config from the API (`/public-config.js`). Empty during Vite mock mode. */
export function readPublicConfig(): PublicConfig {
  if (typeof window === "undefined") {
    return {};
  }
  return window.__LIFEOS_PUBLIC_CONFIG__ ?? {};
}

export function supabaseUrl(): string {
  return readPublicConfig().supabaseUrl || import.meta.env.VITE_SUPABASE_URL || "";
}

export function supabaseAnonKey(): string {
  return readPublicConfig().supabaseAnonKey || import.meta.env.VITE_SUPABASE_ANON_KEY || "";
}

export function runtimeApiBase(): string {
  return readPublicConfig().apiBaseUrl || "";
}

export type PublicConfig = {
  apiBaseUrl?: string;
  supabaseUrl?: string;
  supabaseAnonKey?: string;
  /** The API's home zone (`Rewards:HomeTimeZone`), an IANA name. */
  homeTimeZone?: string;
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

/**
 * The home zone (an IANA name) whose days the kernel counts, from the API's runtime config.
 * Without one (mock mode, an API from before it said), the browser's own zone, which is what the
 * mock store counts days in.
 */
export function homeTimeZone(): string {
  return readPublicConfig().homeTimeZone || Intl.DateTimeFormat().resolvedOptions().timeZone;
}

export function runtimeApiBase(): string {
  return readPublicConfig().apiBaseUrl || "";
}

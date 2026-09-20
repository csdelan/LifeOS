import { runtimeApiBase } from "@/lib/public-config";

/** True when the app should talk to LifeOs.Api instead of the in-memory mock. */
export function isLiveApi(configured = import.meta.env.VITE_API_BASE_URL): boolean {
  return Boolean(configured) || Boolean(runtimeApiBase());
}

/**
 * API origin for openapi-fetch. Production uses `VITE_API_BASE_URL=/` (same-origin)
 * and/or runtime `/public-config.js` from the API. Unset stays mock mode.
 */
export function apiBaseUrl(configured = import.meta.env.VITE_API_BASE_URL): string {
  const value = configured || runtimeApiBase();
  if (!value) {
    throw new Error("VITE_API_BASE_URL is not set.");
  }
  if (value === "/" || value === ".") {
    if (typeof window !== "undefined" && window.location?.origin) {
      return window.location.origin;
    }
    return "";
  }
  return value.replace(/\/$/, "");
}

/** True when the app should talk to LifeOs.Api instead of the in-memory mock. */
export function isLiveApi(configured = import.meta.env.VITE_API_BASE_URL): boolean {
  return Boolean(configured);
}

/**
 * API origin for openapi-fetch. Production container builds pass `VITE_API_BASE_URL=/`
 * (same-origin). Unset / empty stays mock mode.
 */
export function apiBaseUrl(configured = import.meta.env.VITE_API_BASE_URL): string {
  if (!configured) {
    throw new Error("VITE_API_BASE_URL is not set.");
  }
  if (configured === "/" || configured === ".") {
    if (typeof window !== "undefined" && window.location?.origin) {
      return window.location.origin;
    }
    return "";
  }
  return configured.replace(/\/$/, "");
}

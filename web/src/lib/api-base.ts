/** True when the app should talk to LifeOs.Api instead of the in-memory mock. */
export function isLiveApi(): boolean {
  return Boolean(import.meta.env.VITE_API_BASE_URL);
}

export function apiBaseUrl(): string {
  const url = import.meta.env.VITE_API_BASE_URL;
  if (!url) {
    throw new Error("VITE_API_BASE_URL is not set.");
  }
  return url.replace(/\/$/, "");
}

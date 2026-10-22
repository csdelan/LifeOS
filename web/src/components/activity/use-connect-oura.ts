import { useState } from "react";
import { useNavigate } from "@tanstack/react-router";
import { useWrites } from "@/lib/queries";

/** Where Oura sends the browser back to; it must match a redirect URI registered with Oura. */
export function ouraRedirectUri(): string {
  return `${window.location.origin}/activity/connected`;
}

export function errorText(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/**
 * What's wrong with what Oura sent back to `/activity/connected`, in words, or null when it
 * carries a code and the state to finish with.
 */
export function returnError(search: {
  code?: string;
  state?: string;
  error?: string;
  error_description?: string;
}): string | null {
  if (search.error === "access_denied") return "You didn't allow LifeOS to read Oura, so nothing was connected.";
  if (search.error) return `Oura said: ${search.error_description || search.error}.`;
  if (!search.code || !search.state) return "Oura didn't send a sign-in code back. Connect Oura again.";
  return null;
}

/**
 * Connect Oura (activity plan §3.1): ask the API for Oura's sign-in page and go there. The
 * mock answers with this app's own `/activity/connected`, which the router opens in place so
 * the in-memory store survives; a real Oura URL is a full page load.
 */
export function useConnectOura() {
  const writes = useWrites();
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function connect() {
    setBusy(true);
    setError(null);
    try {
      const target = new URL(await writes.authorizeOura(ouraRedirectUri()), window.location.href);
      if (target.origin === window.location.origin && target.pathname === "/activity/connected") {
        await navigate({
          to: "/activity/connected",
          search: {
            code: target.searchParams.get("code") ?? undefined,
            state: target.searchParams.get("state") ?? undefined,
          },
        });
        setBusy(false);
      } else {
        window.location.assign(target.toString());
      }
    } catch (e) {
      setError(errorText(e));
      setBusy(false);
    }
  }

  return { connect, busy, error };
}

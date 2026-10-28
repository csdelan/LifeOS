/**
 * The consent page's seam (agent-access plan §4.4). When an agent connects to /mcp, Supabase's
 * OAuth server sends Chris's browser to `/oauth/consent?authorization_id=…`; this page shows
 * which agent is asking and approves or denies the request through supabase-js. In mock mode a
 * pretend request stands in, so the page can be tried and tested without Supabase.
 */

import { isLiveApi } from "@/lib/api-base";
import { getSupabase } from "@/lib/supabase";

export const CONSENT_PATH = "/oauth/consent";

const PENDING_KEY = "lifeos.oauth.pendingAuthorization";

/** What an agent asks for, in the page's terms. */
export type ConsentRequest = {
  authorizationId: string;
  clientId: string;
  clientName: string;
  /** The agent's website, when it gave one. */
  clientUri?: string;
  /** Where approving sends the browser back to (its host is shown to Chris). */
  redirectUri: string;
  scopes: string[];
  /** The account the request is for. */
  email: string;
};

/** A request to show, or (already approved before) where to send the browser now. */
export type ConsentLookup =
  | { kind: "ask"; request: ConsentRequest }
  | { kind: "redirect"; url: string };

export interface ConsentClient {
  lookup(authorizationId: string): Promise<ConsentLookup>;
  /** Approves the request; resolves to the address that returns the browser to the agent. */
  approve(authorizationId: string): Promise<string>;
  /** Denies the request; resolves to the address that tells the agent so. */
  deny(authorizationId: string): Promise<string>;
}

/** Leaves the page: a seam so tests can watch where the browser would go. */
export const browser = {
  go(url: string) {
    window.location.assign(url);
  },
  replace(url: string) {
    window.location.replace(url);
  },
};

export function isConsentPath(pathname: string): boolean {
  return pathname.replace(/\/+$/, "") === CONSENT_PATH;
}

/** The request's id from the page's query string, or null when it's missing or malformed. */
export function authorizationIdFrom(search: string): string | null {
  const id = new URLSearchParams(search).get("authorization_id")?.trim();
  return id && /^[A-Za-z0-9._~-]{1,200}$/.test(id) ? id : null;
}

export function consentUrl(authorizationId: string): string {
  return `${CONSENT_PATH}?authorization_id=${encodeURIComponent(authorizationId)}`;
}

/**
 * Keeps the request across signing in: Google returns the browser to the site's root, and the
 * sign-in gate sends it back here (see {@link takePendingConsent}).
 */
export function rememberPendingConsent(authorizationId: string, now = Date.now()): void {
  try {
    sessionStorage.setItem(PENDING_KEY, JSON.stringify({ id: authorizationId, at: now }));
  } catch {
    // Without storage, Chris starts again from the agent after signing in.
  }
}

/** How long a request waits for a sign-in to finish before it's dropped. */
export const PENDING_CONSENT_MS = 15 * 60 * 1000;

/** The request waiting for a sign-in to finish, once; null when there's none or it's gone stale. */
export function takePendingConsent(now = Date.now()): string | null {
  try {
    const saved = sessionStorage.getItem(PENDING_KEY);
    sessionStorage.removeItem(PENDING_KEY);
    if (!saved) return null;
    const { id, at } = JSON.parse(saved) as { id?: unknown; at?: unknown };
    if (typeof id !== "string" || typeof at !== "number") return null;
    if (now - at > PENDING_CONSENT_MS || at > now + 60_000) return null;
    return authorizationIdFrom(`?authorization_id=${encodeURIComponent(id)}`);
  } catch {
    return null;
  }
}

export function forgetPendingConsent(): void {
  try {
    sessionStorage.removeItem(PENDING_KEY);
  } catch {
    // Nothing to forget.
  }
}

/** The host an address points to, for "sends you back to …"; the address itself if it can't be read. */
export function hostOf(url: string): string {
  try {
    return new URL(url).host || url;
  } catch {
    return url;
  }
}

/** Only web addresses are followed; anything else (a script, a custom scheme) is refused. */
export function isFollowable(url: string): boolean {
  try {
    const parsed = new URL(url, window.location.origin);
    return parsed.protocol === "https:" || parsed.protocol === "http:";
  } catch {
    return false;
  }
}

const SCOPE_WORDS: Record<string, string> = {
  openid: "Confirm who you are",
  email: "Your email address",
  profile: "Your name and picture",
  phone: "Your phone number",
  offline_access: "Stay connected without asking again",
};

/** The scopes asked for, in words. */
export function scopeWords(scopes: string[]): string[] {
  return scopes.map((s) => SCOPE_WORDS[s] ?? s);
}

function splitScopes(scope: string | undefined): string[] {
  return (scope ?? "").split(/\s+/).filter(Boolean);
}

function fail(error: { message?: string } | null, fallback: string): never {
  throw new Error(error?.message || fallback);
}

/** The real thing: Supabase's OAuth server, as the signed-in user. */
export function liveConsentClient(): ConsentClient {
  return {
    async lookup(authorizationId) {
      const { data, error } = await getSupabase().auth.oauth.getAuthorizationDetails(authorizationId);
      if (error || !data) fail(error, "Supabase didn't return the request.");
      if (!("authorization_id" in data)) {
        return { kind: "redirect", url: data.redirect_url };
      }
      return {
        kind: "ask",
        request: {
          authorizationId: data.authorization_id,
          clientId: data.client.id,
          clientName: data.client.name?.trim() || "An unnamed agent",
          clientUri: data.client.uri || undefined,
          redirectUri: data.redirect_uri,
          scopes: splitScopes(data.scope),
          email: data.user.email,
        },
      };
    },
    async approve(authorizationId) {
      const { data, error } = await getSupabase().auth.oauth.approveAuthorization(authorizationId, {
        skipBrowserRedirect: true,
      });
      if (error || !data) fail(error, "Supabase didn't approve the request.");
      return data.redirect_url;
    },
    async deny(authorizationId) {
      const { data, error } = await getSupabase().auth.oauth.denyAuthorization(authorizationId, {
        skipBrowserRedirect: true,
      });
      if (error || !data) fail(error, "Supabase didn't deny the request.");
      return data.redirect_url;
    },
  };
}

/** An id the mock treats as expired, to show that state. */
export const MOCK_EXPIRED_ID = "mock-expired";

const MOCK_CALLBACK = "https://claude.ai/api/mcp/auth_callback";

/** A pretend request from Claude, for mock mode and the e2e paths. */
export function mockConsentClient(): ConsentClient {
  return {
    async lookup(authorizationId) {
      if (authorizationId === MOCK_EXPIRED_ID) {
        throw new Error("authorization not found");
      }
      return {
        kind: "ask",
        request: {
          authorizationId,
          clientId: "mock-claude",
          clientName: "Claude",
          clientUri: "https://claude.ai",
          redirectUri: MOCK_CALLBACK,
          scopes: ["openid", "email", "profile"],
          email: "chris@example.com",
        },
      };
    },
    async approve() {
      return `${MOCK_CALLBACK}?code=mock-code&state=mock-state`;
    },
    async deny() {
      return `${MOCK_CALLBACK}?error=access_denied&state=mock-state`;
    },
  };
}

export function getConsentClient(): ConsentClient {
  return isLiveApi() ? liveConsentClient() : mockConsentClient();
}

/**
 * Connected agents (agent-access plan §4.5): the agents Chris connected through Supabase's OAuth
 * server, which of them LifeOS allows (its own list, fix 3 in §4.4), and what each one called.
 * Supabase lists the grants; LifeOS's `/api/agents` holds the allow list and the call log. In mock
 * mode `lib/mock/agents.ts` plays both, so the section can be tried and tested without either.
 */

import { fits } from "@/lib/live/fits";
import { api, unwrap } from "@/lib/live/http";
import { getSupabase } from "@/lib/supabase";

/** An agent on LifeOS's allow list, allowed or revoked, or one LifeOS refused. */
export type AgentRecord = {
  clientId: string;
  /** Its name as the consent page showed it; null for a client LifeOS never allowed. */
  name: string | null;
  clientUri: string | null;
  /**
   * Where the consent page sent the browser back to (migration 0048). With the name, it says which
   * agent this is: a client with both the same replaces this one.
   */
  redirectUri: string | null;
  /** On the list and not revoked: `/mcp` answers it. */
  allowed: boolean;
  allowedAt: string | null;
  revokedAt: string | null;
  /** The client that replaced this one when the agent connected again. */
  replacedBy: string | null;
  /** The AI-agent Person it acts as (GEN-15). */
  personId: string | null;
  personName: string | null;
  /** Its Person is an AI agent in use: the Person an agent must act as to write. */
  actsAsAgentPerson: boolean;
  /** `/mcp` lets it write at `Mcp:Writes`'s level, by the rule each call is checked against. */
  mayWrite: boolean;
  /** Why it can only read, in the server log's words; null when it may write. */
  readOnlyBecause: string | null;
  lastCallAt: string | null;
  callsThisWeek: number;
  lastRefusedAt: string | null;
  refusedThisWeek: number;
};

export type AgentsOverview = {
  /** `Mcp:Enabled`: off, no agent can call LifeOS. */
  mcpEnabled: boolean;
  /** `Mcp:Writes`: "off" (read-only) or "capture" (Inbox and journal writes, agent-access plan §5.2). */
  writes: string;
  agents: AgentRecord[];
};

/** What each `Mcp:Writes` level lets an agent do, in Chris's words. */
const WRITE_LEVELS: Record<string, string> = {
  capture: "capture to the Inbox and the journal",
};

function writeLevel(writes: string): string {
  return WRITE_LEVELS[writes] ?? `write (Mcp:Writes is ${writes})`;
}

/**
 * What agents may change, as Connected agents says it: read-only only when `Mcp:Writes` is off
 * (code review CR64; it used to say read-only while agents were writing).
 */
export function writesText(writes: string): string {
  return writes === "off"
    ? "Read-only: agents can't change anything in LifeOS."
    : `Agents you let write can ${writeLevel(writes)}; the rest only read. An agent writes when Mcp:WriteClients names it and it acts as an AI-agent Person.`;
}

/** What one allowed agent may do, as its card says it. */
export function accessText(writes: string, record: AgentRecord | null): string {
  if (record?.mayWrite) {
    return `It can read LifeOS and ${writeLevel(writes)} as ${record.personName ?? "its Person"}.`;
  }
  return writes === "off" || !record?.readOnlyBecause
    ? "It can read LifeOS."
    : `It can read LifeOS, but not write: ${record.readOnlyBecause}.`;
}

export type AgentCallOutcome = "ok" | "error" | "refused";

export type AgentCallRow = {
  id: number;
  calledAt: string;
  clientId: string;
  /** Null for a call refused before any tool ran. */
  tool: string | null;
  /** The arguments as JSON, trimmed to 1 KB. */
  arguments: string | null;
  outcome: AgentCallOutcome;
  items: number | null;
  durationMs: number;
};

/** An agent Chris approved at Supabase. */
export type AgentGrant = {
  clientId: string;
  name: string;
  uri: string | null;
  scopes: string[];
  grantedAt: string;
};

/** What the consent page adds when it allows an agent. */
export type AllowOptions = {
  /** Where approving sends the browser back to. */
  redirectUri?: string | null;
  /**
   * Clients this one replaces (see `replaceable`): it acts as their Person, and they're revoked.
   * LifeOS refuses the lot unless each has the same name and return address.
   */
  replaces?: string[];
};

export interface AgentsClient {
  overview(): Promise<AgentsOverview>;
  /** The agents approved at Supabase. */
  grants(): Promise<AgentGrant[]>;
  calls(clientId: string, limit?: number): Promise<AgentCallRow[]>;
  /** Adds an agent to LifeOS's list, or allows it again. */
  allow(clientId: string, name: string, clientUri?: string | null, options?: AllowOptions): Promise<AgentRecord>;
  /** Takes an agent off LifeOS's list: `/mcp` refuses its next call. */
  revoke(clientId: string): Promise<AgentRecord>;
  /** Removes Supabase's grant, so the agent can't get new tokens. */
  removeGrant(clientId: string): Promise<void>;
  linkPerson(clientId: string, personId: string | null): Promise<AgentRecord>;
}

/** Where an agent stands, from Supabase's grants and LifeOS's list together. */
export type AgentStatus =
  /** Approved at Supabase and allowed in LifeOS: it can call. */
  | "connected"
  /** Approved at Supabase, but not allowed in LifeOS: `/mcp` refuses it. */
  | "not-allowed"
  /** Allowed in LifeOS, but Supabase no longer lists it: it can't get new tokens. */
  | "no-grant"
  /** Revoked (or replaced when its agent connected again), or a client LifeOS only ever refused. */
  | "gone";

export type AgentRow = {
  clientId: string;
  name: string;
  uri: string | null;
  status: AgentStatus;
  grant: AgentGrant | null;
  record: AgentRecord | null;
};

const STATUS_ORDER: Record<AgentStatus, number> = { "not-allowed": 0, connected: 1, "no-grant": 2, gone: 3 };

/**
 * One row per agent from Supabase's grants and LifeOS's list: those waiting on Chris (approved at
 * Supabase but not allowed here) first, then connected ones, then the rest, each by name.
 */
export function agentRows(grants: AgentGrant[], records: AgentRecord[]): AgentRow[] {
  const byId = new Map(records.map((r) => [r.clientId, r]));
  const rows: AgentRow[] = grants.map((grant) => {
    const record = byId.get(grant.clientId) ?? null;
    byId.delete(grant.clientId);
    return {
      clientId: grant.clientId,
      name: grant.name || record?.name || "An unnamed agent",
      uri: grant.uri ?? record?.clientUri ?? null,
      // A client replaced when its agent connected again isn't waiting on Chris, even while
      // Supabase still lists it.
      status: record?.allowed ? "connected" : record?.replacedBy ? "gone" : "not-allowed",
      grant,
      record,
    };
  });
  for (const record of byId.values()) {
    rows.push({
      clientId: record.clientId,
      name: record.name ?? "An unknown agent",
      uri: record.clientUri,
      status: record.allowed ? "no-grant" : "gone",
      grant: null,
      record,
    });
  }
  return rows.sort(
    (a, b) => STATUS_ORDER[a.status] - STATUS_ORDER[b.status] || a.name.localeCompare(b.name) || a.clientId.localeCompare(b.clientId),
  );
}

/**
 * The clients a connecting agent replaces (migration 0048): Claude registers a new client each
 * time it connects again, so the one it used before stays on the list acting as its Person. These
 * are the ones with the same name and the same return address, not already replaced, that are
 * still allowed or act as someone, most recently allowed first. Only claude.ai receives a sign-in
 * sent to claude.ai's address, so a client can't pass itself off as Claude by its name alone.
 */
export function replaceable(
  records: AgentRecord[],
  request: { clientId: string; clientName: string; redirectUri: string },
): AgentRecord[] {
  return records
    .filter(
      (r) =>
        r.clientId !== request.clientId &&
        r.name === request.clientName &&
        r.redirectUri !== null &&
        r.redirectUri === request.redirectUri &&
        r.replacedBy === null &&
        (r.allowed || r.personId !== null),
    )
    .sort((a, b) => (b.allowedAt ?? "").localeCompare(a.allowedAt ?? ""));
}

/** The Person a replacing client takes on: the one the most recently allowed of them acts as. */
export function carriedPerson(replaced: AgentRecord[]): { id: string; name: string } | null {
  const from = [...replaced]
    .sort((a, b) => (b.allowedAt ?? "").localeCompare(a.allowedAt ?? ""))
    .find((r) => r.personId !== null);
  return from?.personId ? { id: from.personId, name: from.personName ?? "an AI agent" } : null;
}

/** An agent's website is its own say, so only a web address is kept; anything else is null. */
export function webAddress(uri: string | null | undefined): string | null {
  return uri && /^https?:\/\/[^\s]+$/i.test(uri.trim()) ? uri.trim() : null;
}

function fail(error: { message?: string } | null, fallback: string): never {
  throw new Error(error?.message || fallback);
}

/** The real thing: Supabase's grants as the signed-in user, and LifeOS's `/api/agents`. */
export function liveAgentsClient(): AgentsClient {
  return {
    async overview() {
      return fits<AgentsOverview>()(await unwrap(api().GET("/api/agents")));
    },
    async grants() {
      const { data, error } = await getSupabase().auth.oauth.listGrants();
      if (error || !data) fail(error, "Supabase didn't list the connected agents.");
      return data.map((g) => ({
        clientId: g.client.id,
        name: g.client.name?.trim() ?? "",
        uri: g.client.uri || null,
        scopes: g.scopes ?? [],
        grantedAt: g.granted_at,
      }));
    },
    async calls(clientId, limit = 20) {
      const read = fits<{ calls: AgentCallRow[] }>()(
        await unwrap(api().GET("/api/agents/calls", { params: { query: { clientId, limit } } })),
      );
      return read.calls;
    },
    async allow(clientId, name, clientUri, options) {
      return fits<AgentRecord>()(
        await unwrap(
          api().PUT("/api/agents/{clientId}", {
            params: { path: { clientId } },
            body: {
              name,
              clientUri: webAddress(clientUri),
              redirectUri: options?.redirectUri ?? null,
              replaces: options?.replaces ?? [],
            },
          }),
        ),
      );
    },
    async revoke(clientId) {
      return fits<AgentRecord>()(
        await unwrap(api().POST("/api/agents/{clientId}/revoke", { params: { path: { clientId } } })),
      );
    },
    async removeGrant(clientId) {
      const { error } = await getSupabase().auth.oauth.revokeGrant({ clientId });
      if (error) fail(error, "Supabase didn't remove the agent.");
    },
    async linkPerson(clientId, personId) {
      return fits<AgentRecord>()(
        await unwrap(
          api().PUT("/api/agents/{clientId}/person", {
            params: { path: { clientId } },
            body: { personId },
          }),
        ),
      );
    },
  };
}

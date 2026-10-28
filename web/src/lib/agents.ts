/**
 * Connected agents (agent-access plan §4.5): the agents Chris connected through Supabase's OAuth
 * server, which of them LifeOS allows (its own list, fix 3 in §4.4), and what each one called.
 * Supabase lists the grants; LifeOS's `/api/agents` holds the allow list and the call log. In mock
 * mode an in-memory stand-in plays both, so the section can be tried and tested without either.
 */

import { isLiveApi } from "@/lib/api-base";
import { api, unwrap } from "@/lib/live/http";
import { getState } from "@/lib/mock/store";
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
  lastCallAt: string | null;
  callsThisWeek: number;
  lastRefusedAt: string | null;
  refusedThisWeek: number;
};

export type AgentsOverview = {
  /** `Mcp:Enabled`: off, no agent can call LifeOS. */
  mcpEnabled: boolean;
  /** `Mcp:Writes`: "off" (read-only) until Phase 2. */
  writes: string;
  agents: AgentRecord[];
};

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

function num(value: unknown): number {
  const n = typeof value === "number" ? value : Number(value);
  return Number.isFinite(n) ? n : 0;
}

function str(value: unknown): string | null {
  return typeof value === "string" && value !== "" ? value : null;
}

export function normalizeAgent(raw: unknown): AgentRecord {
  const r = (raw ?? {}) as Record<string, unknown>;
  return {
    clientId: String(r.clientId ?? ""),
    name: str(r.name),
    clientUri: str(r.clientUri),
    redirectUri: str(r.redirectUri),
    allowed: r.allowed === true,
    allowedAt: str(r.allowedAt),
    revokedAt: str(r.revokedAt),
    replacedBy: str(r.replacedBy),
    personId: str(r.personId),
    personName: str(r.personName),
    lastCallAt: str(r.lastCallAt),
    callsThisWeek: num(r.callsThisWeek),
    lastRefusedAt: str(r.lastRefusedAt),
    refusedThisWeek: num(r.refusedThisWeek),
  };
}

export function normalizeCall(raw: unknown): AgentCallRow {
  const r = (raw ?? {}) as Record<string, unknown>;
  const outcome = r.outcome === "ok" || r.outcome === "refused" ? r.outcome : "error";
  return {
    id: num(r.id),
    calledAt: String(r.calledAt ?? ""),
    clientId: String(r.clientId ?? ""),
    tool: str(r.tool),
    arguments: str(r.arguments),
    outcome,
    items: r.items === null || r.items === undefined ? null : num(r.items),
    durationMs: num(r.durationMs),
  };
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
      const data = (await unwrap(api().GET("/api/agents"))) as Record<string, unknown>;
      return {
        mcpEnabled: data.mcpEnabled === true,
        writes: typeof data.writes === "string" ? data.writes : "off",
        agents: Array.isArray(data.agents) ? data.agents.map(normalizeAgent) : [],
      };
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
      const data = (await unwrap(
        api().GET("/api/agents/calls", { params: { query: { clientId, limit } } }),
      )) as Record<string, unknown>;
      return Array.isArray(data.calls) ? data.calls.map(normalizeCall) : [];
    },
    async allow(clientId, name, clientUri, options) {
      return normalizeAgent(
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
      return normalizeAgent(
        await unwrap(api().POST("/api/agents/{clientId}/revoke", { params: { path: { clientId } } })),
      );
    },
    async removeGrant(clientId) {
      const { error } = await getSupabase().auth.oauth.revokeGrant({ clientId });
      if (error) fail(error, "Supabase didn't remove the agent.");
    },
    async linkPerson(clientId, personId) {
      return normalizeAgent(
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

// ---- Mock mode ----

const MOCK_CLAUDE = "mock-claude";
const MOCK_HELPER = "mock-helper";
/** Where Claude's sign-ins come back to; the mock consent request uses it too. */
export const CLAUDE_CALLBACK = "https://claude.ai/api/mcp/auth_callback";

type MockState = { records: Map<string, AgentRecord>; grants: Map<string, AgentGrant>; calls: AgentCallRow[] };

function hoursAgo(hours: number): string {
  return new Date(Date.now() - hours * 3_600_000).toISOString();
}

function seedState(): MockState {
  const claude: AgentRecord = {
    clientId: MOCK_CLAUDE,
    name: "Claude",
    clientUri: "https://claude.ai",
    redirectUri: CLAUDE_CALLBACK,
    allowed: true,
    allowedAt: hoursAgo(72),
    revokedAt: null,
    replacedBy: null,
    personId: null,
    personName: null,
    lastCallAt: null,
    callsThisWeek: 0,
    lastRefusedAt: null,
    refusedThisWeek: 0,
  };
  const calls: AgentCallRow[] = [
    { id: 4, calledAt: hoursAgo(0.5), clientId: MOCK_CLAUDE, tool: "life_snapshot", arguments: null, outcome: "ok", items: 12, durationMs: 84 },
    { id: 3, calledAt: hoursAgo(0.6), clientId: MOCK_CLAUDE, tool: "find_items", arguments: '{"query":"shed"}', outcome: "ok", items: 3, durationMs: 41 },
    { id: 2, calledAt: hoursAgo(26), clientId: MOCK_CLAUDE, tool: "get_item", arguments: '{"id":"urn:bsk:task:nope"}', outcome: "error", items: null, durationMs: 12 },
    { id: 1, calledAt: hoursAgo(2), clientId: MOCK_HELPER, tool: null, arguments: null, outcome: "refused", items: null, durationMs: 0 },
  ];
  return {
    records: new Map([[MOCK_CLAUDE, claude]]),
    grants: new Map([
      [MOCK_CLAUDE, { clientId: MOCK_CLAUDE, name: "Claude", uri: "https://claude.ai", scopes: ["openid", "email"], grantedAt: hoursAgo(72) }],
      [MOCK_HELPER, { clientId: MOCK_HELPER, name: "Research helper", uri: "https://helper.example.com", scopes: ["openid"], grantedAt: hoursAgo(3) }],
    ]),
    calls,
  };
}

let mock: MockState = seedState();

/** Back to the seed, for tests. */
export function resetMockAgents(): void {
  mock = seedState();
}

function mockRecords(): AgentRecord[] {
  const weekStart = startOfWeek(new Date()).getTime();
  const ids = new Set([...mock.records.keys(), ...mock.calls.filter((c) => c.outcome === "refused").map((c) => c.clientId)]);
  return [...ids].map((clientId) => {
    const base = mock.records.get(clientId) ?? {
      clientId,
      name: null,
      clientUri: null,
      redirectUri: null,
      allowed: false,
      allowedAt: null,
      revokedAt: null,
      replacedBy: null,
      personId: null,
      personName: null,
      lastCallAt: null,
      callsThisWeek: 0,
      lastRefusedAt: null,
      refusedThisWeek: 0,
    };
    const mine = mock.calls.filter((c) => c.clientId === clientId);
    const tools = mine.filter((c) => c.outcome !== "refused");
    const refused = mine.filter((c) => c.outcome === "refused");
    const thisWeek = (rows: AgentCallRow[]) => rows.filter((c) => new Date(c.calledAt).getTime() >= weekStart).length;
    const latest = (rows: AgentCallRow[]) => rows.map((c) => c.calledAt).sort().at(-1) ?? null;
    return {
      ...base,
      lastCallAt: latest(tools),
      callsThisWeek: thisWeek(tools),
      lastRefusedAt: latest(refused),
      refusedThisWeek: thisWeek(refused),
    };
  });
}

/** Monday 00:00 of this week, local time: Chris's week runs Monday to Sunday. */
export function startOfWeek(now: Date): Date {
  const start = new Date(now);
  start.setHours(0, 0, 0, 0);
  start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
  return start;
}

function mockRecord(clientId: string): AgentRecord {
  const record = mockRecords().find((r) => r.clientId === clientId);
  if (!record) throw new Error(`No agent with client id ${clientId} was ever allowed in LifeOS.`);
  return record;
}

/** An in-memory stand-in for Supabase's grants and LifeOS's list, seeded with Claude and a helper LifeOS refuses. */
export function mockAgentsClient(): AgentsClient {
  return {
    async overview() {
      return { mcpEnabled: true, writes: "off", agents: mockRecords() };
    },
    async grants() {
      return [...mock.grants.values()];
    },
    async calls(clientId, limit = 20) {
      return mock.calls
        .filter((c) => c.clientId === clientId)
        .sort((a, b) => b.calledAt.localeCompare(a.calledAt) || b.id - a.id)
        .slice(0, limit);
    },
    async allow(clientId, name, clientUri, options) {
      const now = new Date().toISOString();
      const previous = mock.records.get(clientId);
      const redirectUri = options?.redirectUri ?? null;
      // As LifeOS does: each replaced client must be on the list with the same name and address.
      const replaced = [...new Set(options?.replaces ?? [])].map((id) => {
        const old = mock.records.get(id);
        if (id === clientId) throw new Error("A client can't replace itself.");
        if (!old) throw new Error(`${id} isn't on LifeOS's list, so there's nothing to replace.`);
        if (old.name !== name || !redirectUri || old.redirectUri !== redirectUri) {
          throw new Error(`${id} is ${old.name}. A client replaces only one with the same name and return address.`);
        }
        return old;
      });
      const carried = previous?.personId ? null : carriedPerson(replaced);
      mock.records.set(clientId, {
        ...(previous ?? mockRecordShell(clientId)),
        name,
        clientUri: webAddress(clientUri),
        redirectUri: redirectUri ?? previous?.redirectUri ?? null,
        allowed: true,
        allowedAt: previous?.allowed ? previous.allowedAt : now,
        revokedAt: null,
        replacedBy: null,
        ...(carried ? { personId: carried.id, personName: carried.name } : {}),
      });
      for (const old of replaced) {
        mock.records.set(old.clientId, { ...old, allowed: false, revokedAt: old.revokedAt ?? now, replacedBy: clientId });
      }
      // The consent page's approve, in the mock: Supabase lists it too.
      if (!mock.grants.has(clientId)) {
        mock.grants.set(clientId, { clientId, name, uri: clientUri ?? null, scopes: ["openid"], grantedAt: now });
      }
      return mockRecord(clientId);
    },
    async revoke(clientId) {
      const previous = mock.records.get(clientId);
      if (!previous) throw new Error(`No agent with client id ${clientId} was ever allowed in LifeOS.`);
      mock.records.set(clientId, { ...previous, allowed: false, revokedAt: previous.revokedAt ?? new Date().toISOString() });
      return mockRecord(clientId);
    },
    async removeGrant(clientId) {
      mock.grants.delete(clientId);
    },
    async linkPerson(clientId, personId) {
      const previous = mock.records.get(clientId);
      if (!previous) throw new Error(`No agent with client id ${clientId} was ever allowed in LifeOS.`);
      const person = personId ? getState().people.find((p) => p.id === personId) : null;
      if (personId && !person) throw new Error(`No Person has the id ${personId}.`);
      if (person && person.personKind !== "ai") {
        throw new Error(`${person.title} is a person, not an AI agent. Link an agent to a Person whose kind is AI.`);
      }
      mock.records.set(clientId, { ...previous, personId: person?.id ?? null, personName: person?.title ?? null });
      return mockRecord(clientId);
    },
  };
}

function mockRecordShell(clientId: string): AgentRecord {
  return {
    clientId,
    name: null,
    clientUri: null,
    redirectUri: null,
    allowed: false,
    allowedAt: null,
    revokedAt: null,
    replacedBy: null,
    personId: null,
    personName: null,
    lastCallAt: null,
    callsThisWeek: 0,
    lastRefusedAt: null,
    refusedThisWeek: 0,
  };
}

export function getAgentsClient(): AgentsClient {
  return isLiveApi() ? liveAgentsClient() : mockAgentsClient();
}

/** The mock's connected agents: an in-memory stand-in for Supabase's grants and LifeOS's allow list and call log. */
import {
  AGENT_LEVELS,
  carriedLevel,
  carriedPerson,
  webAddress,
  type AgentCallRow,
  type AgentGrant,
  type AgentRecord,
  type AgentsClient,
} from "@/lib/agents";
import { getState } from "@/lib/mock/store";

const MOCK_CLAUDE = "mock-claude";
const MOCK_HELPER = "mock-helper";
/** The mock's `Mcp:Writes`, the most any agent may change. */
const MOCK_WRITES: string = "full";
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
    actsAsAgentPerson: false,
    mayWrite: false,
    readOnlyBecause: null,
    givenLevel: null,
    writeLevel: null,
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
    const base = mock.records.get(clientId) ?? mockRecordShell(clientId);
    const mine = mock.calls.filter((c) => c.clientId === clientId);
    const tools = mine.filter((c) => c.outcome !== "refused");
    const refused = mine.filter((c) => c.outcome === "refused");
    const thisWeek = (rows: AgentCallRow[]) => rows.filter((c) => new Date(c.calledAt).getTime() >= weekStart).length;
    const latest = (rows: AgentCallRow[]) => rows.map((c) => c.calledAt).sort().at(-1) ?? null;
    return {
      ...base,
      ...mockAccess(base),
      lastCallAt: latest(tools),
      callsThisWeek: thisWeek(tools),
      lastRefusedAt: latest(refused),
      refusedThisWeek: thisWeek(refused),
    };
  });
}

const RANK: Record<string, number> = { off: 0, capture: 1, full: 2 };

/** What `/mcp` lets an agent write, by the rule `McpWriteAccess.ReadOnlyBecause` checks, in the mock. */
function mockAccess(base: AgentRecord): Pick<AgentRecord, "actsAsAgentPerson" | "mayWrite" | "readOnlyBecause" | "givenLevel" | "writeLevel"> {
  const actsAsAgentPerson = base.personId !== null;
  const given = base.name === null ? null : base.givenLevel;
  const because = !base.allowed
    ? "LifeOS doesn't allow it"
    : MOCK_WRITES === "off"
      ? "Mcp:Writes is off"
      : given === null
        ? "it has no write level (Connected agents)"
        : given === "off"
          ? "its write level is read only (Connected agents)"
          : actsAsAgentPerson
            ? null
            : "it acts as no AI-agent Person in use (Connected agents)";
  return {
    actsAsAgentPerson,
    mayWrite: because === null,
    readOnlyBecause: because,
    givenLevel: given,
    writeLevel: because === null && given ? (RANK[given] <= RANK[MOCK_WRITES] ? given : MOCK_WRITES) : null,
  };
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
      return { mcpEnabled: true, writes: MOCK_WRITES, agents: mockRecords() };
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
      // The mock has no Mcp:WriteClients, so a stored level is the only one to carry.
      const level = previous?.givenLevel ? null : carriedLevel(replaced);
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
        ...(level ? { givenLevel: level } : {}),
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
    async setLevel(clientId, level) {
      const previous = mock.records.get(clientId);
      if (!previous) throw new Error(`No agent with client id ${clientId} was ever allowed in LifeOS.`);
      if (level !== null && !AGENT_LEVELS.some((l) => l.value === level)) {
        throw new Error(`'${level}' isn't a write level. Use off, capture or full.`);
      }
      mock.records.set(clientId, { ...previous, givenLevel: level });
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
    actsAsAgentPerson: false,
    mayWrite: false,
    readOnlyBecause: null,
    givenLevel: null,
    writeLevel: null,
    lastCallAt: null,
    callsThisWeek: 0,
    lastRefusedAt: null,
    refusedThisWeek: 0,
  };
}

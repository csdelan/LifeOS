import { beforeEach, describe, expect, it } from "vitest";
import {
  agentRows,
  mockAgentsClient,
  normalizeAgent,
  normalizeCall,
  resetMockAgents,
  startOfWeek,
  webAddress,
  type AgentGrant,
  type AgentRecord,
} from "@/lib/agents";
import { getState } from "@/lib/mock/store";

function record(clientId: string, extra: Partial<AgentRecord> = {}): AgentRecord {
  return {
    clientId,
    name: clientId,
    clientUri: null,
    allowed: true,
    allowedAt: "2026-10-27T10:00:00Z",
    revokedAt: null,
    personId: null,
    personName: null,
    lastCallAt: null,
    callsThisWeek: 0,
    lastRefusedAt: null,
    refusedThisWeek: 0,
    ...extra,
  };
}

function grant(clientId: string, name: string): AgentGrant {
  return { clientId, name, uri: `https://${clientId}.example.com`, scopes: ["openid"], grantedAt: "2026-10-27T10:00:00Z" };
}

describe("agentRows", () => {
  it("puts agents waiting on Chris first, then connected ones, then the rest", () => {
    const rows = agentRows(
      [grant("b", "Beta"), grant("a", "Alpha"), grant("x", "Stranger")],
      [
        record("a", { name: "Alpha" }),
        record("b", { name: "Beta" }),
        record("gone", { name: "Old", allowed: false, revokedAt: "2026-10-01T00:00:00Z" }),
        record("orphan", { name: "Orphan" }),
      ],
    );
    expect(rows.map((r) => [r.clientId, r.status])).toEqual([
      ["x", "not-allowed"],
      ["a", "connected"],
      ["b", "connected"],
      ["orphan", "no-grant"],
      ["gone", "gone"],
    ]);
  });

  it("treats a grant LifeOS revoked as not allowed, and names an unknown client", () => {
    const rows = agentRows(
      [grant("r", "")],
      [record("r", { name: "Revoked one", allowed: false, revokedAt: "2026-10-27T00:00:00Z" }), record("u", { name: null, allowed: false })],
    );
    expect(rows[0]).toMatchObject({ clientId: "r", status: "not-allowed", name: "Revoked one" });
    expect(rows[1]).toMatchObject({ clientId: "u", status: "gone", name: "An unknown agent" });
  });
});

describe("normalize", () => {
  it("reads the API's loose numbers and empty strings", () => {
    expect(normalizeAgent({ clientId: "c", allowed: true, callsThisWeek: "3", name: "" })).toMatchObject({
      clientId: "c",
      allowed: true,
      callsThisWeek: 3,
      name: null,
      refusedThisWeek: 0,
    });
    expect(normalizeCall({ id: "7", outcome: "weird", items: null, durationMs: "12" })).toMatchObject({
      id: 7,
      outcome: "error",
      items: null,
      durationMs: 12,
    });
  });
});

describe("webAddress", () => {
  it("keeps only http and https addresses", () => {
    expect(webAddress("https://claude.ai")).toBe("https://claude.ai");
    expect(webAddress(" http://localhost:3000/x ")).toBe("http://localhost:3000/x");
    expect(webAddress("javascript:alert(1)")).toBeNull();
    expect(webAddress("cursor://callback")).toBeNull();
    expect(webAddress("")).toBeNull();
    expect(webAddress(null)).toBeNull();
  });
});

describe("startOfWeek", () => {
  it("is Monday", () => {
    expect(startOfWeek(new Date(2026, 9, 28, 15)).getDate()).toBe(26); // Wednesday → Monday the 26th
    expect(startOfWeek(new Date(2026, 10, 1, 9)).getDate()).toBe(26); // Sunday → the Monday before
    expect(startOfWeek(new Date(2026, 9, 26, 0, 5)).getDate()).toBe(26);
  });
});

describe("mockAgentsClient", () => {
  beforeEach(() => resetMockAgents());

  it("seeds Claude connected and a helper LifeOS refuses", async () => {
    const agents = mockAgentsClient();
    const rows = agentRows(await agents.grants(), (await agents.overview()).agents);
    expect(rows.map((r) => [r.name, r.status])).toEqual([
      ["Research helper", "not-allowed"],
      ["Claude", "connected"],
    ]);
    expect(rows[0].record?.refusedThisWeek).toBeGreaterThanOrEqual(0);
    expect((await agents.calls("mock-claude")).map((c) => c.tool)).toEqual(["life_snapshot", "find_items", "get_item"]);
  });

  it("allows, revokes and allows again", async () => {
    const agents = mockAgentsClient();
    const allowed = await agents.allow("mock-helper", "Research helper", "https://helper.example.com");
    expect(allowed.allowed).toBe(true);
    const revoked = await agents.revoke("mock-helper");
    expect(revoked).toMatchObject({ allowed: false });
    expect(revoked.revokedAt).not.toBeNull();
    expect((await agents.allow("mock-helper", "Research helper")).revokedAt).toBeNull();
    await expect(agents.revoke("nobody")).rejects.toThrow(/was ever allowed/);
  });

  it("links an agent to an AI Person only", async () => {
    const agents = mockAgentsClient();
    const ai = getState().people.find((p) => p.personKind === "ai")!;
    const human = getState().people.find((p) => p.personKind !== "ai")!;
    await expect(agents.linkPerson("mock-claude", human.id)).rejects.toThrow(/not an AI agent/);
    expect(await agents.linkPerson("mock-claude", ai.id)).toMatchObject({ personId: ai.id, personName: ai.title });
    expect(await agents.linkPerson("mock-claude", null)).toMatchObject({ personId: null, personName: null });
  });

  it("removing a grant leaves LifeOS's list alone", async () => {
    const agents = mockAgentsClient();
    await agents.removeGrant("mock-claude");
    const rows = agentRows(await agents.grants(), (await agents.overview()).agents);
    expect(rows.find((r) => r.clientId === "mock-claude")?.status).toBe("no-grant");
  });
});

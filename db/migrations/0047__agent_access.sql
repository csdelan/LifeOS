-- 0047__agent_access.sql
-- Which agents may read LifeOS through /mcp, and what each one called (agent-access plan
-- §4.5, step 1.4).
--
-- Supabase's OAuth server can't decide this alone: any of Chris's tokens, an agent's
-- included, can approve a new agent there, so an agent could register another client and
-- approve it itself without Chris ever seeing the consent page. LifeOS keeps its own list:
--
--   * `bsk_ops.agent_client` is the allow list. Approving on LifeOS's consent page adds the
--     agent (through /api, which only Chris's Google web session reaches), and /mcp refuses
--     any client that isn't on it, or is revoked, even with a valid token. Revoking takes
--     effect on the agent's next call, not when its token expires.
--   * `bsk_ops.agent_call` logs every tool call: when, which client, the tool, its arguments
--     trimmed to 1 KB, how it went, how many items it returned and how long it took. A call
--     refused because the client isn't allowed is logged too, without a tool. Rows older
--     than 90 days are deleted by the API.
--
-- Both are operational, not the life record: mutable, owner-only like the rest of bsk_ops,
-- never granted to bsk_reader. bsk_backup reads them through 0046's default privileges.

CREATE TABLE IF NOT EXISTS bsk_ops.agent_client (
    -- Supabase's OAuth client id.
    client_id   text        PRIMARY KEY CHECK (client_id ~ '^[A-Za-z0-9._:-]{1,200}$'),
    -- The name and website the client registered with, as the consent page showed them.
    name        text        NOT NULL CHECK (length(name) BETWEEN 1 AND 200),
    client_uri  text        CHECK (client_uri IS NULL OR length(client_uri) <= 500),
    -- When Chris last approved it in LifeOS. It's allowed while revoked_at is null.
    allowed_at  timestamptz NOT NULL DEFAULT now(),
    revoked_at  timestamptz,
    -- The AI-agent Person it acts as (GEN-15), a bsk.subject id. Phase 2 needs this before
    -- the agent may write. Not a foreign key, so bsk_ops restores on its own; the API checks it.
    person_id   uuid,
    updated_at  timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE bsk_ops.agent_client IS
    'Agents Chris approved in LifeOS (the /mcp allow list): allowed while revoked_at is null.';

REVOKE ALL ON bsk_ops.agent_client FROM PUBLIC;

CREATE TABLE IF NOT EXISTS bsk_ops.agent_call (
    id          bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    called_at   timestamptz NOT NULL DEFAULT now(),
    client_id   text        NOT NULL CHECK (length(client_id) BETWEEN 1 AND 200),
    -- Null for a call refused before any tool ran (the client isn't allowed).
    tool        text        CHECK (tool IS NULL OR length(tool) BETWEEN 1 AND 100),
    -- The arguments as JSON, trimmed to 1 KB.
    arguments   text        CHECK (arguments IS NULL OR length(arguments) <= 1024),
    outcome     text        NOT NULL CHECK (outcome IN ('ok', 'error', 'refused')),
    -- Items in the answer's lists, when it has any.
    items       integer     CHECK (items IS NULL OR items >= 0),
    duration_ms integer     NOT NULL CHECK (duration_ms >= 0)
);

COMMENT ON TABLE bsk_ops.agent_call IS
    'One row per MCP tool call, or per refused call from a client not on the allow list. Kept 90 days.';

CREATE INDEX IF NOT EXISTS agent_call_called_at ON bsk_ops.agent_call (called_at);
CREATE INDEX IF NOT EXISTS agent_call_client_called_at ON bsk_ops.agent_call (client_id, called_at DESC);

REVOKE ALL ON bsk_ops.agent_call FROM PUBLIC;

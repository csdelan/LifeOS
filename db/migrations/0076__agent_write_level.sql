-- 0076__agent_write_level.sql
-- Each agent's write level lives in LifeOS beside the Person it acts as, so an agent that connects
-- again keeps it (agent-access plan §5.1, decision 7 as amended 2026-11-19).
--
-- Claude registers a new client each time it connects again. The consent page already carries
-- the old client's Person over to the new one (0048), but the level was only in the
-- Mcp:WriteClients setting, which names client ids, so an agent's writes stopped until the setting
-- named its new id too. Now:
--   * `write_level` is the level Chris gives the client on Connected agents: `off` (it only
--     reads), `capture` or `full`. Replacing a client carries it over, as it carries the Person.
--   * Null leaves the client to Mcp:WriteClients, as before, so nothing changes at deploy.
--   * Mcp:Writes stays a setting: the highest level any client writes at, whatever it's given.

ALTER TABLE bsk_ops.agent_client
    ADD COLUMN IF NOT EXISTS write_level text
        CHECK (write_level IS NULL OR write_level IN ('off', 'capture', 'full'));

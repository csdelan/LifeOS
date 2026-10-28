-- 0048__agent_replaces.sql
-- An agent that reconnects carries its Person over (agent-access plan §4.5).
--
-- Each time Claude goes through Connect again, it registers a new client with Supabase and
-- gets a new client id, so LifeOS's list gains an entry that acts as no one, and the old one
-- stays allowed though nothing calls it. The consent page now spots a client that replaces
-- one already on the list: the same name and the same return address. Approving it links the
-- new client to the old one's Person and revokes the old one.
--
--   * `redirect_uri` is the address the consent page sent the browser back to. The name is
--     the client's own say, and any client can call itself "Claude", but only claude.ai
--     receives a sign-in sent to claude.ai's address, so the two together name the agent.
--   * `replaced_by` is the client that took over, on one revoked that way.

ALTER TABLE bsk_ops.agent_client
    ADD COLUMN IF NOT EXISTS redirect_uri text
        CHECK (redirect_uri IS NULL OR length(redirect_uri) BETWEEN 1 AND 500),
    ADD COLUMN IF NOT EXISTS replaced_by text
        CHECK (replaced_by IS NULL OR replaced_by ~ '^[A-Za-z0-9._:-]{1,200}$');

-- The clients allowed before this migration get their return address from Supabase's own
-- record, where the OAuth server keeps them (a comma-separated list; only a single address is
-- taken). Elsewhere (tests, local Docker) there's no auth.oauth_clients, and a role that can't
-- read it leaves them empty: the consent page then just doesn't offer to replace them.
DO $$
BEGIN
    IF to_regclass('auth.oauth_clients') IS NOT NULL THEN
        UPDATE bsk_ops.agent_client a
        SET redirect_uri = c.redirect_uris
        FROM auth.oauth_clients c
        WHERE c.id::text = a.client_id
          AND a.redirect_uri IS NULL
          AND c.redirect_uris IS NOT NULL
          AND position(',' IN c.redirect_uris) = 0
          AND length(c.redirect_uris) BETWEEN 1 AND 500;
    END IF;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'auth.oauth_clients is not readable; return addresses left empty.';
END
$$;

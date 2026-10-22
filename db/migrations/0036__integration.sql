-- 0036__integration.sql
-- A connection to an outside service, starting with Oura (activity plan A1, ACTIVITY-1; §3.1, §3.5).
--
-- LifeOS reads Oura through OAuth, so it holds an access token and a refresh token. Those are
-- secrets, not part of the life record, and they change on every refresh, so they can't live
-- in `bsk`: every table there is append-only or derived, and readable by `bsk_reader` by
-- default (0005's default privileges). They go in a schema of their own, `bsk_ops`:
--
--   * `bsk_ops` is operational state the owner role reads and writes. It's mutable, and no
--     other role is granted anything on it, `bsk_reader` included.
--   * Tokens are stored encrypted (AES-GCM under `Integrations:TokenKey`, held outside the
--     database), so a dump of the database alone doesn't reveal them.
--   * A refresh token is single-use: a refresh locks the row (SELECT … FOR UPDATE), spends the
--     token and saves the new pair in one transaction.
--   * A connect in progress keeps its OAuth `state`, PKCE verifier and redirect URI here for
--     ten minutes, until Oura sends the browser back.
--
-- Oura's records themselves are observed events in `bsk.event` (D14, migration 0037).

CREATE SCHEMA IF NOT EXISTS bsk_ops;

REVOKE ALL ON SCHEMA bsk_ops FROM PUBLIC;

COMMENT ON SCHEMA bsk_ops IS
    'Operational state that is not part of the life record (integration tokens). Owner only; never granted to bsk_reader.';

CREATE TABLE IF NOT EXISTS bsk_ops.integration (
    -- Which service ('oura').
    service              text        PRIMARY KEY CHECK (service ~ '^[a-z][a-z0-9_-]*$'),

    -- The current tokens, encrypted. Both set or both null; expires_at with them.
    access_token_enc     bytea,
    refresh_token_enc    bytea,
    expires_at           timestamptz,
    -- The scopes the service granted, without any prefix ('daily', 'workout').
    scopes               text[]      NOT NULL DEFAULT '{}',
    connected_at         timestamptz,

    -- The service refused the stored sign-in (a spent or revoked refresh token): the page
    -- shows Reconnect, and nothing calls the service until it's connected again.
    needs_reconnect      boolean     NOT NULL DEFAULT false,
    reconnect_reason     text,

    -- A connect in progress: all four set or all null.
    pending_state        text,
    pending_verifier     text,
    pending_redirect_uri text,
    pending_until        timestamptz,

    -- The last sync's outcome (A2), and the first day the history backfill reached.
    last_synced_at       timestamptz,
    last_sync_error      text,
    history_from         date,

    updated_at           timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT integration_tokens_together CHECK (
        (access_token_enc IS NULL) = (refresh_token_enc IS NULL)
        AND (access_token_enc IS NULL) = (expires_at IS NULL)),
    CONSTRAINT integration_pending_together CHECK (
        (pending_state IS NULL) = (pending_verifier IS NULL)
        AND (pending_state IS NULL) = (pending_redirect_uri IS NULL)
        AND (pending_state IS NULL) = (pending_until IS NULL))
);

COMMENT ON TABLE bsk_ops.integration IS
    'One row per connected outside service: its encrypted OAuth tokens, a connect in progress, and the last sync''s outcome.';

REVOKE ALL ON bsk_ops.integration FROM PUBLIC;

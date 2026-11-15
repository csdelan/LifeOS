-- 0071__writer_role.sql
-- Code review CR126 (second review, choice C): a role for the API to write as, bsk_writer.
--
-- The API has connected as the owner, which can alter or drop any table, drop the append-only
-- triggers on bsk.event and then rewrite the log. bsk_writer reads and writes what the API needs
-- and owns nothing, so a leaked API credential, or a bug that runs SQL it shouldn't, can't change
-- the schema or switch the guard off. bsk migrate, bsk and the backup keep their own roles.
--
--   * NOLOGIN, with no password, as bsk_backup (0046): migrations are shared by every environment,
--     so a password here would be a public default. Each project turns login on by hand
--     (ALTER ROLE bsk_writer WITH LOGIN PASSWORD '…'; docs/pilot/supabase-hosting.md), and the
--     API then connects with BSK_WRITER_CONNECTION_STRING.
--   * USAGE on bsk, bsk_derived and bsk_ops, never CREATE, and it owns nothing: it can't add,
--     alter or drop a table, a view, a function or a trigger.
--   * SELECT, INSERT, UPDATE and DELETE on their tables, but only SELECT and INSERT on the
--     append-only ones (bsk.event and the three artifact tables), whose triggers refuse the rest
--     too. Never TRUNCATE, REFERENCES or TRIGGER: only bsk rebuild truncates, as the owner.
--   * USAGE and SELECT on their sequences (there are none today), so a later serial column works.
--   * Default privileges, so tables added by later migrations are covered (migrations always run
--     as the owner). A later append-only table gets UPDATE and DELETE that way too, and
--     WriterRoleTests fails until its migration revokes them.
--   * Nothing on public.schema_migrations: the API never reads the history (bsk does).

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bsk_writer') THEN
        CREATE ROLE bsk_writer NOLOGIN;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA bsk, bsk_derived, bsk_ops TO bsk_writer;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA bsk, bsk_derived, bsk_ops TO bsk_writer;
REVOKE UPDATE, DELETE ON bsk.event, bsk.artifact, bsk.artifact_blob, bsk.artifact_object FROM bsk_writer;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA bsk, bsk_derived, bsk_ops TO bsk_writer;

ALTER DEFAULT PRIVILEGES IN SCHEMA bsk         GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO bsk_writer;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_derived GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO bsk_writer;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_ops     GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO bsk_writer;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk         GRANT USAGE, SELECT ON SEQUENCES TO bsk_writer;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_derived GRANT USAGE, SELECT ON SEQUENCES TO bsk_writer;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_ops     GRANT USAGE, SELECT ON SEQUENCES TO bsk_writer;

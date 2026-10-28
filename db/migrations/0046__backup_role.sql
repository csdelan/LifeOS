-- 0046__backup_role.sql
-- OPS-1 (agent access plan §3.2): a read-only role for the nightly backup, bsk_backup.
--
-- The backup dumps bsk, bsk_derived, bsk_ops and the migration history every night, unattended.
-- It shouldn't hold the owner's credential (the owner can drop the append-only triggers and
-- rewrite history), and bsk_reader can't do the job: by design it reads neither bsk_ops nor
-- public.schema_migrations. So bsk_backup reads everything and writes nothing.
--
--   * NOLOGIN, with no password: migrations are shared by every environment, so a password here
--     would be a public default. Each project turns login on by hand
--     (ALTER ROLE bsk_backup WITH LOGIN PASSWORD '…'; scripts/backup-setup.ps1 does it).
--   * USAGE on the three schemas, SELECT on their tables, views and sequences, and default
--     privileges so tables added by later migrations are covered (migrations always run as the
--     owner, so the defaults apply to them).
--   * The history table has row level security on and no policies (MigrationRunner), so it also
--     gets a SELECT policy for this role alone. The runner's every-run step only enables RLS and
--     revokes Supabase's API roles; it never drops policies, so this one stays.
--   * No write grant anywhere: a leaked backup password can read everything and change nothing.
--     bsk_ops's tokens are encrypted under Integrations:TokenKey, which isn't in the database.

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bsk_backup') THEN
        CREATE ROLE bsk_backup NOLOGIN;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA bsk, bsk_derived, bsk_ops TO bsk_backup;

GRANT SELECT ON ALL TABLES    IN SCHEMA bsk, bsk_derived, bsk_ops TO bsk_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA bsk, bsk_derived, bsk_ops TO bsk_backup;

ALTER DEFAULT PRIVILEGES IN SCHEMA bsk         GRANT SELECT ON TABLES    TO bsk_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_derived GRANT SELECT ON TABLES    TO bsk_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_ops     GRANT SELECT ON TABLES    TO bsk_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk         GRANT SELECT ON SEQUENCES TO bsk_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_derived GRANT SELECT ON SEQUENCES TO bsk_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_ops     GRANT SELECT ON SEQUENCES TO bsk_backup;

GRANT SELECT ON public.schema_migrations TO bsk_backup;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'schema_migrations'
          AND policyname = 'schema_migrations_backup_read') THEN
        CREATE POLICY schema_migrations_backup_read ON public.schema_migrations
            FOR SELECT TO bsk_backup USING (true);
    END IF;
END
$$;

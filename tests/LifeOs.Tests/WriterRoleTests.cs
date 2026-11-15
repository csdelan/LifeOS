using Dapper;
using LifeOs.Infrastructure;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies the bsk_writer role from migration 0071, which the API writes as (code review CR126):
/// it reads and writes every table in bsk, bsk_derived and bsk_ops, only adds to the append-only
/// ones, and owns nothing, so it can't alter or drop a table or switch the append-only triggers
/// off. The tables come from the catalog, so a table a later migration adds is checked the day
/// it's added. Each test signs in as the role, with the password the test server gives it.
/// </summary>
public sealed class WriterRoleTests(PostgresFixture postgres, ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private static readonly string[] Schemas = ["bsk", "bsk_derived", "bsk_ops"];

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Writer_cannot_sign_in_until_a_project_sets_its_password()
        => Assert.False(postgres.WriterCouldSignInAfterMigrating);

    [Fact]
    public async Task Writer_owns_nothing_belongs_to_no_role_and_creates_nowhere()
    {
        await using var owner = await OpenAsync(classDb.ConnectionString);

        var role = await owner.QuerySingleAsync<(bool Super, bool CreateRole, bool CreateDb, bool BypassRls, bool Replication)>(
            new CommandDefinition(
                """
                SELECT rolsuper, rolcreaterole, rolcreatedb, rolbypassrls, rolreplication
                FROM pg_roles WHERE rolname = @role;
                """, new { role = BskWriter.RoleName }, cancellationToken: Ct));
        Assert.Equal((false, false, false, false, false), role);

        var owned = await owner.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT (SELECT count(*) FROM pg_class WHERE relowner = r.oid)
                 + (SELECT count(*) FROM pg_proc WHERE proowner = r.oid)
                 + (SELECT count(*) FROM pg_namespace WHERE nspowner = r.oid)
                 + (SELECT count(*) FROM pg_type WHERE typowner = r.oid)
            FROM pg_roles r WHERE r.rolname = @role;
            """, new { role = BskWriter.RoleName }, cancellationToken: Ct));
        Assert.Equal(0L, owned);

        // A member of the owner could SET ROLE to it and do anything the owner can.
        var memberOf = (await owner.QueryAsync<string>(new CommandDefinition(
            """
            SELECT g.rolname FROM pg_auth_members m
            JOIN pg_roles g ON g.oid = m.roleid
            JOIN pg_roles r ON r.oid = m.member
            WHERE r.rolname = @role;
            """, new { role = BskWriter.RoleName }, cancellationToken: Ct))).ToList();
        Assert.Empty(memberOf);

        var creates = (await owner.QueryAsync<string>(new CommandDefinition(
            """
            SELECT nspname FROM pg_namespace
            WHERE nspname = ANY(@schemas || 'public'::text)
              AND has_schema_privilege(@role, oid, 'CREATE')
            UNION ALL
            SELECT 'database ' || current_database()
            WHERE has_database_privilege(@role, current_database(), 'CREATE');
            """, new { role = BskWriter.RoleName, schemas = Schemas }, cancellationToken: Ct))).ToList();
        Assert.Empty(creates);
    }

    [Fact]
    public async Task Writer_reads_and_writes_every_table_but_only_adds_to_the_append_only_ones()
    {
        await using var owner = await OpenAsync(classDb.ConnectionString);

        // A table is append-only when bsk.deny_mutation guards it (bsk.event and the artifacts).
        var tables = (await owner.QueryAsync<(string Name, bool AppendOnly)>(new CommandDefinition(
            """
            SELECT format('%I.%I', n.nspname, c.relname),
                   EXISTS (SELECT 1 FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid
                           WHERE t.tgrelid = c.oid AND p.proname = 'deny_mutation')
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = ANY(@schemas) AND c.relkind IN ('r', 'p')
            ORDER BY 1;
            """, new { schemas = Schemas }, cancellationToken: Ct))).ToList();
        Assert.Contains(("bsk.event", true), tables);
        Assert.Contains(("bsk.subject", false), tables);
        Assert.Contains(("bsk_ops.agent_client", false), tables);

        var wrong = new List<string>();
        foreach (var (table, appendOnly) in tables)
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER" })
            {
                var expected = privilege switch
                {
                    "SELECT" or "INSERT" => true,
                    "UPDATE" or "DELETE" => !appendOnly,
                    _ => false,
                };
                var granted = await owner.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT has_table_privilege(@role, @table, @privilege);",
                    new { role = BskWriter.RoleName, table, privilege }, cancellationToken: Ct));
                if (granted != expected)
                {
                    wrong.Add($"{table} {privilege}: {(granted ? "granted" : "not granted")}");
                }
            }
        }

        // An append-only table a later migration adds gets UPDATE and DELETE from the default
        // privileges (0071); its migration revokes them, as 0071 does for bsk.event.
        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("ALTER TABLE bsk.event DISABLE TRIGGER event_deny_update")]
    [InlineData("ALTER TABLE bsk.event DISABLE TRIGGER ALL")]
    [InlineData("DROP TRIGGER event_deny_delete ON bsk.event")]
    [InlineData("CREATE OR REPLACE FUNCTION bsk.deny_mutation() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NEW; END'")]
    [InlineData("UPDATE bsk.event SET kind = kind")]
    [InlineData("DELETE FROM bsk.event")]
    [InlineData("TRUNCATE bsk.event")]
    [InlineData("DELETE FROM bsk.artifact_blob")]
    [InlineData("TRUNCATE bsk_derived.subject_current")]
    [InlineData("ALTER TABLE bsk.subject ADD COLUMN probe int")]
    [InlineData("DROP TABLE bsk_ops.agent_call")]
    [InlineData("DROP VIEW bsk.v_subject_current")]
    [InlineData("CREATE TABLE bsk.writer_probe (id int)")]
    [InlineData("CREATE TABLE public.writer_probe (id int)")]
    [InlineData("SELECT count(*) FROM public.schema_migrations")]
    public async Task Writer_cannot_change_the_schema_the_log_or_the_guard(string statement)
    {
        await using var writer = await OpenAsync(BskWriter.ConnectionStringFrom(classDb.ConnectionString));
        await using var transaction = await writer.BeginTransactionAsync(Ct);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await writer.ExecuteAsync(new CommandDefinition(statement, transaction: transaction, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Writer_adds_events_and_changes_what_the_api_changes()
    {
        await using var writer = await OpenAsync(BskWriter.ConnectionStringFrom(classDb.ConnectionString));
        await using var transaction = await writer.BeginTransactionAsync(Ct);

        var user = await writer.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT current_user;", transaction: transaction, cancellationToken: Ct));
        Assert.Equal(BskWriter.RoleName, user);

        // What the kernel's writes come to, rolled back: the derived row each change keeps
        // (bsk_derived.subject_current) is written by the triggers, as the writer.
        await writer.ExecuteAsync(new CommandDefinition(
            """
            WITH e AS (
                INSERT INTO bsk.event (kind, provenance, occurred_at, source_id)
                VALUES ('note', 'declared', now(), 'writer-role-test') RETURNING id),
            a AS (
                INSERT INTO bsk.subject (urn, type, title, origin_event_id)
                SELECT 'urn:bsk:task:writer-a', 'Task', 'a', id FROM e RETURNING id),
            b AS (
                INSERT INTO bsk.subject (urn, type, title)
                VALUES ('urn:bsk:task:writer-b', 'Task', 'b') RETURNING id)
            INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
            SELECT a.id, 'serves', b.id, 'declared' FROM a, b;

            UPDATE bsk.subject SET title = 'renamed' WHERE urn = 'urn:bsk:task:writer-a';
            INSERT INTO bsk.item_tag (subject_id, tag)
            SELECT id, 'probe' FROM bsk.subject WHERE urn = 'urn:bsk:task:writer-a';
            DELETE FROM bsk.item_tag WHERE tag = 'probe';
            DELETE FROM bsk.subject_relation r USING bsk.subject s
            WHERE s.id = r.from_subject AND s.urn = 'urn:bsk:task:writer-a';

            INSERT INTO bsk_ops.integration (service) VALUES ('writer-role-test')
            ON CONFLICT (service) DO UPDATE SET last_sync_error = 'again';
            UPDATE bsk_ops.integration SET needs_reconnect = true WHERE service = 'writer-role-test';
            DELETE FROM bsk_ops.integration WHERE service = 'writer-role-test';
            """, transaction: transaction, cancellationToken: Ct));

        var title = await writer.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT title FROM bsk.subject WHERE urn = 'urn:bsk:task:writer-a';",
            transaction: transaction, cancellationToken: Ct));
        Assert.Equal("renamed", title);
        await transaction.RollbackAsync(Ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }
}

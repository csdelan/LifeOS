using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies the bsk_backup role from migration 0046: it reads every table in bsk, bsk_derived
/// and bsk_ops and the migration history, writes nothing, and covers tables added later.
/// The role is NOLOGIN, so each test borrows it with SET LOCAL ROLE inside a transaction that
/// rolls back.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupRoleTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Backup_reads_every_table_in_the_three_schemas()
    {
        await using var connection = await OpenAsync();

        var tables = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT format('%I.%I', n.nspname, c.relname)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('bsk', 'bsk_derived', 'bsk_ops') AND c.relkind IN ('r', 'p', 'v')
            ORDER BY 1;
            """, cancellationToken: Ct))).ToList();
        Assert.Contains("bsk_ops.integration", tables);

        await using var transaction = await AsBackupAsync(connection);
        foreach (var table in tables)
        {
            var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT count(*) FROM {table};", transaction: transaction, cancellationToken: Ct));
            Assert.True(count >= 0, $"bsk_backup could not read {table}");
        }
    }

    [Fact]
    public async Task Backup_reads_the_migration_history_despite_row_level_security()
    {
        await using var connection = await OpenAsync();
        var applied = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM public.schema_migrations;", cancellationToken: Ct));

        await using var transaction = await AsBackupAsync(connection);
        var visible = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM public.schema_migrations;", transaction: transaction, cancellationToken: Ct));

        Assert.True(applied > 0);
        Assert.Equal(applied, visible);
    }

    [Theory]
    [InlineData("INSERT INTO bsk.subject (urn, type, title) VALUES ('urn:bsk:task:x', 'Task', 't')")]
    [InlineData("UPDATE bsk.subject SET title = 'nope'")]
    [InlineData("DELETE FROM bsk.event")]
    [InlineData("UPDATE bsk_derived.subject_current SET status = 'nope'")]
    [InlineData("INSERT INTO bsk_ops.integration (service) VALUES ('x')")]
    [InlineData("DELETE FROM bsk_ops.integration")]
    [InlineData("DELETE FROM public.schema_migrations")]
    [InlineData("INSERT INTO public.schema_migrations (version, name, checksum) VALUES (9999, 'x', 'x')")]
    [InlineData("CREATE TABLE bsk.backup_probe (id int)")]
    public async Task Backup_cannot_write(string statement)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await AsBackupAsync(connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                statement, transaction: transaction, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Backup_reads_a_table_a_later_migration_adds()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);

        // As the owner, the way a migration would, then as the backup role.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            CREATE TABLE bsk_ops.backup_later (id bigserial PRIMARY KEY, note text);
            INSERT INTO bsk_ops.backup_later (note) VALUES ('added later');
            SET LOCAL ROLE bsk_backup;
            """, transaction: transaction, cancellationToken: Ct));

        var note = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT note FROM bsk_ops.backup_later;", transaction: transaction, cancellationToken: Ct));
        var sequence = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT last_value FROM bsk_ops.backup_later_id_seq;", transaction: transaction, cancellationToken: Ct));

        Assert.Equal("added later", note);
        Assert.Equal(1L, sequence);
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task Backup_role_cannot_sign_in_until_a_project_sets_its_password()
    {
        await using var connection = await OpenAsync();

        var canLogin = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT rolcanlogin FROM pg_roles WHERE rolname = 'bsk_backup';", cancellationToken: Ct));

        Assert.False(canLogin);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<NpgsqlTransaction> AsBackupAsync(NpgsqlConnection connection)
    {
        var transaction = await connection.BeginTransactionAsync(Ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "SET LOCAL ROLE bsk_backup;", transaction: transaction, cancellationToken: Ct));
        return transaction;
    }
}

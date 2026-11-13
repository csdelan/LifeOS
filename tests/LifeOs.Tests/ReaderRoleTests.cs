using Dapper;
using LifeOs.Infrastructure;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies the bsk_reader role from migration 0005: it can read every table and
/// view, and cannot write to any of them. The relations read come from the catalog, so a new
/// view is checked the day it's added (code review CR52).
/// </summary>
public sealed class ReaderRoleTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenReaderAsync()
    {
        var connection = new NpgsqlConnection(BskReader.ConnectionStringFrom(classDb.ConnectionString));
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task Reader_connects_and_reads_tables_and_views()
    {
        // Seed a subject and event as the owner so there is something to read.
        await using (var owner = new NpgsqlConnection(classDb.ConnectionString))
        {
            await owner.OpenAsync(Ct);
            await owner.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.subject (urn, type, title)
                VALUES (@urn, 'Task', 'readable') ON CONFLICT DO NOTHING;
                """,
                new { urn = $"urn:bsk:task:{Guid.NewGuid():N}" }, cancellationToken: Ct));
        }

        // Every table and view in the readable schemas, as the owner sees them in the catalog.
        List<string> relations;
        await using (var owner = new NpgsqlConnection(classDb.ConnectionString))
        {
            relations = (await owner.QueryAsync<string>(new CommandDefinition(
                """
                SELECT format('%I.%I', n.nspname, c.relname)
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname IN ('bsk', 'bsk_derived') AND c.relkind IN ('r', 'p', 'v', 'm')
                ORDER BY 1;
                """, cancellationToken: Ct))).AsList();
        }

        Assert.Contains("bsk.subject", relations);
        Assert.Contains("bsk_derived.subject_current", relations);
        Assert.True(relations.Count(r => r.StartsWith("bsk.v_", StringComparison.Ordinal)) >= 40, string.Join(", ", relations));

        await using var reader = await OpenReaderAsync();
        var unreadable = new List<string>();
        foreach (var relation in relations)
        {
            try
            {
                await reader.ExecuteScalarAsync<long>(new CommandDefinition(
                    $"SELECT count(*) FROM {relation};", cancellationToken: Ct));
            }
            catch (PostgresException ex)
            {
                unreadable.Add($"{relation}: {ex.SqlState} {ex.MessageText}");
            }
        }

        Assert.Empty(unreadable);
    }

    [Theory]
    [InlineData("INSERT INTO bsk.subject (urn, type, title) VALUES ('urn:bsk:task:x', 'Task', 't')")]
    [InlineData("UPDATE bsk.subject SET title = 'nope'")]
    [InlineData("DELETE FROM bsk.subject")]
    [InlineData("INSERT INTO bsk.event (kind, provenance, occurred_at, source_id) VALUES ('note','declared',now(),'x')")]
    [InlineData("UPDATE bsk_derived.subject_current SET status = 'nope'")]
    public async Task Reader_cannot_write(string writeStatement)
    {
        await using var reader = await OpenReaderAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await reader.ExecuteAsync(new CommandDefinition(writeStatement, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    // Integration tokens live in bsk_ops (migration 0036), which the reader can't touch at all.
    [Theory]
    [InlineData("SELECT count(*) FROM bsk_ops.integration")]
    [InlineData("SELECT access_token_enc FROM bsk_ops.integration")]
    [InlineData("INSERT INTO bsk_ops.integration (service) VALUES ('x')")]
    public async Task Reader_cannot_reach_ops(string statement)
    {
        await using var reader = await OpenReaderAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await reader.ExecuteAsync(new CommandDefinition(statement, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }
}

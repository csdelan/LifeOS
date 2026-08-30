using Dapper;
using LifeOs.Infrastructure;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies the bsk_reader role from migration 0005: it can read every table and
/// view, and cannot write to any of them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReaderRoleTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenReaderAsync()
    {
        var connection = new NpgsqlConnection(BskReader.ConnectionStringFrom(postgres.ConnectionString));
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task Reader_connects_and_reads_tables_and_views()
    {
        // Seed a subject and event as the owner so there is something to read.
        await using (var owner = new NpgsqlConnection(postgres.ConnectionString))
        {
            await owner.OpenAsync(Ct);
            await owner.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.subject (urn, type, title)
                VALUES (@urn, 'Task', 'readable') ON CONFLICT DO NOTHING;
                """,
                new { urn = $"urn:bsk:task:{Guid.NewGuid():N}" }, cancellationToken: Ct));
        }

        await using var reader = await OpenReaderAsync();

        // Reads against base tables and every flattened view succeed.
        foreach (var relation in new[]
                 {
                     "bsk.subject", "bsk.event", "bsk.subject_relation", "bsk.subject_event",
                     "bsk_derived.subject_current",
                     "bsk.v_subject", "bsk.v_event", "bsk.v_subject_relation", "bsk.v_subject_event",
                     "bsk.v_subject_current"
                 })
        {
            var count = await reader.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT count(*) FROM {relation};", cancellationToken: Ct));
            Assert.True(count >= 0, $"reader could not read {relation}");
        }
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
}

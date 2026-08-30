using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies the source-layer guarantees enforced by migration 0002: append-only
/// events and artifacts, provenance/timestamp NOT NULL, the derived-events check,
/// and (source_id, external_id) idempotency.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SourceTablesTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<Guid> InsertEventAsync(
        NpgsqlConnection connection,
        string kind = "journal",
        string provenance = "declared",
        string? sourceId = null,
        string? externalId = null)
    {
        const string sql = """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, external_id)
            VALUES (@kind, @provenance, now(), @sourceId, @externalId)
            RETURNING id;
            """;
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            sql,
            new { kind, provenance, sourceId = sourceId ?? Guid.NewGuid().ToString(), externalId },
            cancellationToken: Ct));
    }

    [Fact]
    public async Task Update_of_an_event_is_denied_at_the_database()
    {
        await using var connection = await OpenAsync();
        var id = await InsertEventAsync(connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE bsk.event SET payload = '{\"x\":1}'::jsonb WHERE id = @id;",
                new { id }, cancellationToken: Ct)));

        Assert.Contains("append-only", ex.MessageText);
    }

    [Fact]
    public async Task Delete_of_an_event_is_denied_at_the_database()
    {
        await using var connection = await OpenAsync();
        var id = await InsertEventAsync(connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM bsk.event WHERE id = @id;",
                new { id }, cancellationToken: Ct)));

        Assert.Contains("append-only", ex.MessageText);
    }

    [Fact]
    public async Task Duplicate_source_and_external_id_does_not_create_a_second_row()
    {
        await using var connection = await OpenAsync();
        var sourceId = Guid.NewGuid().ToString();
        const string externalId = "dupe-1";

        await InsertEventAsync(connection, sourceId: sourceId, externalId: externalId);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertEventAsync(connection, sourceId: sourceId, externalId: externalId));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);

        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE source_id = @sourceId AND external_id = @externalId;",
            new { sourceId, externalId }, cancellationToken: Ct));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Rows_with_null_external_id_are_not_deduplicated()
    {
        await using var connection = await OpenAsync();
        var sourceId = Guid.NewGuid().ToString();

        await InsertEventAsync(connection, sourceId: sourceId, externalId: null);
        await InsertEventAsync(connection, sourceId: sourceId, externalId: null);

        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE source_id = @sourceId;",
            new { sourceId }, cancellationToken: Ct));
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData("provenance")]
    [InlineData("occurred_at")]
    [InlineData("recorded_at")]
    public async Task Required_columns_reject_null(string column)
    {
        await using var connection = await OpenAsync();

        var sql = $"""
            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id)
            VALUES ('journal',
                    {(column == "provenance" ? "NULL" : "'declared'")},
                    {(column == "occurred_at" ? "NULL" : "now()")},
                    {(column == "recorded_at" ? "NULL" : "now()")},
                    @sourceId);
            """;

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                sql, new { sourceId = Guid.NewGuid().ToString() }, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, ex.SqlState);
    }

    [Fact]
    public async Task Derived_event_without_sources_is_rejected()
    {
        await using var connection = await OpenAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, derived_from)
                VALUES ('observation', 'derived', now(), @sourceId, '{}'::uuid[]);
                """,
                new { sourceId = Guid.NewGuid().ToString() }, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Derived_event_with_a_source_is_accepted()
    {
        await using var connection = await OpenAsync();
        var source = await InsertEventAsync(connection, kind: "journal", provenance: "declared");

        var derived = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, derived_from)
            VALUES ('observation', 'derived', now(), @sourceId, ARRAY[@source]::uuid[])
            RETURNING id;
            """,
            new { sourceId = Guid.NewGuid().ToString(), source }, cancellationToken: Ct));

        Assert.NotEqual(Guid.Empty, derived);
    }

    [Fact]
    public async Task Unknown_event_kind_is_rejected()
    {
        await using var connection = await OpenAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertEventAsync(connection, kind: "not_a_kind"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Artifact_update_is_denied_at_the_database()
    {
        await using var connection = await OpenAsync();
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "INSERT INTO bsk.artifact (content) VALUES ('raw text') RETURNING id;",
            cancellationToken: Ct));

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE bsk.artifact SET content = 'changed' WHERE id = @id;",
                new { id }, cancellationToken: Ct)));

        Assert.Contains("append-only", ex.MessageText);
    }
}

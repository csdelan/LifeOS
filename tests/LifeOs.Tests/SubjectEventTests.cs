using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The event → subject link table from migration 0007: <c>subject_event</c> accepts
/// the three event-oriented kinds (concerns/evidences/violates), enforces the
/// (event, subject, relation) unique edge, and references real events and subjects.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SubjectEventTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<Guid> InsertSubjectAsync(NpgsqlConnection connection, string type, string ns)
        => await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title)
            VALUES (@urn, @type, 'untitled') RETURNING id;
            """,
            new { urn = $"urn:bsk:{type.ToLowerInvariant()}:{ns}", type }, cancellationToken: Ct));

    private async Task<Guid> InsertEventAsync(NpgsqlConnection connection)
        => await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id)
            VALUES ('activity', 'declared', now(), 'test') RETURNING id;
            """,
            cancellationToken: Ct));

    private async Task<Guid> InsertEdgeAsync(
        NpgsqlConnection connection, Guid eventId, Guid subjectId, string relation, string provenance = "declared")
        => await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject_event (event_id, subject_id, relation, provenance)
            VALUES (@eventId, @subjectId, @relation, @provenance) RETURNING id;
            """,
            new { eventId, subjectId, relation, provenance }, cancellationToken: Ct));

    [Theory]
    [InlineData("concerns")]
    [InlineData("evidences")]
    [InlineData("violates")]
    public async Task Accepts_the_three_event_oriented_kinds(string relation)
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var eventId = await InsertEventAsync(connection);
        var subject = await InsertSubjectAsync(connection, "Commitment", ns);

        var id = await InsertEdgeAsync(connection, eventId, subject, relation);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task An_unknown_kind_is_rejected()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var eventId = await InsertEventAsync(connection);
        var subject = await InsertSubjectAsync(connection, "Project", ns);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertEdgeAsync(connection, eventId, subject, "serves"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task The_same_edge_cannot_be_recorded_twice()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var eventId = await InsertEventAsync(connection);
        var subject = await InsertSubjectAsync(connection, "Commitment", ns);

        await InsertEdgeAsync(connection, eventId, subject, "violates");

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertEdgeAsync(connection, eventId, subject, "violates"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);

        // A different relation over the same pair is still allowed.
        var other = await InsertEdgeAsync(connection, eventId, subject, "evidences");
        Assert.NotEqual(Guid.Empty, other);
    }

    [Fact]
    public async Task An_edge_to_a_nonexistent_event_is_rejected()
    {
        await using var connection = await OpenAsync();
        var subject = await InsertSubjectAsync(connection, "Commitment", Guid.NewGuid().ToString("N"));

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertEdgeAsync(connection, Guid.NewGuid(), subject, "concerns"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }
}

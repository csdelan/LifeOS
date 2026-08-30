using Dapper;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The atomic write behind <c>bsk log activity</c>: the activity event and its
/// <c>subject_event</c> edges land in one transaction, so a failing edge rolls the
/// whole thing back. This matters because <c>bsk.event</c> is append-only — a
/// committed event with missing edges could never be repaired.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NpgsqlActivityWriterTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private NewEvent ActivityWithText(string text) => new(
        Kind: EventKinds.Activity,
        Provenance: Provenances.Declared,
        OccurredAt: DateTimeOffset.UtcNow,
        RecordedAt: DateTimeOffset.UtcNow,
        SourceId: "test",
        PayloadJson: $$"""{"text": "{{text}}"}""");

    private async Task<Guid> InsertCommitmentAsync(NpgsqlConnection connection, string ns)
        => await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title)
            VALUES (@urn, 'Commitment', 'a commitment') RETURNING id;
            """,
            new { urn = $"urn:bsk:commitment:{ns}" }, cancellationToken: Ct));

    [Fact]
    public async Task Event_and_edges_are_written_together()
    {
        var writer = new NpgsqlActivityWriter(postgres.ConnectionString);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var commitment = await InsertCommitmentAsync(connection, Guid.NewGuid().ToString("N"));

        var eventId = await writer.WriteAsync(
            ActivityWithText("clean write"),
            [new SubjectEventEdge(commitment, SubjectEventRelations.Violates, Provenances.Declared)],
            Ct);

        var edges = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_event WHERE event_id = @id;",
            new { id = eventId }, cancellationToken: Ct));
        Assert.Equal(1, edges);
    }

    [Fact]
    public async Task A_failing_edge_rolls_back_the_event()
    {
        var writer = new NpgsqlActivityWriter(postgres.ConnectionString);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var commitment = await InsertCommitmentAsync(connection, Guid.NewGuid().ToString("N"));

        var marker = $"atomic-{Guid.NewGuid():N}";

        // Two identical edges violate the (event, subject, relation) unique index —
        // the second insert fails mid-transaction.
        await Assert.ThrowsAsync<PostgresException>(async () => await writer.WriteAsync(
            ActivityWithText(marker),
            [
                new SubjectEventEdge(commitment, SubjectEventRelations.Violates, Provenances.Declared),
                new SubjectEventEdge(commitment, SubjectEventRelations.Violates, Provenances.Declared)
            ],
            Ct));

        // The event never landed: the whole write rolled back.
        var events = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE payload->>'text' = @marker;",
            new { marker }, cancellationToken: Ct));
        Assert.Equal(0, events);
    }
}

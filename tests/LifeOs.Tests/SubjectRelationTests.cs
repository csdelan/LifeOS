using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies the subject and relation source tables from migration 0003: every
/// subject type inserts with no DDL, relations are constrained to valid kinds
/// and existing subjects, and origin_event_id defaults NULL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SubjectRelationTests(PostgresFixture postgres)
{
    private static readonly string[] AllSubjectTypes =
    [
        "Value", "Goal", "Problem", "Project", "Task", "Commitment",
        "Decision", "Idea", "Person", "Constraint", "Season"
    ];

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<Guid> InsertSubjectAsync(
        NpgsqlConnection connection,
        string type,
        string urn,
        string title = "untitled",
        string attributes = "{}",
        Guid? originEventId = null)
    {
        const string sql = """
            INSERT INTO bsk.subject (urn, type, title, attributes, origin_event_id)
            VALUES (@urn, @type, @title, @attributes::jsonb, @originEventId)
            RETURNING id;
            """;
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            sql, new { urn, type, title, attributes, originEventId }, cancellationToken: Ct));
    }

    [Fact]
    public async Task All_eleven_subject_types_insert_with_no_ddl_change()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");

        foreach (var type in AllSubjectTypes)
        {
            // A Value must carry a statement (migration 0010); the rest insert bare.
            var attributes = type == "Value" ? """{"statement": "identity"}""" : "{}";
            await InsertSubjectAsync(
                connection, type, $"urn:bsk:{type.ToLowerInvariant()}:{ns}", attributes: attributes);
        }

        var distinctTypes = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(DISTINCT type) FROM bsk.subject WHERE urn LIKE @pattern;",
            new { pattern = $"%:{ns}" }, cancellationToken: Ct));

        Assert.Equal(AllSubjectTypes.Length, distinctTypes);
    }

    [Fact]
    public async Task Cross_cutting_attributes_live_in_jsonb()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");

        await InsertSubjectAsync(connection, "Commitment", $"urn:bsk:commitment:{ns}",
            attributes: """{"expected_cadence": "weekly", "next_review_at": "2026-09-01T00:00:00Z"}""");
        await InsertSubjectAsync(connection, "Constraint", $"urn:bsk:constraint:{ns}",
            attributes: """{"scope": "capacity"}""");

        var cadence = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT attributes->>'expected_cadence' FROM bsk.subject WHERE urn = @urn;",
            new { urn = $"urn:bsk:commitment:{ns}" }, cancellationToken: Ct));
        var scope = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT attributes->>'scope' FROM bsk.subject WHERE urn = @urn;",
            new { urn = $"urn:bsk:constraint:{ns}" }, cancellationToken: Ct));

        Assert.Equal("weekly", cadence);
        Assert.Equal("capacity", scope);
    }

    [Fact]
    public async Task Duplicate_urn_is_rejected()
    {
        await using var connection = await OpenAsync();
        var urn = $"urn:bsk:goal:{Guid.NewGuid():N}";
        await InsertSubjectAsync(connection, "Goal", urn);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertSubjectAsync(connection, "Goal", urn));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
    }

    [Fact]
    public async Task Origin_event_id_defaults_null_and_can_reference_an_event()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");

        var plainId = await InsertSubjectAsync(connection, "Idea", $"urn:bsk:idea:{ns}");
        var plainOrigin = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT origin_event_id FROM bsk.subject WHERE id = @id;",
            new { id = plainId }, cancellationToken: Ct));
        Assert.Null(plainOrigin);

        var eventId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id)
            VALUES ('idea_session', 'declared', now(), @sourceId)
            RETURNING id;
            """,
            new { sourceId = ns }, cancellationToken: Ct));

        var promotedId = await InsertSubjectAsync(connection, "Idea", $"urn:bsk:idea:promoted:{ns}",
            originEventId: eventId);
        var promotedOrigin = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT origin_event_id FROM bsk.subject WHERE id = @id;",
            new { id = promotedId }, cancellationToken: Ct));
        Assert.Equal(eventId, promotedOrigin);
    }

    [Fact]
    public async Task Relation_between_valid_subjects_with_a_valid_kind_is_accepted()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var task = await InsertSubjectAsync(connection, "Task", $"urn:bsk:task:{ns}");
        var goal = await InsertSubjectAsync(connection, "Goal", $"urn:bsk:goal:{ns}");

        var relationId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
            VALUES (@from, 'serves', @to, 'declared')
            RETURNING id;
            """,
            new { from = task, to = goal }, cancellationToken: Ct));

        Assert.NotEqual(Guid.Empty, relationId);
    }

    [Fact]
    public async Task Relation_with_an_unknown_kind_is_rejected()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var a = await InsertSubjectAsync(connection, "Task", $"urn:bsk:task:{ns}");
        var b = await InsertSubjectAsync(connection, "Goal", $"urn:bsk:goal:{ns}");

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
                VALUES (@from, 'not_a_relation', @to, 'declared');
                """,
                new { from = a, to = b }, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Theory]
    [InlineData("concerns")]
    [InlineData("evidences")]
    [InlineData("violates")]
    public async Task Subject_relation_now_rejects_event_oriented_kinds(string relation)
    {
        // After 0007 these are event -> subject edges and belong in subject_event;
        // the narrowed CHECK must refuse them here.
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var a = await InsertSubjectAsync(connection, "Task", $"urn:bsk:task:{ns}");
        var b = await InsertSubjectAsync(connection, "Commitment", $"urn:bsk:commitment:{ns}");

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
                VALUES (@from, @relation, @to, 'declared');
                """,
                new { from = a, relation, to = b }, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Promoted_from_is_no_longer_a_relation_kind()
    {
        // promoted_from is represented by subject.origin_event_id, not an edge.
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var a = await InsertSubjectAsync(connection, "Project", $"urn:bsk:project:{ns}");
        // Problem is reuse-by-title (unique on title); keep it unique to this test.
        var b = await InsertSubjectAsync(connection, "Problem", $"urn:bsk:problem:{ns}", title: $"promoted-from {ns}");

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
                VALUES (@from, 'promoted_from', @to, 'declared');
                """,
                new { from = a, to = b }, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Relation_to_a_nonexistent_subject_is_rejected()
    {
        await using var connection = await OpenAsync();
        var from = await InsertSubjectAsync(connection, "Task", $"urn:bsk:task:{Guid.NewGuid():N}");

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
                VALUES (@from, 'serves', @to, 'declared');
                """,
                new { from, to = Guid.NewGuid() }, cancellationToken: Ct)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }
}

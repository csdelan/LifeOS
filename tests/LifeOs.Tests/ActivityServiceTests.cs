using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk log activity</c> (M3.5): an activity event is written, its evidences /
/// violates linkage to a Commitment is recorded in the payload with the correct
/// kind, and a commitment with a violating activity is detectable by the same SQL a
/// breach diagnostic would use. Non-Commitment targets are rejected.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ActivityServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    [Fact]
    public async Task Activity_evidences_a_commitment_and_is_recorded_in_the_payload()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var activities = provider.GetRequiredService<ActivityService>();
        var commitment = await subjects.CreateAsync(SubjectTypes.Commitment, $"train 3x a week {Guid.NewGuid():N}", cancellationToken: Ct);

        var result = await activities.LogAsync(
            new ActivityInput("ran 5k this morning", Evidences: [commitment.Urn]), Ct);

        Assert.Contains(commitment.Id, result.EvidencesCommitmentIds);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleAsync<(string Kind, string Text, bool Evidenced)>(
            new CommandDefinition(
                """
                SELECT kind,
                       payload->>'text',
                       (payload->'evidences' @> to_jsonb(@commitment::text)) AS Evidenced
                FROM bsk.event WHERE id = @id;
                """,
                new { id = result.EventId, commitment = commitment.Id }, cancellationToken: Ct));

        Assert.Equal(EventKinds.Activity, row.Kind);
        Assert.Equal("ran 5k this morning", row.Text);
        Assert.True(row.Evidenced);
    }

    [Fact]
    public async Task A_commitment_with_a_violating_activity_is_detectable()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var activities = provider.GetRequiredService<ActivityService>();
        var commitment = await subjects.CreateAsync(
            SubjectTypes.Commitment, $"never add to a losing position {Guid.NewGuid():N}", cancellationToken: Ct);

        await activities.LogAsync(
            new ActivityInput("doubled down on the losing trade", Violates: [commitment.Urn]), Ct);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);

        // The shape a breach diagnostic (M4.3) uses: activity events whose payload
        // marks them as violating the commitment.
        var breaches = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT count(*) FROM bsk.event
            WHERE kind = 'activity' AND payload->'violates' @> to_jsonb(@commitment::text);
            """,
            new { commitment = commitment.Id }, cancellationToken: Ct));

        Assert.Equal(1, breaches);
    }

    [Fact]
    public async Task An_activity_can_be_logged_without_any_commitment_linkage()
    {
        var activities = Provider().GetRequiredService<ActivityService>();

        var result = await activities.LogAsync(new ActivityInput("tidied the inbox"), Ct);

        Assert.Empty(result.EvidencesCommitmentIds);
        Assert.Empty(result.ViolatesCommitmentIds);
        Assert.NotEqual(Guid.Empty, result.EventId);
    }

    [Fact]
    public async Task Evidencing_a_non_commitment_is_rejected()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var activities = provider.GetRequiredService<ActivityService>();
        var goal = await subjects.CreateAsync(SubjectTypes.Goal, $"get fit {Guid.NewGuid():N}", cancellationToken: Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await activities.LogAsync(new ActivityInput("went running", Evidences: [goal.Urn]), Ct));
    }
}

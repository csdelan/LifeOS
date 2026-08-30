using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk decide</c> (M3.4): a Decision persists with its prediction fields even
/// when it results in nothing; it can result in a subject; it can supersede a prior
/// Decision (reversal trail); and its review date is stored.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DecisionServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    [Fact]
    public async Task A_decision_that_results_in_nothing_still_persists_with_its_prediction()
    {
        var decisions = Provider().GetRequiredService<DecisionService>();

        var result = await decisions.DecideAsync(new DecisionInput(
            Title: $"do not hire a second dev yet {Guid.NewGuid():N}",
            Alternatives: ["hire now", "hire in Q3"],
            Rationale: "runway is tight",
            ExpectedOutcome: "we ship M4 without new headcount",
            Confidence: "0.6",
            ReviewAt: "2026-11-01T00:00:00Z"), Ct);

        Assert.Null(result.ResultsInSubjectId);
        Assert.Null(result.SupersedesSubjectId);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleAsync<(string Type, string Expected, string Confidence, string ReviewAt)>(
            new CommandDefinition(
                """
                SELECT type,
                       attributes->>'expected_outcome',
                       attributes->>'confidence',
                       attributes->>'next_review_at'
                FROM bsk.subject WHERE id = @id;
                """,
                new { id = result.Decision.Id }, cancellationToken: Ct));

        Assert.Equal(SubjectTypes.Decision, row.Type);
        Assert.Equal("we ship M4 without new headcount", row.Expected);
        Assert.Equal("0.6", row.Confidence);
        Assert.Equal("2026-11-01T00:00:00Z", row.ReviewAt);

        // No relations for a decision that resulted in nothing.
        var edges = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @id;",
            new { id = result.Decision.Id }, cancellationToken: Ct));
        Assert.Equal(0, edges);
    }

    [Fact]
    public async Task A_decision_can_result_in_a_subject()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var decisions = provider.GetRequiredService<DecisionService>();
        var project = await subjects.CreateAsync(SubjectTypes.Project, $"migrate the db {Guid.NewGuid():N}", cancellationToken: Ct);

        var result = await decisions.DecideAsync(new DecisionInput(
            Title: $"adopt Postgres {Guid.NewGuid():N}", ResultsIn: project.Urn), Ct);

        Assert.Equal(project.Id, result.ResultsInSubjectId);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var relation = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT relation FROM bsk.subject_relation WHERE from_subject = @from AND to_subject = @to;",
            new { from = result.Decision.Id, to = project.Id }, cancellationToken: Ct));
        Assert.Equal(SubjectRelations.ResultsIn, relation);
    }

    [Fact]
    public async Task A_decision_can_supersede_a_prior_decision()
    {
        var decisions = Provider().GetRequiredService<DecisionService>();
        var prior = await decisions.DecideAsync(new DecisionInput($"use MySQL {Guid.NewGuid():N}"), Ct);

        var reversal = await decisions.DecideAsync(new DecisionInput(
            Title: $"switch to Postgres {Guid.NewGuid():N}", Supersedes: prior.Decision.Urn), Ct);

        Assert.Equal(prior.Decision.Id, reversal.SupersedesSubjectId);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var relation = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT relation FROM bsk.subject_relation WHERE from_subject = @from AND to_subject = @to;",
            new { from = reversal.Decision.Id, to = prior.Decision.Id }, cancellationToken: Ct));
        Assert.Equal(SubjectRelations.Supersedes, relation);
    }
}

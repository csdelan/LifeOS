using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies <c>bsk ideas</c> behaviour: one invocation stores exactly one
/// idea_session event referencing a Problem, and the individual ideas are not
/// created as subjects.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IdeaSessionTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task One_invocation_writes_exactly_one_idea_session_with_all_ideas()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var capture = provider.GetRequiredService<CaptureService>();

        var ns = Guid.NewGuid().ToString("N");
        var problem = await subjects.ResolveOrCreateAsync(SubjectTypes.Problem, $"ship faster {ns}", Ct);
        string[] ideas = [$"idea-alpha-{ns}", $"idea-beta-{ns}", $"idea-gamma-{ns}"];

        var session = await capture.CaptureIdeaSessionAsync(problem.Subject.Id, problem.Subject.Title, ideas, Ct);

        await using var connection = await OpenAsync();

        // Exactly one idea_session event for this problem, and it holds all ideas.
        var eventCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'idea_session' AND payload->>'subject_id' = @pid;",
            new { pid = problem.Subject.Id.ToString() }, cancellationToken: Ct));
        Assert.Equal(1, eventCount);

        var ideaCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT jsonb_array_length(payload->'ideas') FROM bsk.event WHERE id = @id;",
            new { id = session.EventId }, cancellationToken: Ct));
        Assert.Equal(ideas.Length, ideaCount);
    }

    [Fact]
    public async Task Individual_ideas_are_not_created_as_subjects()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var capture = provider.GetRequiredService<CaptureService>();

        var ns = Guid.NewGuid().ToString("N");
        var problem = await subjects.ResolveOrCreateAsync(SubjectTypes.Problem, $"reduce churn {ns}", Ct);
        string[] ideas = [$"call-users-{ns}", $"fix-onboarding-{ns}"];

        await capture.CaptureIdeaSessionAsync(problem.Subject.Id, problem.Subject.Title, ideas, Ct);

        await using var connection = await OpenAsync();
        var subjectsNamedLikeIdeas = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE title = ANY(@ideas);",
            new { ideas }, cancellationToken: Ct));

        Assert.Equal(0, subjectsNamedLikeIdeas);
    }

    [Fact]
    public async Task Re_running_against_the_same_problem_reuses_it()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var capture = provider.GetRequiredService<CaptureService>();

        var title = $"same problem {Guid.NewGuid():N}";

        var first = await subjects.ResolveOrCreateAsync(SubjectTypes.Problem, title, Ct);
        await capture.CaptureIdeaSessionAsync(first.Subject.Id, first.Subject.Title, ["one"], Ct);

        var second = await subjects.ResolveOrCreateAsync(SubjectTypes.Problem, title, Ct);
        await capture.CaptureIdeaSessionAsync(second.Subject.Id, second.Subject.Title, ["two"], Ct);

        Assert.Equal(first.Subject.Id, second.Subject.Id);

        await using var connection = await OpenAsync();
        var problemCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE type = 'Problem' AND title = @title;",
            new { title }, cancellationToken: Ct));
        Assert.Equal(1, problemCount);

        var sessionCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'idea_session' AND payload->>'subject_id' = @pid;",
            new { pid = first.Subject.Id.ToString() }, cancellationToken: Ct));
        Assert.Equal(2, sessionCount);
    }

    [Fact]
    public async Task An_idea_session_needs_at_least_one_idea()
    {
        var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await capture.CaptureIdeaSessionAsync(Guid.NewGuid(), "empty", [], Ct));
    }
}

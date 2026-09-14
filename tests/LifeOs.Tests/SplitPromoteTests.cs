using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Phase 7 (D4 split promote): "promote" splits by source. An event promotes into a
/// subject (origin_event_id); a subject — the Idea case — promotes into new work via
/// a results_in edge and advances to Promoted. Either way the source resolves out of
/// the inbox. Also covers Problem reuse-by-title on (re-)creation (Decision 1) and the
/// atomicity of the incoming-edge create.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SplitPromoteTests(PostgresFixture postgres)
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

    private async Task<bool> InInboxAsync(NpgsqlConnection c, Guid id)
        => await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.v_inbox WHERE item_id = @id);", new { id }, cancellationToken: Ct));

    private async Task<bool> EdgeExistsAsync(NpgsqlConnection c, Guid from, string rel, Guid to)
        => await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.subject_relation WHERE from_subject=@from AND relation=@rel AND to_subject=@to);",
            new { from, rel, to }, cancellationToken: Ct));

    private async Task<string?> FoldedStatusAsync(NpgsqlConnection c, Guid id)
        => await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current_source WHERE subject_id = @id;",
            new { id }, cancellationToken: Ct));

    [Fact]
    public async Task Idea_promote_creates_target_links_it_and_marks_the_idea_promoted()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var triage = provider.GetRequiredService<TriageService>();
        var promotion = provider.GetRequiredService<PromotionService>();
        var ns = Guid.NewGuid().ToString("N");

        var idea = await subjects.CreateAsync(SubjectTypes.Idea, $"a bright idea {ns}", cancellationToken: Ct);
        await triage.FlagItemAsync(isEvent: false, idea.Id, Ct);

        var result = await promotion.PromoteSubjectAsync(idea.Urn, SubjectTypes.Goal, $"the goal {ns}", Ct);
        // Mirror the CLI: promote resolves the source out of the inbox.
        await triage.SetStateAsync(isEvent: false, idea.Id, TriageStates.Promoted, Ct);

        await using var connection = await OpenAsync();
        Assert.True(await EdgeExistsAsync(connection, idea.Id, SubjectRelations.ResultsIn, result.Target.Id));
        Assert.Equal("Promoted", await FoldedStatusAsync(connection, idea.Id));
        Assert.False(await InInboxAsync(connection, idea.Id));
    }

    [Fact]
    public async Task Promoting_a_non_idea_subject_does_not_change_its_status()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var promotion = provider.GetRequiredService<PromotionService>();
        var ns = Guid.NewGuid().ToString("N");

        // A Problem that produces work is not resolved by that alone (GEN-14).
        var problem = await subjects.CreateAsync(SubjectTypes.Problem, $"a real problem {ns}", cancellationToken: Ct);
        var result = await promotion.PromoteSubjectAsync(problem.Urn, SubjectTypes.Task, $"do the thing {ns}", Ct);

        await using var connection = await OpenAsync();
        Assert.True(await EdgeExistsAsync(connection, problem.Id, SubjectRelations.ResultsIn, result.Target.Id));
        Assert.Null(await FoldedStatusAsync(connection, problem.Id)); // untouched
    }

    [Fact]
    public async Task Event_promote_resolves_the_captures_inbox_triage()
    {
        var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();
        var triage = provider.GetRequiredService<TriageService>();
        var promotion = provider.GetRequiredService<PromotionService>();

        var captured = await capture.CaptureNoteAsync($"promote me {Guid.NewGuid():N}", Ct);
        await triage.FlagItemAsync(isEvent: true, captured.EventId, Ct);

        var result = await promotion.PromoteAsync(captured.EventId, SubjectTypes.Project, $"real project {Guid.NewGuid():N}", Ct);
        await triage.SetStateAsync(isEvent: true, captured.EventId, TriageStates.Promoted, Ct);

        Assert.Equal(captured.EventId, result.OriginEventId);
        await using var connection = await OpenAsync();
        Assert.False(await InInboxAsync(connection, captured.EventId));
    }

    [Fact]
    public async Task Recreating_a_problem_by_title_reuses_the_existing_one()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var title = $"recurring problem {Guid.NewGuid():N}";

        var first = await subjects.ResolveOrCreateAsync(
            SubjectTypes.Problem, title, """{"impact":"high"}""", Ct);
        var second = await subjects.ResolveOrCreateAsync(
            SubjectTypes.Problem, title, """{"impact":"low"}""", Ct);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subject.Id, second.Subject.Id);

        // Attributes came from the first (creating) call; the reuse did not overwrite them.
        await using var connection = await OpenAsync();
        var impact = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT attributes->>'impact' FROM bsk.subject WHERE id = @id;",
            new { id = first.Subject.Id }, cancellationToken: Ct));
        Assert.Equal("high", impact);
    }

    [Fact]
    public async Task A_failed_incoming_edge_leaves_no_orphan_target()
    {
        var repository = new NpgsqlSubjectRepository(postgres.ConnectionString);
        var urn = $"urn:bsk:goal:orphan-target-{Guid.NewGuid():N}";
        var target = new NewSubject(urn, SubjectTypes.Goal, "orphan target", "{}", OriginEventId: null);

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await repository.CreateWithIncomingEdgeAsync(
                target, SubjectRelations.ResultsIn, Guid.NewGuid(), Provenances.Declared, Ct));

        await using var connection = await OpenAsync();
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.subject WHERE urn = @urn);", new { urn }, cancellationToken: Ct));
        Assert.False(exists);
    }
}

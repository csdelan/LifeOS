using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk promote</c> (M3.3): promotion creates exactly one new subject with
/// origin_event_id set and leaves the source event byte-identical. The origin
/// column is the promotion link; there is no promoted_from edge (see #24).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PromotionServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    // A canonical snapshot of a single event row, for byte-identity comparison.
    private async Task<string> EventSnapshotAsync(NpgsqlConnection connection, Guid id)
        => await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT to_jsonb(e.*)::text FROM bsk.event e WHERE id = @id;",
            new { id }, cancellationToken: Ct)) ?? string.Empty;

    [Fact]
    public async Task Promotion_creates_a_subject_with_origin_and_leaves_the_source_untouched()
    {
        var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();
        var promotion = provider.GetRequiredService<PromotionService>();

        var source = await capture.CaptureJournalAsync(
            $"a long journal entry worth acting on {Guid.NewGuid():N}", Ct);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var before = await EventSnapshotAsync(connection, source.EventId);

        var result = await promotion.PromoteAsync(
            source.EventId, SubjectTypes.Project, $"the project {Guid.NewGuid():N}", Ct);

        // Exactly one new subject, carrying origin_event_id.
        var origin = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT origin_event_id FROM bsk.subject WHERE id = @id;",
            new { id = result.Subject.Id }, cancellationToken: Ct));
        Assert.Equal(source.EventId, origin);

        var subjectsForEvent = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE origin_event_id = @id;",
            new { id = source.EventId }, cancellationToken: Ct));
        Assert.Equal(1, subjectsForEvent);

        // The source event is byte-identical.
        var after = await EventSnapshotAsync(connection, source.EventId);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Promotion_records_the_origin_and_no_promoted_from_edge()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var capture = provider.GetRequiredService<CaptureService>();
        var promotion = provider.GetRequiredService<PromotionService>();

        var problem = await subjects.ResolveOrCreateAsync(
            SubjectTypes.Problem, $"how to focus {Guid.NewGuid():N}", Ct);
        var session = await capture.CaptureIdeaSessionAsync(
            problem.Subject.Id, problem.Subject.Title, ["time-box mornings", "phone in a drawer"], Ct);

        var result = await promotion.PromoteAsync(
            session.EventId, SubjectTypes.Project, $"morning focus system {Guid.NewGuid():N}", Ct);

        // The promotion link is origin_event_id (subject -> event) — not a relation
        // edge. The source subject stays reachable via the event's payload.
        Assert.Equal(session.EventId, result.OriginEventId);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var edges = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @from;",
            new { from = result.Subject.Id }, cancellationToken: Ct));
        Assert.Equal(0, edges);
    }

    private async Task<List<string>> TagsAsync(string column, Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return (await connection.QueryAsync<string>(new CommandDefinition(
            $"SELECT tag FROM bsk.item_tag WHERE {column} = @id ORDER BY tag;",
            new { id }, cancellationToken: Ct))).ToList();
    }

    [Fact]
    public async Task Promoting_a_tagged_capture_carries_its_tags_to_the_new_subject()
    {
        var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();
        var tags = provider.GetRequiredService<TagService>();
        var promotion = provider.GetRequiredService<PromotionService>();

        var source = await capture.CaptureNoteAsync($"a tagged thought {Guid.NewGuid():N}", Ct);
        await tags.ApplyAsync(source.EventId.ToString(), ["errand", "Home"], [], Ct);

        var result = await promotion.PromoteAsync(
            source.EventId, SubjectTypes.Task, $"the errand {Guid.NewGuid():N}", Ct);

        Assert.Equal(["errand", "home"], await TagsAsync("subject_id", result.Subject.Id));
        // The capture keeps its own classification; promotion copies, it does not move.
        Assert.Equal(["errand", "home"], await TagsAsync("event_id", source.EventId));
    }

    [Fact]
    public async Task Promoting_a_tagged_idea_carries_its_tags_to_the_new_work()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var tags = provider.GetRequiredService<TagService>();
        var promotion = provider.GetRequiredService<PromotionService>();

        var idea = await subjects.CreateAsync(
            SubjectTypes.Idea, $"a tagged idea {Guid.NewGuid():N}", cancellationToken: Ct);
        await tags.ApplyAsync(idea.Urn, ["garden"], [], Ct);

        var result = await promotion.PromoteSubjectAsync(
            idea.Urn, SubjectTypes.Project, $"the garden project {Guid.NewGuid():N}", Ct);

        Assert.Equal(["garden"], await TagsAsync("subject_id", result.Target.Id));
    }

    [Fact]
    public async Task Promoting_from_an_unknown_event_is_an_error()
    {
        var promotion = Provider().GetRequiredService<PromotionService>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await promotion.PromoteAsync(Guid.NewGuid(), SubjectTypes.Idea, "orphan", Ct));
    }
}

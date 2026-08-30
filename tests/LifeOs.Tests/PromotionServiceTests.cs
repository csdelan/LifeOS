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
/// origin_event_id set, leaves the source event byte-identical, and records a
/// promoted_from relation to the subject the source event named.
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
    public async Task Promoting_from_an_idea_session_links_promoted_from_to_the_problem()
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

        Assert.Equal(problem.Subject.Id, result.PromotedFromSubjectId);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleAsync<(Guid ToSubject, string Provenance)>(
            new CommandDefinition(
                """
                SELECT to_subject, provenance FROM bsk.relation
                WHERE from_subject = @from AND relation = 'promoted_from';
                """,
                new { from = result.Subject.Id }, cancellationToken: Ct));

        Assert.Equal(problem.Subject.Id, row.ToSubject);
        Assert.Equal(Provenances.Derived, row.Provenance);
    }

    [Fact]
    public async Task Promoting_from_an_unknown_event_is_an_error()
    {
        var promotion = Provider().GetRequiredService<PromotionService>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await promotion.PromoteAsync(Guid.NewGuid(), SubjectTypes.Idea, "orphan", Ct));
    }
}

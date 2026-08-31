using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk relate</c>'s service: filing an existing capture against a subject with a
/// <c>concerns</c> edge. Relating is idempotent, and an unknown event is an error.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RelateServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<int> EdgeCount(Guid eventId, Guid subjectId, string relation)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*) FROM bsk.subject_event
            WHERE event_id = @eventId AND subject_id = @subjectId AND relation = @relation;
            """,
            new { eventId, subjectId, relation }, cancellationToken: Ct));
    }

    [Fact]
    public async Task Relates_a_capture_to_a_subject_and_is_idempotent()
    {
        await using var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relate = provider.GetRequiredService<RelateService>();

        var note = await capture.CaptureNoteAsync($"a passing thought {Guid.NewGuid():N}", Ct);
        var project = await subjects.CreateAsync(
            SubjectTypes.Project, $"Reporting {Guid.NewGuid():N}", cancellationToken: Ct);

        var first = await relate.RelateAsync(note.EventId, project.Urn, SubjectEventRelations.Concerns, Ct);
        Assert.True(first.Created);
        Assert.Equal(project.Id, first.Subject.Id);
        Assert.Equal(1, await EdgeCount(note.EventId, project.Id, SubjectEventRelations.Concerns));

        var second = await relate.RelateAsync(note.EventId, project.Urn, SubjectEventRelations.Concerns, Ct);
        Assert.False(second.Created);
        Assert.Equal(1, await EdgeCount(note.EventId, project.Id, SubjectEventRelations.Concerns));
    }

    [Fact]
    public async Task Relating_an_unknown_event_is_an_error()
    {
        await using var provider = Provider();
        var relate = provider.GetRequiredService<RelateService>();
        var subjects = provider.GetRequiredService<SubjectService>();

        var project = await subjects.CreateAsync(
            SubjectTypes.Project, $"target {Guid.NewGuid():N}", cancellationToken: Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await relate.RelateAsync(Guid.NewGuid(), project.Urn, SubjectEventRelations.Concerns, Ct));
    }
}

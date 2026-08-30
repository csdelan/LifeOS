using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Rebuild;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>A clock the test advances by hand, so event ordering is deterministic.</summary>
file sealed class AdvancingClock(DateTimeOffset start) : IClock
{
    private DateTimeOffset _now = start;

    public DateTimeOffset UtcNow
    {
        get
        {
            var current = _now;
            _now = _now.AddSeconds(1);
            return current;
        }
    }
}

/// <summary>
/// <c>bsk status</c> (M3.2): a status change writes a <c>state_change</c> event and
/// nothing more — the <c>subject_current</c> projection reflects it only after a
/// rebuild folds the event in, never by a direct write (epic invariant 8).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StatusServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    [Fact]
    public async Task Status_writes_a_state_change_event_and_the_projection_reflects_it_after_rebuild()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var status = provider.GetRequiredService<StatusService>();
        var rebuilder = new DerivedRebuilder(postgres.ConnectionString);
        var ns = Guid.NewGuid().ToString("N");

        var project = await subjects.CreateAsync(SubjectTypes.Project, $"launch {ns}", cancellationToken: Ct);

        var result = await status.ChangeStatusAsync(project.Urn, "active", Ct);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);

        // A state_change event exists, tagged to this subject.
        var stored = await connection.QuerySingleAsync<(string Kind, string SubjectId, string Status)>(
            new CommandDefinition(
                "SELECT kind, payload->>'subject_id', payload->>'status' FROM bsk.event WHERE id = @id;",
                new { id = result.EventId }, cancellationToken: Ct));
        Assert.Equal(EventKinds.StateChange, stored.Kind);
        Assert.Equal(project.Id.ToString(), stored.SubjectId);
        Assert.Equal("active", stored.Status);

        // The projection does NOT reflect it until a rebuild runs.
        var beforeRebuild = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current WHERE subject_id = @id;",
            new { id = project.Id }, cancellationToken: Ct));
        Assert.Null(beforeRebuild);

        await rebuilder.RebuildAsync(Ct);

        var afterRebuild = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current WHERE subject_id = @id;",
            new { id = project.Id }, cancellationToken: Ct));
        Assert.Equal("active", afterRebuild);
    }

    [Fact]
    public async Task The_latest_status_change_wins_after_rebuild()
    {
        // A hand-advanced clock so the two state_change events have distinct,
        // ordered timestamps and "latest wins" is deterministic, not a race.
        var provider = new ServiceCollection()
            .AddLifeOsKernel(postgres.ConnectionString)
            .AddSingleton<IClock>(new AdvancingClock(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)))
            .BuildServiceProvider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var status = provider.GetRequiredService<StatusService>();
        var rebuilder = new DerivedRebuilder(postgres.ConnectionString);
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"pack {ns}", cancellationToken: Ct);
        await status.ChangeStatusAsync(task.Urn, "open", Ct);
        await status.ChangeStatusAsync(task.Urn, "done", Ct);

        await rebuilder.RebuildAsync(Ct);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var current = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current WHERE subject_id = @id;",
            new { id = task.Id }, cancellationToken: Ct));

        Assert.Equal("done", current);
    }
}

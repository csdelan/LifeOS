using System.Text.Json;
using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// D9 (migration 0012): the universal archive flag. Archive/restore is recorded as
/// an append-only <c>archive_change</c> event and folded on read by
/// <c>bsk.is_archived</c> (Option B) — reversible, orthogonal to status, never a
/// delete. These tests exercise the fold (newest wins), the reader column, the
/// append-only history, and the exclusion of archived subjects from a diagnostic.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ArchiveServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private DiagnosticRunner Runner => new(postgres.ConnectionString);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<bool> IsArchivedAsync(NpgsqlConnection connection, Guid id)
        => await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT bsk.is_archived(@id);", new { id }, cancellationToken: Ct));

    private async Task<bool> ViewArchivedAsync(NpgsqlConnection connection, Guid id)
        => await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT archived FROM bsk.v_subject WHERE id = @id;", new { id }, cancellationToken: Ct));

    [Fact]
    public async Task Never_archived_subject_is_active()
    {
        var provider = Provider();
        var subject = await provider.GetRequiredService<SubjectService>()
            .CreateAsync(SubjectTypes.Goal, $"active goal {Guid.NewGuid():N}", cancellationToken: Ct);

        await using var connection = await OpenAsync();
        Assert.False(await IsArchivedAsync(connection, subject.Id));
        Assert.False(await ViewArchivedAsync(connection, subject.Id));
    }

    [Fact]
    public async Task Archive_then_restore_then_archive_folds_newest_wins()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var archive = provider.GetRequiredService<ArchiveService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Value, $"identity {Guid.NewGuid():N}",
            attributesJson: """{"statement":"I am someone who tests archive."}""", cancellationToken: Ct);

        await using var connection = await OpenAsync();

        await archive.ArchiveAsync(subject.Urn, Ct);
        Assert.True(await IsArchivedAsync(connection, subject.Id));
        Assert.True(await ViewArchivedAsync(connection, subject.Id));

        await archive.RestoreAsync(subject.Urn, Ct);
        Assert.False(await IsArchivedAsync(connection, subject.Id));
        Assert.False(await ViewArchivedAsync(connection, subject.Id));

        await archive.ArchiveAsync(subject.Urn, Ct);
        Assert.True(await IsArchivedAsync(connection, subject.Id));
    }

    [Fact]
    public async Task Archive_and_restore_are_append_only_events_that_retain_history()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var archive = provider.GetRequiredService<ArchiveService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Task, $"history task {Guid.NewGuid():N}", cancellationToken: Ct);

        var archived = await archive.ArchiveAsync(subject.Urn, Ct);
        await archive.RestoreAsync(subject.Urn, Ct);

        await using var connection = await OpenAsync();

        // Two archive_change events remain — the flag's whole history is preserved.
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT count(*) FROM bsk.event
            WHERE kind = 'archive_change' AND payload->>'subject_id' = @id;
            """,
            new { id = subject.Id.ToString() }, cancellationToken: Ct));
        Assert.Equal(2, count);

        // The original archive event is untouched (append-only source, invariant 3/5).
        var firstArchived = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT payload->>'archived' FROM bsk.event WHERE id = @id;",
            new { id = archived.EventId }, cancellationToken: Ct));
        Assert.Equal("true", firstArchived);
    }

    [Fact]
    public async Task Archived_subject_is_excluded_from_the_neglect_diagnostic()
    {
        var provider = Provider();
        var archive = provider.GetRequiredService<ArchiveService>();

        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");

        // A daily-cadenced subject created well in the past with no concerning
        // activity: neglect would flag it — until it is archived.
        var attrs = JsonSerializer.Serialize(new { expected_cadence = "daily" });
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title, attributes, created_at)
            VALUES (@urn, 'Goal', @title, @attrs::jsonb, @createdAt)
            RETURNING id;
            """,
            new
            {
                urn = $"urn:bsk:goal:{ns}",
                title = $"neglected {ns}",
                attrs,
                createdAt = DateTimeOffset.UtcNow.AddDays(-10)
            },
            cancellationToken: Ct));

        Assert.NotNull(await FindNeglectAsync(id));   // flagged before archiving

        await archive.ArchiveAsync($"urn:bsk:goal:{ns}", Ct);

        Assert.Null(await FindNeglectAsync(id));       // gone once archived
    }

    private async Task<Finding?> FindNeglectAsync(Guid subjectId)
    {
        var report = await Runner.RunAsync(only: "neglect", cancellationToken: Ct);
        var neglect = Assert.Single(report.Diagnostics);
        return neglect.Findings.FirstOrDefault(f => f.Subject.Id == subjectId);
    }
}

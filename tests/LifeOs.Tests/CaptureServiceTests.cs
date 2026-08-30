using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Exercises the M2.1 spine end to end: resolve CaptureService from the
/// composition root, capture a note, and confirm it reached Postgres through the
/// repository layer with the body stored as an artifact.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CaptureServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Capture_runs_service_to_repository_to_postgres_and_back()
    {
        await using var provider = new ServiceCollection()
            .AddLifeOsKernel(postgres.ConnectionString)
            .BuildServiceProvider();

        var capture = provider.GetRequiredService<CaptureService>();

        const string text = "buy milk and think about the roadmap";
        var result = await capture.CaptureNoteAsync(text, Ct);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);

        var row = await connection.QuerySingleAsync<(string Kind, string Provenance, string Source, Guid? ArtifactId)>(
            new CommandDefinition(
                "SELECT kind, provenance, source_id, artifact_id FROM bsk.event WHERE id = @id;",
                new { id = result.EventId }, cancellationToken: Ct));

        Assert.Equal("note", row.Kind);
        Assert.Equal("declared", row.Provenance);
        Assert.Equal("cli", row.Source);
        Assert.Equal(result.ArtifactId, row.ArtifactId);

        var storedContent = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT content FROM bsk.artifact WHERE id = @id;",
            new { id = result.ArtifactId }, cancellationToken: Ct));
        Assert.Equal(text, storedContent);
    }

    [Fact]
    public async Task Empty_capture_text_is_rejected_before_touching_the_store()
    {
        await using var provider = new ServiceCollection()
            .AddLifeOsKernel(postgres.ConnectionString)
            .BuildServiceProvider();

        var capture = provider.GetRequiredService<CaptureService>();

        await Assert.ThrowsAsync<ArgumentException>(async () => await capture.CaptureNoteAsync("   ", Ct));
    }
}

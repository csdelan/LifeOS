using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Cli.Editor;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>The editor round-trip, tested with an injected fake editor (no DB).</summary>
public sealed class EditorBufferTests
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_text_the_editor_wrote()
    {
        const string edited = "line one\nline two\n";

        var result = await EditorBuffer.EditAsync(
            async (path, ct) => await File.WriteAllTextAsync(path, edited, ct),
            cancellationToken: Ct);

        Assert.Equal(edited, result);
    }

    [Fact]
    public async Task Seeds_the_buffer_the_editor_opens()
    {
        var seenBySeededEditor = string.Empty;

        await EditorBuffer.EditAsync(
            async (path, ct) => seenBySeededEditor = await File.ReadAllTextAsync(path, ct),
            seed: "template",
            cancellationToken: Ct);

        Assert.Equal("template", seenBySeededEditor);
    }
}

/// <summary>Capture and journal behaviour against Postgres.</summary>
[Collection(PostgresCollection.Name)]
public sealed class CaptureAndJournalDbTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private CaptureService Capture()
    {
        var provider = new ServiceCollection()
            .AddLifeOsKernel(postgres.ConnectionString)
            .BuildServiceProvider();
        return provider.GetRequiredService<CaptureService>();
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task Journal_stores_the_full_text_as_one_journal_event()
    {
        const string text = "Dear diary,\n\nToday I built the capture path.\nIt has multiple lines.\n";
        var result = await Capture().CaptureJournalAsync(text, Ct);

        await using var connection = await OpenAsync();

        var kind = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT kind FROM bsk.event WHERE id = @id;", new { id = result.EventId }, cancellationToken: Ct));
        Assert.Equal("journal", kind);

        // Exactly one event carries this id, and the artifact holds the full text.
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE id = @id;", new { id = result.EventId }, cancellationToken: Ct));
        Assert.Equal(1, count);

        var content = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT content FROM bsk.artifact WHERE id = @id;", new { id = result.ArtifactId }, cancellationToken: Ct));
        Assert.Equal(text, content);
    }

    [Fact]
    public async Task Capture_creates_one_note_event_and_leaves_earlier_events_untouched()
    {
        var capture = Capture();

        var first = await capture.CaptureNoteAsync("first note", Ct);
        var second = await capture.CaptureNoteAsync("second note", Ct);

        Assert.NotEqual(first.EventId, second.EventId);

        await using var connection = await OpenAsync();

        // Both events exist and are notes.
        var kinds = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT kind FROM bsk.event WHERE id = ANY(@ids);",
            new { ids = new[] { first.EventId, second.EventId } }, cancellationToken: Ct))).ToList();
        Assert.Equal(["note", "note"], kinds.OrderBy(k => k).ToList());

        // The first capture's stored body is unchanged after the second capture.
        var firstContent = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT content FROM bsk.artifact WHERE id = @id;",
            new { id = first.ArtifactId }, cancellationToken: Ct));
        Assert.Equal("first note", firstContent);
    }
}

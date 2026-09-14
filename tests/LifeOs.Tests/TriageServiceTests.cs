using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// INBOX-1 (migration 0015): the triage marker and the v_inbox projection. Inbox
/// membership is asserted by an append-only triage event (newest-per-item wins),
/// covers subjects and events, resolves via drop/file, and hides archived subjects.
/// Also covers flag-on-capture — the mechanism the capture / new-Problem|Idea verbs
/// use to drop a fresh item into the inbox.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TriageServiceTests(PostgresFixture postgres)
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

    private async Task<bool> InInboxAsync(NpgsqlConnection connection, Guid itemId)
        => await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.v_inbox WHERE item_id = @id);",
            new { id = itemId }, cancellationToken: Ct));

    [Fact]
    public async Task Flagging_a_subject_puts_it_in_the_inbox_and_dropping_removes_it()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var triage = provider.GetRequiredService<TriageService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Problem, $"triage problem {Guid.NewGuid():N}", cancellationToken: Ct);

        await using var connection = await OpenAsync();

        await triage.FlagAsync(subject.Urn, Ct);
        Assert.True(await InInboxAsync(connection, subject.Id));

        await triage.DropAsync(subject.Urn, Ct);
        Assert.False(await InInboxAsync(connection, subject.Id));

        // Its row carries subject fields for the preview.
        var kind = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT subject_type FROM bsk.v_inbox WHERE item_id = @id;",
            new { id = subject.Id }, cancellationToken: Ct));
        Assert.Null(kind); // resolved out, so no row at all
    }

    [Fact]
    public async Task Newest_marker_wins_so_reflagging_after_a_drop_returns_it()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var triage = provider.GetRequiredService<TriageService>();
        var subject = await subjects.CreateAsync(
            SubjectTypes.Idea, $"reflag idea {Guid.NewGuid():N}", cancellationToken: Ct);

        await using var connection = await OpenAsync();

        await triage.FlagAsync(subject.Urn, Ct);
        await triage.FileAsync(subject.Urn, Ct);
        Assert.False(await InInboxAsync(connection, subject.Id));

        await triage.FlagAsync(subject.Urn, Ct);
        Assert.True(await InInboxAsync(connection, subject.Id));
    }

    [Fact]
    public async Task A_raw_capture_event_can_be_flagged_and_shows_its_content()
    {
        var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();
        var triage = provider.GetRequiredService<TriageService>();

        var text = $"an untriaged capture {Guid.NewGuid():N}";
        var captured = await capture.CaptureNoteAsync(text, Ct);

        var result = await triage.FlagAsync(captured.EventId.ToString(), Ct);
        Assert.True(result.Item.IsEvent);

        await using var connection = await OpenAsync();
        var row = await connection.QuerySingleAsync<(string ItemKind, string EventKind, string EventContent)>(
            new CommandDefinition(
                "SELECT item_kind, event_kind, event_content FROM bsk.v_inbox WHERE item_id = @id;",
                new { id = captured.EventId }, cancellationToken: Ct));

        Assert.Equal("event", row.ItemKind);
        Assert.Equal("note", row.EventKind);
        Assert.Equal(text, row.EventContent);
    }

    [Fact]
    public async Task An_archived_subject_is_hidden_from_the_inbox()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var triage = provider.GetRequiredService<TriageService>();
        var archive = provider.GetRequiredService<ArchiveService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Problem, $"archived inbox {Guid.NewGuid():N}", cancellationToken: Ct);

        await triage.FlagAsync(subject.Urn, Ct);
        await archive.ArchiveAsync(subject.Urn, Ct);

        await using var connection = await OpenAsync();
        Assert.False(await InInboxAsync(connection, subject.Id));
    }

    [Fact]
    public async Task Flag_on_capture_mechanism_lands_a_note_in_the_inbox()
    {
        // Mirrors what CaptureCommand does: capture, then flag the event.
        var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();
        var triage = provider.GetRequiredService<TriageService>();

        var captured = await capture.CaptureNoteAsync($"flag on capture {Guid.NewGuid():N}", Ct);
        await triage.FlagItemAsync(isEvent: true, captured.EventId, Ct);

        await using var connection = await OpenAsync();
        Assert.True(await InInboxAsync(connection, captured.EventId));
    }
}

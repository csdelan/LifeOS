using System.CommandLine;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Cli.Commands;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR63: a document or voice capture reaches the Inbox once it's stored, whatever the
/// caller does next, and sending the same file again finishes what the first send stopped before;
/// <c>bsk journal</c> writes an entry and its link to the item in one call.
/// </summary>
public sealed class CaptureFilingTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Kernel(Action<IServiceCollection>? more = null)
    {
        var services = classDb.Services();
        more?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static byte[] RandomFile()
    {
        var bytes = new byte[256];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private async Task<bool> InInboxAsync(Guid itemId)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.v_inbox WHERE item_id = @itemId);", new { itemId }, cancellationToken: Ct));
    }

    [Fact]
    public async Task A_document_whose_first_send_stopped_before_its_flag_reaches_the_Inbox_when_sent_again()
    {
        await using var kernel = Kernel();
        var bytes = RandomFile();
        // The first send stored the file and stopped there: the phone dropped.
        var first = await kernel.GetRequiredService<AttachmentService>().CaptureAttachmentAsync(bytes, "lease.pdf", cancellationToken: Ct);
        Assert.False(await InInboxAsync(first.EventId));

        var again = await kernel.GetRequiredService<InboxFilingService>()
            .CaptureDocumentAsync(new CaptureFile(bytes, "lease.pdf"), cancellationToken: Ct);

        Assert.True(again.Stored!.Deduplicated);
        Assert.Equal(first.EventId, again.EventId);
        Assert.True(await InInboxAsync(first.EventId));
    }

    [Fact]
    public async Task A_document_dealt_with_stays_dealt_with_when_sent_again()
    {
        await using var kernel = Kernel();
        var filing = kernel.GetRequiredService<InboxFilingService>();
        var bytes = RandomFile();
        var first = await filing.CaptureDocumentAsync(new CaptureFile(bytes, "receipt.png"), cancellationToken: Ct);
        Assert.True(await InInboxAsync(first.EventId));
        await kernel.GetRequiredService<TriageService>().DismissAsync(first.EventId.ToString(), Ct);

        await filing.CaptureDocumentAsync(new CaptureFile(bytes, "receipt.png"), cancellationToken: Ct);

        Assert.False(await InInboxAsync(first.EventId));
    }

    [Fact]
    public async Task A_voice_note_is_filed_and_linked_even_when_the_caller_cancels_once_it_is_stored()
    {
        using var caller = new CancellationTokenSource();
        await using var plain = Kernel();
        var store = new CancelAfterStore(plain.GetRequiredService<IArtifactBlobStore>(), caller);
        await using var kernel = Kernel(services => services.AddSingleton<IArtifactBlobStore>(store));
        var review = await kernel.GetRequiredService<SubjectService>()
            .CreateAsync(SubjectTypes.Project, $"Garden {Guid.NewGuid():N}", cancellationToken: Ct);

        var captured = await kernel.GetRequiredService<InboxFilingService>().CaptureVoiceAsync(
            new CaptureFile(RandomFile(), "memo.m4a"), "Plant the tulips before the frost.", keepAudio: true,
            concerning: review.Urn, cancellationToken: caller.Token);

        Assert.True(caller.IsCancellationRequested);
        Assert.True(await InInboxAsync(captured.EventId));
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.True(await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.subject_event WHERE event_id = @e AND subject_id = @s AND relation = 'concerns');",
            new { e = captured.EventId, s = review.Id }, cancellationToken: Ct)));
    }

    [Fact]
    public async Task Bsk_journal_writes_the_entry_with_its_item_or_nothing()
    {
        await using var kernel = Kernel();
        var item = await kernel.GetRequiredService<SubjectService>()
            .CreateAsync(SubjectTypes.Project, $"Kitchen {Guid.NewGuid():N}", cancellationToken: Ct);
        var text = $"Called the plumber {Guid.NewGuid():N}";

        Assert.Equal(0, await BskAsync("journal", item.Urn, "--text", text));
        Assert.NotEqual(0, await BskAsync("journal", $"No such item {Guid.NewGuid():N}", "--text", text + " again"));

        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        var entries = (await connection.QueryAsync<(string Content, Guid? Subject)>(new CommandDefinition(
            """
            SELECT a.content, se.subject_id
            FROM bsk.event e
            JOIN bsk.artifact a ON a.id = e.artifact_id
            LEFT JOIN bsk.subject_event se ON se.event_id = e.id AND se.relation = 'concerns'
            WHERE e.kind = 'journal' AND a.content LIKE @like;
            """,
            new { like = text + "%" }, cancellationToken: Ct))).ToList();
        Assert.Equal([(text, (Guid?)item.Id)], entries);
    }

    private Task<int> BskAsync(params string[] args)
    {
        var connection = new Option<string?>("--connection", "-c") { Recursive = true };
        var json = new Option<bool>("--json") { Recursive = true };
        var root = new RootCommand("bsk");
        root.Options.Add(connection);
        root.Options.Add(json);
        root.Subcommands.Add(JournalCommand.Create(connection, json));
        return root.Parse([.. args, "--connection", classDb.ConnectionString]).InvokeAsync(cancellationToken: Ct);
    }

    /// <summary>The real store, after which the caller gives up: the request that sent the file has gone.</summary>
    private sealed class CancelAfterStore(IArtifactBlobStore inner, CancellationTokenSource caller) : IArtifactBlobStore
    {
        public async Task<BinaryCaptureResult> PutAsync(NewBinaryCapture capture, CancellationToken cancellationToken = default)
        {
            var stored = await inner.PutAsync(capture, cancellationToken);
            await caller.CancelAsync();
            return stored;
        }

        public Task<bool> ExistsAsync(Guid artifactId, CancellationToken cancellationToken = default)
            => inner.ExistsAsync(artifactId, cancellationToken);

        public Task<ArtifactBlob?> GetAsync(Guid artifactId, CancellationToken cancellationToken = default)
            => inner.GetAsync(artifactId, cancellationToken);

        public Task<ArtifactBlobMetadata?> GetMetadataAsync(Guid artifactId, CancellationToken cancellationToken = default)
            => inner.GetMetadataAsync(artifactId, cancellationToken);
    }
}

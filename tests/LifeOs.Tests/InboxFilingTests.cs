using System.Security.Cryptography;
using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR114 (fifth review R7): filing an Inbox capture as an Idea or a Problem is one
/// write, the item with the capture's edge to it and its flag, so a failure leaves no Idea outside
/// the Inbox; a document or voice note sent again finds the item it was filed as rather than making
/// a second; and an item a capture names that isn't there is refused before anything is stored,
/// from every door.
/// </summary>
public sealed class InboxFilingTests(PostgresFixture postgres, ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] RandomFile()
    {
        var bytes = new byte[256];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public async Task A_document_sent_again_as_an_Idea_is_the_one_Idea_flagged_once()
    {
        await using var kernel = classDb.Services().BuildServiceProvider();
        var filing = kernel.GetRequiredService<InboxFilingService>();
        var bytes = RandomFile();
        var description = $"Turn the shed into a studio {Guid.NewGuid():N}";

        var first = await filing.CaptureDocumentAsync(new CaptureFile(bytes, "shed.pdf"), description, SubjectTypes.Idea, cancellationToken: Ct);
        var again = await filing.CaptureDocumentAsync(new CaptureFile(bytes, "shed.pdf"), description, SubjectTypes.Idea, cancellationToken: Ct);

        Assert.True(again.Stored!.Deduplicated);
        Assert.Equal(first.Item!.Id, again.Item!.Id);
        Assert.Equal([first.Item.Id], await ConcerningAsync(classDb.ConnectionString, first.EventId, SubjectTypes.Idea));
        Assert.Equal(1, await FlagsAsync(classDb.ConnectionString, first.Item.Id));
    }

    [Fact]
    public async Task A_voice_note_kept_with_its_audio_sent_again_as_a_Problem_is_the_one_Problem()
    {
        await using var kernel = classDb.Services().BuildServiceProvider();
        var filing = kernel.GetRequiredService<InboxFilingService>();
        var audio = new CaptureFile(RandomFile(), "memo.m4a");
        var transcript = $"The gutter leaks over the porch {Guid.NewGuid():N}";

        var first = await filing.CaptureVoiceAsync(audio, transcript, keepAudio: true, SubjectTypes.Problem, cancellationToken: Ct);
        var again = await filing.CaptureVoiceAsync(audio, transcript, keepAudio: true, SubjectTypes.Problem, cancellationToken: Ct);

        Assert.True(again.Stored!.Deduplicated);
        Assert.Equal(first.Item!.Id, again.Item!.Id);
        Assert.Equal([first.Item.Id], await ConcerningAsync(classDb.ConnectionString, first.EventId, SubjectTypes.Problem));
    }

    [Fact]
    public async Task A_Problem_already_there_is_filed_with_the_captures_edge_and_flagged_again()
    {
        await using var kernel = classDb.Services().BuildServiceProvider();
        var title = $"Squeaky stairs {Guid.NewGuid():N}";
        var problem = (await kernel.GetRequiredService<SubjectService>().ResolveOrCreateAsync(SubjectTypes.Problem, title, Ct)).Subject;

        var captured = await kernel.GetRequiredService<InboxFilingService>().CaptureAsync(title, SubjectTypes.Problem, cancellationToken: Ct);

        Assert.Equal(problem.Id, captured.Item!.Id);
        Assert.Equal([problem.Id], await ConcerningAsync(classDb.ConnectionString, captured.EventId, SubjectTypes.Problem));
        Assert.Equal(1, await FlagsAsync(classDb.ConnectionString, problem.Id));
    }

    [Fact]
    public async Task An_Idea_whose_flag_fails_is_not_made_and_its_retry_makes_it_once()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await ExecuteAsync(db.ConnectionString, """
            CREATE FUNCTION public.refuse_triage() RETURNS trigger LANGUAGE plpgsql
            AS $$ BEGIN RAISE EXCEPTION 'the flag failed'; END $$;
            CREATE TRIGGER refuse_triage BEFORE INSERT ON bsk.event
            FOR EACH ROW WHEN (NEW.kind = 'triage') EXECUTE FUNCTION public.refuse_triage();
            """);
        var filing = db.Get<InboxFilingService>();
        var key = Guid.NewGuid().ToString();
        const string text = "Build a cold frame for the seedlings";

        await Assert.ThrowsAnyAsync<PostgresException>(() => filing.CaptureAsync(text, SubjectTypes.Idea, key, cancellationToken: Ct));

        // The note is written, but no Idea was left outside the Inbox, nor an edge to one.
        Assert.Equal(0, await IdeasTitledAsync(db.ConnectionString, text));
        Assert.Equal(0, await ScalarAsync<int>(db.ConnectionString, "SELECT count(*) FROM bsk.subject_event;"));

        await ExecuteAsync(db.ConnectionString, "DROP TRIGGER refuse_triage ON bsk.event;");
        var again = await filing.CaptureAsync(text, SubjectTypes.Idea, key, cancellationToken: Ct);

        Assert.True(again.AlreadyWritten);
        Assert.Equal(1, await IdeasTitledAsync(db.ConnectionString, text));
        Assert.True(await ScalarAsync<bool>(
            db.ConnectionString, $"SELECT EXISTS (SELECT 1 FROM bsk.v_inbox WHERE item_id = '{again.Item!.Id}');"));
    }

    [Fact]
    public async Task A_capture_about_no_item_is_refused_before_anything_is_stored()
    {
        await using var kernel = classDb.Services().BuildServiceProvider();
        var filing = kernel.GetRequiredService<InboxFilingService>();
        const string missing = "urn:bsk:project:nowhere-000000";
        var document = RandomFile();
        var recording = RandomFile();
        var transcript = $"Ask about the fence {Guid.NewGuid():N}";

        await Assert.ThrowsAsync<SubjectNotFoundException>(() => filing.CaptureDocumentAsync(
            new CaptureFile(document, "fence.pdf"), concerning: missing, cancellationToken: Ct));
        await Assert.ThrowsAsync<SubjectNotFoundException>(() => filing.CaptureVoiceAsync(
            new CaptureFile(recording, "fence.m4a"), transcript, keepAudio: true, concerning: missing, cancellationToken: Ct));
        await Assert.ThrowsAsync<SubjectNotFoundException>(() => filing.CaptureVoiceAsync(
            audio: null, transcript, concerning: missing, cancellationToken: Ct));

        foreach (var bytes in new[] { document, recording })
        {
            Assert.False(await ScalarAsync<bool>(
                classDb.ConnectionString,
                $"SELECT EXISTS (SELECT 1 FROM bsk.artifact_blob WHERE sha256 = '{Convert.ToHexStringLower(SHA256.HashData(bytes))}');"));
        }

        Assert.False(await ScalarAsync<bool>(
            classDb.ConnectionString, $"SELECT EXISTS (SELECT 1 FROM bsk.artifact WHERE content = '{transcript}');"));
    }

    private static async Task<List<Guid>> ConcerningAsync(string connectionString, Guid eventId, string type)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT s.id FROM bsk.subject_event se JOIN bsk.subject s ON s.id = se.subject_id
            WHERE se.event_id = @eventId AND se.relation = 'concerns' AND s.type = @type;
            """,
            new { eventId, type }, cancellationToken: Ct))).AsList();
    }

    private static async Task<int> FlagsAsync(string connectionString, Guid itemId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'triage' AND payload->>'item_id' = @item AND payload->>'state' = 'flagged';",
            new { item = itemId.ToString() }, cancellationToken: Ct));
    }

    private static async Task<int> IdeasTitledAsync(string connectionString, string title)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE type = 'Idea' AND title = @title;", new { title }, cancellationToken: Ct));
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, cancellationToken: Ct)))!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: Ct));
    }
}

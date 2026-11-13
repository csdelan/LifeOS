using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// CAP-2 and CAP-4 over HTTP: <c>POST /api/capture/document</c> and <c>/voice</c> take a
/// multipart upload and put it in the Inbox (D4), the Inbox and a subject's Files section show
/// the file, and <c>GET /api/artifacts/{id}</c> answers its metadata. Each test has a database
/// of its own, so its Inbox holds only what the test captured.
/// </summary>
public sealed class CaptureEndpointTests(PostgresFixture postgres)
{
    private const string Pacific = "America/Los_Angeles";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] Pdf = "%PDF-1.7 Comcast statement, October"u8.ToArray();
    private static readonly byte[] Audio = [0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x02, 0x03, 0x04];

    private static Task<HttpResponseMessage> DocumentAsync(
        ApiTestHost host, byte[]? bytes, string? description = null, string? type = null,
        string field = "file", string filename = "statement.pdf", string contentType = "application/pdf")
    {
        var form = new MultipartFormDataContent();
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, field, filename);
        }

        if (description is not null)
        {
            form.Add(new StringContent(description), "description");
        }

        if (type is not null)
        {
            form.Add(new StringContent(type), "type");
        }

        return host.Client.PostAsync("/api/capture/document", form, Ct);
    }

    private static Task<HttpResponseMessage> VoiceAsync(
        ApiTestHost host, byte[]? audio, string? transcript, bool keepAudio, string? type = null)
    {
        var form = new MultipartFormDataContent();
        if (audio is not null)
        {
            var file = new ByteArrayContent(audio);
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
            form.Add(file, "audio", "voice.webm");
        }

        if (transcript is not null)
        {
            form.Add(new StringContent(transcript), "transcript");
        }

        form.Add(new StringContent(keepAudio ? "true" : "false"), "keepAudio");
        if (type is not null)
        {
            form.Add(new StringContent(type), "type");
        }

        return host.Client.PostAsync("/api/capture/voice", form, Ct);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<string> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()!;
    }

    private static async Task<JsonElement[]> InboxAsync(ApiTestHost host)
        => [.. (await host.Client.GetFromJsonAsync<JsonElement>("/api/inbox", Ct)).EnumerateArray()];

    [Fact]
    public async Task A_document_note_lands_in_the_inbox_with_its_file_and_once_however_often_it_is_sent()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var result = await OkAsync(await DocumentAsync(host, Pdf, "Disputed Comcast charge reference statement"));
        Assert.Equal("note", result.GetProperty("kind").GetString());
        Assert.False(result.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("subject").ValueKind);
        var artifact = result.GetProperty("artifactId").GetGuid();
        var eventId = result.GetProperty("eventId").GetGuid();

        var item = Assert.Single(await InboxAsync(host));
        Assert.Equal(eventId, item.GetProperty("itemId").GetGuid());
        Assert.Equal("Disputed Comcast charge reference statement", item.GetProperty("eventContent").GetString());
        var attachment = item.GetProperty("attachment");
        Assert.Equal(artifact, attachment.GetProperty("id").GetGuid());
        Assert.Equal("statement.pdf", attachment.GetProperty("filename").GetString());
        Assert.Equal("application/pdf", attachment.GetProperty("contentType").GetString());
        Assert.Equal(Pdf.Length, attachment.GetProperty("byteSize").GetInt64());
        Assert.Equal("note", attachment.GetProperty("eventKind").GetString());
        Assert.False(item.GetProperty("needsTranscription").GetBoolean());

        // Its metadata by either id, and its bytes, which download rather than show.
        foreach (var id in new[] { artifact, eventId })
        {
            var metadata = await host.Client.GetFromJsonAsync<JsonElement>($"/api/artifacts/{id}", Ct);
            Assert.Equal(artifact, metadata.GetProperty("id").GetGuid());
            Assert.True(metadata.GetProperty("hasBytes").GetBoolean());
        }

        var content = await host.Client.GetAsync($"/api/artifacts/{artifact}/content", Ct);
        Assert.Equal(Pdf, await content.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("statement.pdf", content.Content.Headers.ContentDisposition!.FileName?.Trim('"'));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/artifacts/{Guid.NewGuid()}", Ct)).StatusCode);

        // The same file again is the same capture, and the Inbox still holds one.
        var again = await OkAsync(await DocumentAsync(host, Pdf, "Same statement"));
        Assert.True(again.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(eventId, again.GetProperty("eventId").GetGuid());
        Assert.Single(await InboxAsync(host));
    }

    [Fact]
    public async Task A_document_problem_is_made_at_once_on_Chris_s_day_with_the_file_in_its_files()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct, Pacific);
        await using var host = await ApiTestHost.StartAsync(
            db.ConnectionString, Ct, new Dictionary<string, string?> { [HomeZone.ConfigurationKey] = Pacific });

        var result = await OkAsync(await DocumentAsync(
            host, Pdf, "Comcast billed twice in October\nStatement attached", type: "problem"));
        var subject = result.GetProperty("subject");
        Assert.Equal(SubjectTypes.Problem, subject.GetProperty("type").GetString());
        Assert.Equal("Comcast billed twice in October", subject.GetProperty("title").GetString());
        var id = subject.GetProperty("id").GetGuid();

        // The Problem is what's in the Inbox, carrying the file it was made from.
        var item = Assert.Single(await InboxAsync(host));
        Assert.Equal("subject", item.GetProperty("itemKind").GetString());
        Assert.Equal(id, item.GetProperty("itemId").GetGuid());
        Assert.Equal("statement.pdf", item.GetProperty("attachment").GetProperty("filename").GetString());

        var files = await host.Client.GetFromJsonAsync<JsonElement>($"/api/subjects/{id}/files", Ct);
        Assert.Equal(result.GetProperty("artifactId").GetGuid(), Assert.Single(files.EnumerateArray()).GetProperty("id").GetGuid());

        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var (identified, today, description) = await connection.QuerySingleAsync<(string, string, string)>(
            """
            SELECT s.attributes->>'date_identified', (now() AT TIME ZONE @zone)::date::text, s.attributes->>'description'
            FROM bsk.subject s WHERE s.id = @id
            """,
            new { id, zone = Pacific });
        Assert.Equal(today, identified);
        Assert.Equal("Comcast billed twice in October\nStatement attached", description);
    }

    [Fact]
    public async Task A_voice_note_keeps_only_its_transcript_unless_asked_and_one_with_none_keeps_its_audio()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        // The default: the transcript only, so no audio was even sent.
        var spoken = await OkAsync(await VoiceAsync(host, null, "Call the dentist about Thursday", keepAudio: false));
        Assert.Equal("voice", spoken.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, spoken.GetProperty("artifactId").ValueKind);

        // Keep audio: the recording is stored with the transcript.
        var kept = await OkAsync(await VoiceAsync(host, Audio, "Groceries: eggs, spinach", keepAudio: true));
        Assert.Equal(Audio.Length, kept.GetProperty("byteSize").GetInt64());

        // No transcript: the audio is kept anyway, and marked as needing one.
        byte[] other = [.. Audio, 0x05];
        var failed = await OkAsync(await VoiceAsync(host, other, "  ", keepAudio: false));
        Assert.True(failed.GetProperty("needsTranscription").GetBoolean());

        var inbox = await InboxAsync(host);
        Assert.Equal(3, inbox.Length);
        var byEvent = inbox.ToDictionary(i => i.GetProperty("itemId").GetGuid());
        var plain = byEvent[spoken.GetProperty("eventId").GetGuid()];
        Assert.Equal("voice", plain.GetProperty("eventKind").GetString());
        Assert.Equal("Call the dentist about Thursday", plain.GetProperty("eventContent").GetString());
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("attachment").ValueKind);
        Assert.Equal("audio/webm", byEvent[kept.GetProperty("eventId").GetGuid()]
            .GetProperty("attachment").GetProperty("contentType").GetString());
        var untranscribed = byEvent[failed.GetProperty("eventId").GetGuid()];
        Assert.True(untranscribed.GetProperty("needsTranscription").GetBoolean());
        Assert.Equal("voice", untranscribed.GetProperty("attachment").GetProperty("eventKind").GetString());
    }

    [Fact]
    public async Task A_voice_idea_is_made_at_once_with_its_recording_in_its_files()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var result = await OkAsync(await VoiceAsync(host, Audio, "A standing desk for the den", keepAudio: true, type: "Idea"));
        var subject = result.GetProperty("subject");
        Assert.Equal(SubjectTypes.Idea, subject.GetProperty("type").GetString());
        Assert.Equal("A standing desk for the den", subject.GetProperty("title").GetString());

        var files = await host.Client.GetFromJsonAsync<JsonElement>(
            $"/api/subjects/{subject.GetProperty("id").GetGuid()}/files", Ct);
        Assert.Equal("voice", Assert.Single(files.EnumerateArray()).GetProperty("eventKind").GetString());
    }

    // Code review CR44: a typed capture is a note event however it's filed, so an Idea typed into
    // quick capture keeps the words it was made from, as a document or voice Idea does.
    [Fact]
    public async Task A_typed_idea_keeps_the_note_it_was_made_from_and_a_typed_note_is_flagged()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        await OkStatusAsync(await host.Client.PostAsJsonAsync("/api/capture", new { text = "  Call the plumber  " }, Ct));
        await OkStatusAsync(await host.Client.PostAsJsonAsync(
            "/api/capture", new { text = Shed, type = "idea" }, Ct));
        Assert.Contains("not 'Goal'", await RefusedAsync(
            await host.Client.PostAsJsonAsync("/api/capture", new { text = "Run a marathon", type = "Goal" }, Ct)));

        var inbox = await InboxAsync(host);
        Assert.Equal(2, inbox.Length);
        var note = Assert.Single(inbox, i => i.GetProperty("itemKind").GetString() == "event");
        Assert.Equal("Call the plumber", note.GetProperty("eventContent").GetString());
        var idea = Assert.Single(inbox, i => i.GetProperty("itemKind").GetString() == "subject");
        Assert.Equal(SubjectTypes.Idea, idea.GetProperty("subjectType").GetString());
        Assert.Equal("A shed for the bikes", idea.GetProperty("subjectTitle").GetString());

        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var (description, kept) = await connection.QuerySingleAsync<(string, string)>(
            """
            SELECT s.attributes->>'description', a.content
            FROM bsk.subject s
            JOIN bsk.subject_event se ON se.subject_id = s.id AND se.relation = 'concerns'
            JOIN bsk.event e ON e.id = se.event_id AND e.kind = 'note'
            JOIN bsk.artifact a ON a.id = e.artifact_id
            WHERE s.id = @id
            """,
            new { id = idea.GetProperty("itemId").GetGuid() });
        Assert.Equal(Shed, description);
        Assert.Equal(Shed, kept);
    }

    private const string Shed = "A shed for the bikes\nBehind the garage";

    private static async Task OkStatusAsync(HttpResponseMessage response)
        => Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));

    [Fact]
    public async Task Captures_that_cannot_be_written_are_refused_in_words_with_nothing_written()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        Assert.Contains("field named file", await RefusedAsync(await DocumentAsync(host, Pdf, field: "attachment")));
        Assert.Contains("field named file", await RefusedAsync(await DocumentAsync(host, null, "just words")));
        Assert.Contains("not 'Goal'", await RefusedAsync(await DocumentAsync(host, Pdf, type: "Goal")));
        Assert.Contains("at least one byte", await RefusedAsync(await DocumentAsync(host, [])));
        Assert.Contains("needs its recording", await RefusedAsync(await VoiceAsync(host, null, null, keepAudio: false)));
        Assert.Contains("Keep audio needs", await RefusedAsync(await VoiceAsync(host, null, "words", keepAudio: true)));

        Assert.Empty(await InboxAsync(host));
    }
}

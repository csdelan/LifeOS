using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Journals over HTTP (D19, csdelan/LifeOS#28): the browser talks to LifeOS, LifeOS to
/// SuperJournal (played by <see cref="InMemorySuperJournal"/>), as the signed-in person, with a
/// pointer in <c>bsk</c> for every saved version, and files streamed through both ways. Each test
/// has a database of its own.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JournalEndpointTests(PostgresFixture postgres)
{
    // Settings win over environment variables, so a SuperJournal on this machine can't switch it on.
    private static readonly Dictionary<string, string?> Settings = new(DomainTestDatabase.ApiZone)
    {
        [SuperJournalOptions.BaseUrlKey] = "",
    };

    private static readonly object Doc = new
    {
        type = "doc",
        content = new object[] { new { type = "paragraph", content = new object[] { new { type = "text", text = "Up early." } } } },
    };

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<ApiTestHost> StartAsync(DomainTestDatabase db, InMemorySuperJournal? fake, CancellationToken ct)
        => ApiTestHost.StartAsync(db.ConnectionString, ct, Settings, fake is null ? null : services => services.AddSingleton<ISuperJournal>(fake));

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task Without_superjournal_the_journal_endpoints_say_it_isnt_set_up()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db, fake: null, Ct);

        var open = await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/journals", new { subjectType = "daily", subjectKey = "2026-10-28" }, Ct),
            HttpStatusCode.ServiceUnavailable);
        var entries = await host.Client.GetAsync("/api/journals/j1/entries", Ct);

        Assert.Equal(SuperJournalException.NotSetUpMessage, open.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, entries.StatusCode);
    }

    [Fact]
    public async Task A_journal_is_opened_written_edited_and_read_back_through_lifeos_with_a_pointer_per_version()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var fake = new InMemorySuperJournal();
        await using var host = await StartAsync(db, fake, Ct);
        var client = host.Client;

        var journal = await JsonAsync(await client.PostAsJsonAsync(
            "/api/journals", new { subjectType = "daily", subjectKey = "2026-10-28", title = "Wednesday" }, Ct));
        var id = journal.GetProperty("id").GetString()!;
        var again = await JsonAsync(await client.PostAsJsonAsync(
            "/api/journals", new { subjectType = "daily", subjectKey = "2026-10-28" }, Ct));
        var found = await JsonAsync(await client.GetAsync("/api/journals?subjectType=daily&subjectKey=2026-10-28", Ct));
        await JsonAsync(await client.GetAsync("/api/journals?subjectType=daily&subjectKey=2026-10-29", Ct), HttpStatusCode.NotFound);
        Assert.Equal(id, again.GetProperty("id").GetString());
        Assert.Equal(id, found.GetProperty("id").GetString());
        Assert.Equal("Wednesday", journal.GetProperty("title").GetString());

        var saved = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/journals/{id}/entries", new { content = Doc, contentText = "Up early.", tags = new[] { "morning" } }, Ct));
        var entry = saved.GetProperty("entry");
        var entryId = entry.GetProperty("id").GetString()!;
        Assert.Equal(1, entry.GetProperty("version").GetInt32());
        Assert.Equal("doc", entry.GetProperty("content").GetProperty("type").GetString());
        Assert.Equal(["morning"], entry.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.False(saved.GetProperty("pointerPending").GetBoolean());

        var edited = await JsonAsync(await client.PatchAsJsonAsync(
            $"/api/journals/entries/{entryId}", new { content = Doc, contentText = "Up early. Walked the ridge.", expectedVersion = 1 }, Ct));
        var stale = await JsonAsync(await client.PatchAsJsonAsync(
            $"/api/journals/entries/{entryId}", new { content = Doc, contentText = "From an old copy.", expectedVersion = 1 }, Ct),
            HttpStatusCode.Conflict);
        Assert.Equal(2, edited.GetProperty("entry").GetProperty("version").GetInt32());
        Assert.Contains("changed since it was read", stale.GetProperty("error").GetString(), StringComparison.Ordinal);

        var history = await JsonAsync(await client.GetAsync($"/api/journals/entries/{entryId}/history", Ct));
        var replaced = Assert.Single(history.EnumerateArray());
        Assert.Equal(1, replaced.GetProperty("version").GetInt32());
        Assert.Equal("Up early.", replaced.GetProperty("contentText").GetString());
        var list = await JsonAsync(await client.GetAsync($"/api/journals/{id}/entries?pageSize=5", Ct));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt64());
        Assert.Equal(2, list.GetProperty("items")[0].GetProperty("version").GetInt32());
        var read = await JsonAsync(await client.GetAsync($"/api/journals/entries/{entryId}", Ct));
        Assert.Equal("Up early. Walked the ridge.", read.GetProperty("contentText").GetString());
        await JsonAsync(await client.GetAsync("/api/journals/entries/no-such-entry", Ct), HttpStatusCode.NotFound);
        await JsonAsync(await client.PostAsJsonAsync($"/api/journals/{id}/entries", new { content = Doc }, Ct), HttpStatusCode.BadRequest);

        // Everything went to SuperJournal as Chris, from the web.
        Assert.All(fake.Callers, c => Assert.Equal(new JournalCaller("user-1", KernelSources.Api), c));
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var pointers = (await connection.QueryAsync<(string Source, string ExternalId, string Excerpt)>(new CommandDefinition(
            """
            SELECT source_id, external_id, payload->'superjournal'->>'excerpt'
            FROM bsk.event WHERE kind = 'journal' ORDER BY external_id
            """, cancellationToken: Ct))).ToList();
        Assert.Equal(
            [("api", $"{entryId}:1", "Up early."), ("api", $"{entryId}:2", "Up early. Walked the ridge.")],
            pointers);
        Assert.Equal(saved.GetProperty("pointerEventId").GetGuid(), await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM bsk.event WHERE external_id = @key", new { key = $"{entryId}:1" }, cancellationToken: Ct)));
    }

    [Fact]
    public async Task A_save_sent_twice_with_one_idempotency_key_is_one_entry()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var fake = new InMemorySuperJournal();
        await using var host = await StartAsync(db, fake, Ct);
        var journal = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/journals", new { subjectType = "daily", subjectKey = "2026-10-28" }, Ct));
        var path = $"/api/journals/{journal.GetProperty("id").GetString()}/entries";

        async Task<JsonElement> SaveAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new { content = Doc, contentText = "Up early." }),
            };
            request.Headers.Add("Idempotency-Key", "8d6f0c1e-save");
            return await JsonAsync(await host.Client.SendAsync(request, Ct));
        }

        var first = await SaveAsync();
        var again = await SaveAsync();

        Assert.Equal(
            first.GetProperty("entry").GetProperty("id").GetString(),
            again.GetProperty("entry").GetProperty("id").GetString());
        Assert.Equal(first.GetProperty("pointerEventId").GetGuid(), again.GetProperty("pointerEventId").GetGuid());
        Assert.Equal(["8d6f0c1e-save", "8d6f0c1e-save"], fake.IdempotencyKeys.ToArray());
    }

    [Fact]
    public async Task An_upload_streams_through_to_superjournal_before_the_browser_has_sent_it_all()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var fake = new InMemorySuperJournal();
        await using var host = await StartAsync(db, fake, Ct);
        var entryId = await DraftAsync(host);
        var first = Bytes(64 * 1024, seed: 1);
        var rest = Bytes(64 * 1024, seed: 2);

        // The body holds its second half back until SuperJournal has the first: if LifeOS read the
        // whole upload before sending it on, this would never finish.
        using var body = new HeldBackUpload("ridge.png", "image/png", first, rest, fake.FirstUploadBytes.Task);
        var response = await host.Client.PostAsync($"/api/journals/entries/{entryId}/attachments", body, Ct);

        var attachment = await JsonAsync(response);
        Assert.Equal(first.Length + rest.Length, attachment.GetProperty("sizeBytes").GetInt64());
        Assert.Equal("ridge.png", attachment.GetProperty("fileName").GetString());
        Assert.True(attachment.GetProperty("isImage").GetBoolean());
        Assert.Equal([(long)(first.Length + rest.Length)], fake.UploadSizes);

        var content = await host.Client.GetAsync($"/api/journals/attachments/{attachment.GetProperty("id").GetString()}/content", Ct);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", content.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        var downloaded = await content.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal([.. first, .. rest], downloaded);
    }

    [Fact]
    public async Task A_file_that_isnt_a_picture_downloads_sandboxed_and_superjournal_refusals_come_back_in_words()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var fake = new InMemorySuperJournal();
        await using var host = await StartAsync(db, fake, Ct);
        var entryId = await DraftAsync(host);
        var csv = Encoding.UTF8.GetBytes("date,steps\n2026-10-28,9000\n");

        var uploaded = await JsonAsync(await host.Client.PostAsync(
            $"/api/journals/entries/{entryId}/attachments", Form("steps.csv", "text/csv", csv), Ct));
        var attachmentId = uploaded.GetProperty("id").GetString()!;
        var zip = await JsonAsync(await host.Client.PostAsync(
            $"/api/journals/entries/{entryId}/attachments", Form("notes.zip", "application/zip", [1, 2, 3]), Ct), HttpStatusCode.BadRequest);
        var noFile = await host.Client.PostAsync(
            $"/api/journals/entries/{entryId}/attachments", new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

        var content = await host.Client.GetAsync($"/api/journals/attachments/{attachmentId}/content", Ct);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(csv, await content.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("sandbox", content.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("attachment", content.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("steps.csv", content.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/journals/attachments/{attachmentId}/thumbnail", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/journals/attachments/no-such-file/content", Ct)).StatusCode);
        Assert.Contains("not 'application/zip'", zip.GetProperty("error").GetString(), StringComparison.Ordinal);
        // The upload takes multipart/form-data only (its Accepts metadata), so anything else is 415.
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, noFile.StatusCode);

        fake.Failure = new SuperJournalException(SuperJournalFailure.Unavailable, "SuperJournal couldn't be reached to save the entry; try again later.");
        var down = await JsonAsync(await host.Client.PostAsJsonAsync(
            $"/api/journals/x/entries", new { content = Doc, contentText = "Lost?" }, Ct), HttpStatusCode.BadGateway);
        Assert.Contains("couldn't be reached", down.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    private async Task<string> DraftAsync(ApiTestHost host)
    {
        var journal = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/journals", new { subjectType = "daily", subjectKey = "2026-10-28" }, Ct));
        var draft = await JsonAsync(await host.Client.PostAsync(
            $"/api/journals/{journal.GetProperty("id").GetString()}/drafts", null, Ct));
        Assert.Equal(1, draft.GetProperty("version").GetInt32());
        return draft.GetProperty("id").GetString()!;
    }

    private static MultipartFormDataContent Form(string fileName, string type, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static byte[] Bytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// A multipart/form-data body with one file part, sent chunked: the first half, then nothing
    /// more until <paramref name="release"/> completes (or 10 seconds pass, which fails the test).
    /// </summary>
    private sealed class HeldBackUpload : HttpContent
    {
        private const string Boundary = "held-back-boundary";
        private readonly string fileName;
        private readonly string type;
        private readonly byte[] first;
        private readonly byte[] rest;
        private readonly Task release;

        public HeldBackUpload(string fileName, string type, byte[] first, byte[] rest, Task release)
        {
            this.fileName = fileName;
            this.type = type;
            this.first = first;
            this.rest = rest;
            this.release = release;
            Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var head = $"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {type}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(first);
            await stream.FlushAsync();
            if (await Task.WhenAny(release, Task.Delay(TimeSpan.FromSeconds(10))) != release)
            {
                throw new TimeoutException("LifeOS didn't send the first part on to SuperJournal before the upload was complete.");
            }

            await stream.WriteAsync(rest);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"\r\n--{Boundary}--\r\n"));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

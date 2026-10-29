using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure.Integrations;

namespace LifeOs.Tests;

/// <summary>
/// The SuperJournal adapter over HTTP (D19, csdelan/LifeOS#28) against a stub that answers as
/// SuperJournal's API v1 does: the paths and bodies it sends, the token on every call, the
/// failures in words, and files streamed both ways. Nothing here reaches a network.
/// </summary>
public sealed class SuperJournalHttpClientTests
{
    private const string Base = "https://journal.example.test/api/journal/";
    private static readonly JournalCaller Chris = new("user-1", "api");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string JournalJson = """
        {"id":"j1","ownerAccountId":"user-1","app":"lifeos","subjectType":"goal","subjectKey":"urn:lifeos:goal/1",
         "title":"Walk the ridge","allowedRoles":[],"createdAt":"2026-10-28T10:00:00+00:00","isArchived":false}
        """;

    private const string EntryJson = """
        {"id":"e1","journalId":"j1","content":{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Up early."}]}]},
         "contentText":"Up early.","tags":["morning"],"attachmentIds":[],"allowedRoles":[],"version":2,
         "createdAt":"2026-10-28T10:00:00+00:00","updatedAt":"2026-10-28T10:05:00+00:00"}
        """;

    [Fact]
    public async Task Opening_a_journal_posts_it_owner_only_with_the_persons_token()
    {
        var stub = new StubHandler(_ => Json(HttpStatusCode.Created, JournalJson));
        var client = Client(stub);

        var journal = await client.OpenJournalAsync(Chris, "goal", "urn:lifeos:goal/1", "Walk the ridge", Ct);

        Assert.Equal(new SuperJournalJournal("j1", "lifeos", "goal", "urn:lifeos:goal/1", "Walk the ridge",
            At(10, 0), false), journal);
        var sent = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(Base + "api/v1/journals", sent.Uri);
        Assert.Equal("Bearer token-for:user-1", sent.Authorization);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal("goal", body.RootElement.GetProperty("subjectType").GetString());
        Assert.Equal("urn:lifeos:goal/1", body.RootElement.GetProperty("subjectKey").GetString());
        Assert.Equal("Walk the ridge", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("allowedRoles").GetArrayLength());
    }

    [Fact]
    public async Task An_entry_is_sent_as_its_document_and_its_plain_text_and_an_edit_keeps_history_from_its_version()
    {
        var stub = new StubHandler(_ => Json(HttpStatusCode.Created, EntryJson));
        var client = Client(stub);
        var text = InMemorySuperJournal.Text("Up early.");

        var added = await client.AppendEntryAsync(Chris, "j1", text, ["morning"], cancellationToken: Ct);
        var edited = await client.EditEntryAsync(Chris, "e1", text, expectedVersion: 1, Ct);

        Assert.Equal(2, added.Version);
        Assert.Equal("Up early.", edited.ContentText);
        Assert.Equal(["morning"], edited.Tags);
        Assert.Equal("doc", edited.Content.GetProperty("type").GetString());
        Assert.Equal(At(10, 5), edited.UpdatedAt);

        var append = stub.Requests[0];
        Assert.Equal(HttpMethod.Post, append.Method);
        Assert.Equal(Base + "api/v1/journals/j1/entries", append.Uri);
        using (var body = JsonDocument.Parse(append.Body))
        {
            Assert.Equal("doc", body.RootElement.GetProperty("content").GetProperty("type").GetString());
            Assert.Equal("Up early.", body.RootElement.GetProperty("contentText").GetString());
            Assert.Equal("morning", body.RootElement.GetProperty("tags")[0].GetString());
            Assert.False(body.RootElement.TryGetProperty("allowedRoles", out _));
        }

        var edit = stub.Requests[1];
        Assert.Equal(HttpMethod.Patch, edit.Method);
        Assert.Equal(Base + "api/v1/entries/e1?trackHistory=true", edit.Uri);
        using (var body = JsonDocument.Parse(edit.Body))
        {
            Assert.Equal(1, body.RootElement.GetProperty("expectedVersion").GetInt32());
            Assert.Equal("Up early.", body.RootElement.GetProperty("contentText").GetString());
            Assert.Equal("doc", body.RootElement.GetProperty("content").GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task Lists_are_lifeos_journals_and_newest_entries_first_and_ids_are_escaped()
    {
        var stub = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query;
            return path.EndsWith("/history", StringComparison.Ordinal) ? Json(HttpStatusCode.OK, """
                    [{"id":"r1","entryId":"e1","supersededVersion":1,"priorContent":{"type":"doc","content":[]},
                      "priorContentText":"Up.","editedAt":"2026-10-28T10:05:00+00:00"}]
                    """)
                : query.Contains("subjectType", StringComparison.Ordinal) ? Json(HttpStatusCode.OK, JournalJson)
                : path.EndsWith("/api/v1/journals", StringComparison.Ordinal) ? Json(HttpStatusCode.OK, Paged(JournalJson))
                : Json(HttpStatusCode.OK, Paged(EntryJson));
        });
        var client = Client(stub);

        var journals = await client.ListJournalsAsync(Chris, 2, 10, Ct);
        var entries = await client.GetEntriesAsync(Chris, "j/1", 2, 10, Ct);
        var found = await client.FindJournalAsync(Chris, "goal", "urn:lifeos:goal/1 & more", Ct);
        var history = await client.GetHistoryAsync(Chris, "e1", Ct);
        var search = await client.SearchAsync(Chris, "ridge walk", null, 1, 20, Ct);

        Assert.Equal(11, journals.TotalCount);
        Assert.Equal("j1", Assert.Single(journals.Items).Id);
        Assert.Equal("e1", Assert.Single(entries.Items).Id);
        Assert.Equal("j1", found!.Id);
        Assert.Equal(1, Assert.Single(history).SupersededVersion);
        Assert.Equal("Up.", history[0].PriorContentText);
        Assert.Single(search.Items);
        Assert.Equal(Base + "api/v1/journals?app=lifeos&page=2&pageSize=10", stub.Requests[0].Uri);
        Assert.Equal(Base + "api/v1/journals/j%2F1/entries?page=2&pageSize=10&sort=desc", stub.Requests[1].Uri);
        Assert.Equal(Base + "api/v1/journals?subjectType=goal&subjectKey=urn%3Alifeos%3Agoal%2F1%20%26%20more", stub.Requests[2].Uri);
        Assert.Equal(Base + "api/v1/entries/e1/history", stub.Requests[3].Uri);
        Assert.Equal(Base + "api/v1/search?q=ridge%20walk&page=1&pageSize=20", stub.Requests[4].Uri);
        Assert.All(stub.Requests, r => Assert.Equal("Bearer token-for:user-1", r.Authorization));
    }

    [Fact]
    public async Task A_creates_idempotency_key_goes_as_superjournals_header_and_a_reused_one_is_a_conflict()
    {
        var keys = new List<string?>();
        var stub = new StubHandler(request =>
        {
            keys.Add(request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null);
            return request.RequestUri!.AbsolutePath.EndsWith("/attachments", StringComparison.Ordinal)
                ? Json(HttpStatusCode.Created, """
                    {"id":"a1","entryId":"e1","journalId":"j1","fileName":"dot.png","contentType":"image/png","sizeBytes":1,
                     "isImage":true,"uploadedAt":"2026-10-28T10:06:00+00:00"}
                    """)
                : Json(HttpStatusCode.Created, EntryJson);
        });
        var client = Client(stub);
        var text = InMemorySuperJournal.Text("Up early.");

        await client.AppendEntryAsync(Chris, "j1", text, null, "save-1", Ct);
        await client.CreateDraftAsync(Chris, "j1", "draft-1", Ct);
        await client.UploadAttachmentAsync(Chris, "e1", new MemoryStream([1]), "dot.png", "image/png", "upload-1", Ct);
        await client.AppendEntryAsync(Chris, "j1", text, null, cancellationToken: Ct);
        await client.EditEntryAsync(Chris, "e1", text, 1, Ct);

        Assert.Equal(["save-1", "draft-1", "upload-1", null, null], keys);

        var reused = Client(new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity,
            """{"title":"Idempotency key reused","status":422,"detail":"The key was already used with a different request."}""")));
        var error = await Assert.ThrowsAsync<SuperJournalException>(
            () => reused.AppendEntryAsync(Chris, "j1", text, null, "save-1", Ct));
        Assert.Equal(SuperJournalFailure.Conflict, error.Failure);
        Assert.Contains("already used for a different request", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_isnt_there_reads_as_nothing_where_a_read_can_find_nothing()
    {
        var stub = new StubHandler(_ => Json(HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"Entry 'e9' not found."}"""));
        var client = Client(stub);

        Assert.Null(await client.GetEntryAsync(Chris, "e9", Ct));
        Assert.Null(await client.GetJournalAsync(Chris, "j9", Ct));
        Assert.Null(await client.FindJournalAsync(Chris, "goal", "nothing", Ct));
        Assert.Null(await client.DownloadAttachmentAsync(Chris, "a9", thumbnail: false, Ct));
        var append = await Assert.ThrowsAsync<SuperJournalException>(
            () => client.AppendEntryAsync(Chris, "j9", InMemorySuperJournal.Text("x"), null, cancellationToken: Ct));
        Assert.Equal(SuperJournalFailure.NotFound, append.Failure);
        Assert.Equal("Entry 'e9' not found.", append.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, SuperJournalFailure.Invalid, "ContentText is required.")]
    [InlineData(HttpStatusCode.Unauthorized, SuperJournalFailure.Refused, "SuperJournal__SigningKey")]
    [InlineData(HttpStatusCode.Forbidden, SuperJournalFailure.Forbidden, "isn't the signed-in person's")]
    [InlineData(HttpStatusCode.Conflict, SuperJournalFailure.Conflict, "Read it again")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, SuperJournalFailure.TooLarge, "over SuperJournal's limit")]
    [InlineData(HttpStatusCode.InternalServerError, SuperJournalFailure.Unavailable, "answered 500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, SuperJournalFailure.Unavailable, "try again later")]
    public async Task A_refusal_says_what_went_wrong_in_words_without_the_token(
        HttpStatusCode status, SuperJournalFailure failure, string words)
    {
        var problem = """
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.",
             "status":400,"errors":{"ContentText":["ContentText is required."]}}
            """;
        var stub = new StubHandler(_ => Json(status, problem));
        var client = Client(stub);

        var error = await Assert.ThrowsAsync<SuperJournalException>(
            () => client.EditEntryAsync(Chris, "e1", InMemorySuperJournal.Text("x"), 1, Ct));

        Assert.Equal(failure, error.Failure);
        Assert.Contains(words, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token-for", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_answer_in_time_or_no_connection_is_superjournal_being_unavailable()
    {
        var slow = new SuperJournalHttpClient(
            new HttpClient(new StubHandler(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return Json(HttpStatusCode.OK, "{}");
            })),
            new SuperJournalOptions(new Uri(Base), TimeSpan.FromMilliseconds(100)),
            new StubTokens());
        var down = Client(new StubHandler(_ => throw new HttpRequestException("No such host is known.")));
        var garbled = Client(new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":null}""")));

        var timeout = await Assert.ThrowsAsync<SuperJournalException>(() => slow.GetEntryAsync(Chris, "e1", Ct));
        var unreachable = await Assert.ThrowsAsync<SuperJournalException>(() => down.GetEntryAsync(Chris, "e1", Ct));
        var unreadable = await Assert.ThrowsAsync<SuperJournalException>(() => garbled.GetEntryAsync(Chris, "e1", Ct));

        Assert.Equal(SuperJournalFailure.Unavailable, timeout.Failure);
        Assert.Contains("didn't answer in time", timeout.Message, StringComparison.Ordinal);
        Assert.Equal(SuperJournalFailure.Unavailable, unreachable.Failure);
        Assert.Contains("couldn't be reached", unreachable.Message, StringComparison.Ordinal);
        Assert.Equal(SuperJournalFailure.Unavailable, unreadable.Failure);
        Assert.Contains("couldn't be read", unreadable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_upload_is_sent_chunked_as_a_file_part_as_its_read()
    {
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
        long? length = 0;
        byte[]? received = null;
        string? partName = null;
        string? partFile = null;
        string? partType = null;
        var stub = new StubHandler(async (request, ct) =>
        {
            length = request.Content!.Headers.ContentLength;
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var part = Assert.Single(form);
            partName = part.Headers.ContentDisposition?.Name?.Trim('"');
            partFile = part.Headers.ContentDisposition?.FileName?.Trim('"');
            partType = part.Headers.ContentType?.MediaType;
            received = await part.ReadAsByteArrayAsync(ct);
            return Json(HttpStatusCode.Created, """
                {"id":"a1","entryId":"e1","journalId":"j1","fileName":"ridge.jpg","contentType":"image/jpeg","sizeBytes":300000,
                 "isImage":true,"uploadedByAccountId":"user-1","uploadedAt":"2026-10-28T10:06:00+00:00",
                 "contentUrl":"/api/v1/attachments/a1/content","thumbnailUrl":"/api/v1/attachments/a1/thumbnail"}
                """);
        }, readBody: false);
        var client = Client(stub);

        await using var source = new OneWayStream(bytes);
        var attachment = await client.UploadAttachmentAsync(Chris, "e1", source, "ridge.jpg", "image/jpeg", cancellationToken: Ct);

        Assert.Equal(new SuperJournalAttachment("a1", "e1", "j1", "ridge.jpg", "image/jpeg", 300_000, true, true,
            At(10, 6)), attachment);
        Assert.Null(length);
        Assert.Equal(bytes, received);
        Assert.Equal("file", partName);
        Assert.Equal("ridge.jpg", partFile);
        Assert.Equal("image/jpeg", partType);
        Assert.Equal(Base + "api/v1/entries/e1/attachments", stub.Requests.Single().Uri);
        Assert.Equal("Bearer token-for:user-1", stub.Requests.Single().Authorization);
    }

    [Fact]
    public async Task A_download_streams_back_with_its_type_and_name_and_closes_the_answer_when_disposed()
    {
        var bytes = Encoding.UTF8.GetBytes("date,steps\n2026-10-28,9000\n");
        var answers = new List<TrackedContent>();
        var stub = new StubHandler(_ =>
        {
            var answer = new TrackedContent(bytes);
            answer.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            answer.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "\"steps.csv\"" };
            answers.Add(answer);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = answer };
        });
        var client = Client(stub);

        var download = await client.DownloadAttachmentAsync(Chris, "a1", thumbnail: false, Ct);
        await using (download)
        {
            Assert.NotNull(download);
            Assert.Equal("text/csv", download.ContentType);
            Assert.Equal("steps.csv", download.FileName);
            using var copy = new MemoryStream();
            await download.Content.CopyToAsync(copy, Ct);
            Assert.Equal(bytes, copy.ToArray());
            Assert.False(answers[0].Disposed);
        }

        Assert.True(answers[0].Disposed);
        Assert.Equal(Base + "api/v1/attachments/a1/content", stub.Requests[0].Uri);

        await using var thumbnail = await client.DownloadAttachmentAsync(Chris, "a1", thumbnail: true, Ct);
        Assert.Equal(Base + "api/v1/attachments/a1/thumbnail", stub.Requests[1].Uri);
    }

    private static DateTimeOffset At(int hour, int minute) => new(2026, 10, 28, hour, minute, 0, TimeSpan.Zero);

    private static SuperJournalHttpClient Client(StubHandler stub)
        => new(new HttpClient(stub), new SuperJournalOptions(new Uri(Base), TimeSpan.FromSeconds(5)), new StubTokens());

    private static string Paged(string item) => $$"""{"items":[{{item}}],"page":2,"pageSize":10,"totalCount":11}""";

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubTokens : ISuperJournalTokens
    {
        public string TokenFor(string subject) => "token-for:" + subject;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer;
        private readonly bool readBody;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
            : this((request, _) => Task.FromResult(answer(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer, bool readBody = true)
        {
            this.answer = answer;
            this.readBody = readBody;
        }

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = !readBody || request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method, request.RequestUri!.AbsoluteUri, body, request.Headers.Authorization?.ToString()));
            return await answer(request, cancellationToken);
        }
    }

    private sealed record SentRequest(HttpMethod Method, string Uri, string Body, string? Authorization);

    /// <summary>A stream that can only be read forward, once, as a request body read from the browser is.</summary>
    private sealed class OneWayStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 8192));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TrackedContent(byte[] bytes) : ByteArrayContent(bytes)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

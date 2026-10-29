using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Application.Abstractions;

namespace LifeOs.Infrastructure.Integrations;

/// <summary>
/// SuperJournal's API v1 over HTTP (D19). The request bodies are typed from SuperJournal's
/// OpenAPI document (<c>/swagger/v1/swagger.json</c>: <c>CreateJournalRequest</c>,
/// <c>AppendEntryRequest</c>, <c>EditEntryRequest</c>, <c>CreateDraftRequest</c>). The document
/// doesn't type its responses yet, so those follow SuperJournal's <c>Dtos/Responses.cs</c>.
/// <list type="bullet">
/// <item>Every request carries <c>Authorization: Bearer</c> with a token signed for the caller,
/// and goes only to <see cref="SuperJournalOptions.BaseUrl"/>.</item>
/// <item>Uploads stream: the file is sent as it's read (chunked), never held whole here.
/// Downloads stream back the same way.</item>
/// <item>A create's idempotency key goes as <c>Idempotency-Key</c>; a SuperJournal that doesn't
/// know the header yet ignores it.</item>
/// <item>Errors are <see cref="SuperJournalException"/> in words: 400 what was sent, 401 LifeOS's
/// token, 403 not the person's, 404 not there, 409 the entry moved on, 413 too big, 422 an
/// idempotency key already used for another request; anything
/// else, a timeout or no connection, SuperJournal being unavailable. No message carries a token.</item>
/// </list>
/// </summary>
public sealed class SuperJournalHttpClient(HttpClient http, SuperJournalOptions options, ISuperJournalTokens tokens) : ISuperJournal
{
    /// <summary>SuperJournal's header for a create's idempotency key (csdelan/SuperJournal#3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private const int MaxDetail = 300;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// An <see cref="HttpClient"/> for SuperJournal: pooled connections that notice DNS changes,
    /// and no timeout of its own, since each call sets one.
    /// </summary>
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("LifeOS/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    public async Task<SuperJournalJournal> OpenJournalAsync(
        JournalCaller caller, string subjectType, string subjectKey, string? title,
        CancellationToken cancellationToken = default)
    {
        // Owner-only: the person's own journals, in LifeOS's app.
        var body = new CreateJournalRequest(subjectType, subjectKey, title, []);
        var journal = await SendJsonAsync<JournalResponse>(
            caller, HttpMethod.Post, "api/v1/journals", body, "open the journal", nullOn404: false, cancellationToken);
        return ToJournal(journal!);
    }

    public async Task<SuperJournalJournal?> FindJournalAsync(
        JournalCaller caller, string subjectType, string subjectKey, CancellationToken cancellationToken = default)
    {
        var path = "api/v1/journals?" + Query(("subjectType", subjectType), ("subjectKey", subjectKey));
        var journal = await SendJsonAsync<JournalResponse>(
            caller, HttpMethod.Get, path, null, "find the journal", nullOn404: true, cancellationToken);
        return journal is null ? null : ToJournal(journal);
    }

    public async Task<SuperJournalJournal?> GetJournalAsync(
        JournalCaller caller, string journalId, CancellationToken cancellationToken = default)
    {
        var journal = await SendJsonAsync<JournalResponse>(
            caller, HttpMethod.Get, "api/v1/journals/" + Escape(journalId), null, "read the journal", nullOn404: true, cancellationToken);
        return journal is null ? null : ToJournal(journal);
    }

    public async Task<SuperJournalPage<SuperJournalJournal>> ListJournalsAsync(
        JournalCaller caller, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var path = "api/v1/journals?" + Query(("app", SuperJournalOptions.App), ("page", Number(page)), ("pageSize", Number(pageSize)));
        var list = await SendJsonAsync<PagedResponse<JournalResponse>>(
            caller, HttpMethod.Get, path, null, "list the journals", nullOn404: false, cancellationToken);
        return ToPage(list!, ToJournal);
    }

    public async Task<SuperJournalPage<SuperJournalEntry>> GetEntriesAsync(
        JournalCaller caller, string journalId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var path = $"api/v1/journals/{Escape(journalId)}/entries?"
                   + Query(("page", Number(page)), ("pageSize", Number(pageSize)), ("sort", "desc"));
        var list = await SendJsonAsync<PagedResponse<JournalEntryResponse>>(
            caller, HttpMethod.Get, path, null, "read the journal's entries", nullOn404: false, cancellationToken);
        return ToPage(list!, ToEntry);
    }

    public async Task<SuperJournalEntry> AppendEntryAsync(
        JournalCaller caller, string journalId, JournalText text, IReadOnlyList<string>? tags,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = new AppendEntryRequest(text.Content, text.ContentText, tags, null);
        var entry = await SendJsonAsync<JournalEntryResponse>(
            caller, HttpMethod.Post, $"api/v1/journals/{Escape(journalId)}/entries", body, "save the entry", nullOn404: false,
            cancellationToken, idempotencyKey);
        return ToEntry(entry!);
    }

    public async Task<SuperJournalEntry> CreateDraftAsync(
        JournalCaller caller, string journalId, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var entry = await SendJsonAsync<JournalEntryResponse>(
            caller, HttpMethod.Post, $"api/v1/journals/{Escape(journalId)}/entries/draft", new CreateDraftRequest(null),
            "start a draft", nullOn404: false, cancellationToken, idempotencyKey);
        return ToEntry(entry!);
    }

    public async Task<SuperJournalEntry?> GetEntryAsync(
        JournalCaller caller, string entryId, CancellationToken cancellationToken = default)
    {
        var entry = await SendJsonAsync<JournalEntryResponse>(
            caller, HttpMethod.Get, "api/v1/entries/" + Escape(entryId), null, "read the entry", nullOn404: true, cancellationToken);
        return entry is null ? null : ToEntry(entry);
    }

    public async Task<SuperJournalEntry> EditEntryAsync(
        JournalCaller caller, string entryId, JournalText text, int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        // trackHistory keeps the version it replaces, so every saved version can still be read.
        var body = new EditEntryRequest(text.Content, text.ContentText, expectedVersion);
        var entry = await SendJsonAsync<JournalEntryResponse>(
            caller, HttpMethod.Patch, $"api/v1/entries/{Escape(entryId)}?trackHistory=true", body, "save the edit",
            nullOn404: false, cancellationToken);
        return ToEntry(entry!);
    }

    public async Task<IReadOnlyList<SuperJournalRevision>> GetHistoryAsync(
        JournalCaller caller, string entryId, CancellationToken cancellationToken = default)
    {
        var revisions = await SendJsonAsync<List<EntryRevisionResponse>>(
            caller, HttpMethod.Get, $"api/v1/entries/{Escape(entryId)}/history", null, "read the entry's history",
            nullOn404: false, cancellationToken);
        return revisions!.Select(ToRevision).ToList();
    }

    public async Task<SuperJournalAttachment> UploadAttachmentAsync(
        JournalCaller caller, string entryId, Stream content, string fileName, string contentType,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        // A stream that can't seek has no length, so the body goes chunked as it's read.
        var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var form = new MultipartFormDataContent();
        form.Add(file, "file", fileName);

        using var response = await SendAsync(
            caller, HttpMethod.Post, $"api/v1/entries/{Escape(entryId)}/attachments", form, SuperJournalOptions.TransferTimeout,
            "upload the file", HttpCompletionOption.ResponseContentRead, cancellationToken, idempotencyKey);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw Failure(response.StatusCode, response.ReasonPhrase, body, "upload the file");
        }

        return ToAttachment(Read<AttachmentResponse>(body, "the upload"));
    }

    public async Task<SuperJournalDownload?> DownloadAttachmentAsync(
        JournalCaller caller, string attachmentId, bool thumbnail, CancellationToken cancellationToken = default)
    {
        var path = $"api/v1/attachments/{Escape(attachmentId)}/{(thumbnail ? "thumbnail" : "content")}";
        var doing = thumbnail ? "send the thumbnail" : "send the file";
        var response = await SendAsync(
            caller, HttpMethod.Get, path, null, SuperJournalOptions.TransferTimeout, doing,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var said = await response.Content.ReadAsStringAsync(cancellationToken);
                throw Failure(response.StatusCode, response.ReasonPhrase, said, doing);
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var type = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var disposition = response.Content.Headers.ContentDisposition;
            var name = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
            return new SuperJournalDownload(stream, type, name, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<SuperJournalPage<SuperJournalEntry>> SearchAsync(
        JournalCaller caller, string query, string? app, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var pairs = new List<(string, string)> { ("q", query), ("page", Number(page)), ("pageSize", Number(pageSize)) };
        if (app is not null)
        {
            pairs.Add(("app", app));
        }

        var list = await SendJsonAsync<PagedResponse<JournalEntryResponse>>(
            caller, HttpMethod.Get, "api/v1/search?" + Query([.. pairs]), null, "search", nullOn404: false, cancellationToken);
        return ToPage(list!, ToEntry);
    }

    // A JSON call: the answer read as T, null for a 404 when nullOn404, otherwise the failure in words.
    private async Task<T?> SendJsonAsync<T>(
        JournalCaller caller, HttpMethod method, string path, object? body, string doing, bool nullOn404,
        CancellationToken cancellationToken, string? idempotencyKey = null)
        where T : class
    {
        using var content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json);
        using var response = await SendAsync(
            caller, method, path, content, options.Timeout, doing, HttpCompletionOption.ResponseContentRead, cancellationToken,
            idempotencyKey);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (nullOn404 && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw Failure(response.StatusCode, response.ReasonPhrase, text, doing);
        }

        return Read<T>(text, doing);
    }

    private async Task<HttpResponseMessage> SendAsync(
        JournalCaller caller, HttpMethod method, string path, HttpContent? content, TimeSpan timeout, string doing,
        HttpCompletionOption completion, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, new Uri(options.BaseUrl, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.TokenFor(caller.Subject));
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
        }

        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            return await http.SendAsync(request, completion, timer.Token);
        }
        catch (Exception e) when (Inner<SuperJournalException>(e) is { } own)
        {
            // The upload's own size limit, from inside the body being sent.
            throw own;
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SuperJournalException(SuperJournalFailure.Unavailable,
                $"SuperJournal didn't answer in time to {doing}; try again later.", e);
        }
        catch (HttpRequestException e)
        {
            throw new SuperJournalException(SuperJournalFailure.Unavailable,
                $"SuperJournal couldn't be reached to {doing}; try again later.", e);
        }
    }

    private static SuperJournalException Failure(HttpStatusCode status, string? reason, string body, string doing)
    {
        var said = Said(body);
        var words = said is null ? "" : $" (“{said}”)";
        var answered = $"{(int)status} {reason}".TrimEnd();
        return status switch
        {
            HttpStatusCode.BadRequest => new SuperJournalException(SuperJournalFailure.Invalid,
                $"SuperJournal refused to {doing}{words}."),
            HttpStatusCode.Unauthorized => new SuperJournalException(SuperJournalFailure.Refused,
                "SuperJournal refused LifeOS's token: check SuperJournal__SigningKey here against the lifeos issuer's PublicKey there."),
            HttpStatusCode.Forbidden => new SuperJournalException(SuperJournalFailure.Forbidden,
                $"SuperJournal says that journal isn't the signed-in person's{words}."),
            HttpStatusCode.NotFound => new SuperJournalException(SuperJournalFailure.NotFound,
                said ?? $"SuperJournal has nothing there to {doing}."),
            HttpStatusCode.Conflict => new SuperJournalException(SuperJournalFailure.Conflict,
                $"The entry changed since it was read, so the edit wasn't saved{words}. Read it again and redo the edit."),
            HttpStatusCode.RequestEntityTooLarge => new SuperJournalException(SuperJournalFailure.TooLarge,
                $"The file is over SuperJournal's limit{words}."),
            HttpStatusCode.UnprocessableEntity => new SuperJournalException(SuperJournalFailure.Conflict,
                $"That Idempotency-Key was already used for a different request, so nothing was saved{words}. Send a new key."),
            _ => new SuperJournalException(SuperJournalFailure.Unavailable,
                $"SuperJournal answered {answered} when asked to {doing}{words}; try again later."),
        };
    }

    // SuperJournal's words from its ProblemDetails: the detail, or the validation errors.
    private static string? Said(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var words = string.Join("; ", errors.EnumerateObject()
                    .SelectMany(field => field.Value.ValueKind == JsonValueKind.Array
                        ? field.Value.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String).Select(m => m.GetString())
                        : []));
                if (words.Length > 0)
                {
                    return Trim(words);
                }
            }

            return root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(detail.GetString())
                ? Trim(detail.GetString()!)
                : null;
        }
        catch (JsonException)
        {
            return Trim(body);
        }
    }

    private static T Read<T>(string body, string what)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json) ?? throw Unreadable(what);
        }
        catch (JsonException e)
        {
            throw Unreadable(what, e);
        }
    }

    private static SuperJournalJournal ToJournal(JournalResponse j)
        => j.Id is null || j.SubjectType is null || j.SubjectKey is null
            ? throw Unreadable("the journal")
            : new SuperJournalJournal(j.Id, j.App ?? "", j.SubjectType, j.SubjectKey, j.Title, j.CreatedAt, j.IsArchived);

    private static SuperJournalEntry ToEntry(JournalEntryResponse e)
        => e.Id is null || e.JournalId is null || e.Version < 1
            ? throw Unreadable("the entry")
            : new SuperJournalEntry(
                e.Id, e.JournalId, e.Content, e.ContentText ?? "", e.Tags ?? [], e.AttachmentIds ?? [], e.Version,
                e.CreatedAt, e.UpdatedAt);

    private static SuperJournalRevision ToRevision(EntryRevisionResponse r)
        => r.Id is null || r.EntryId is null || r.SupersededVersion < 1
            ? throw Unreadable("the entry's history")
            : new SuperJournalRevision(r.Id, r.EntryId, r.SupersededVersion, r.PriorContent, r.PriorContentText ?? "", r.EditedAt);

    private static SuperJournalAttachment ToAttachment(AttachmentResponse a)
        => a.Id is null || a.EntryId is null || a.JournalId is null
            ? throw Unreadable("the upload")
            : new SuperJournalAttachment(
                a.Id, a.EntryId, a.JournalId, a.FileName ?? "", a.ContentType ?? "application/octet-stream", a.SizeBytes,
                a.IsImage, a.ThumbnailUrl is not null, a.UploadedAt);

    private static SuperJournalPage<TOut> ToPage<TIn, TOut>(PagedResponse<TIn> page, Func<TIn, TOut> map)
        => new((page.Items ?? []).Select(map).ToList(), page.Page, page.PageSize, page.TotalCount);

    private static SuperJournalException Unreadable(string what, Exception? inner = null)
        => new(SuperJournalFailure.Unavailable, $"SuperJournal's answer for {what} couldn't be read.", inner);

    private static T? Inner<T>(Exception? e) where T : Exception
    {
        for (; e is not null; e = e.InnerException)
        {
            if (e is T found)
            {
                return found;
            }
        }

        return null;
    }

    private static string Escape(string id) => Uri.EscapeDataString(id);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Trim(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= MaxDetail ? flat : flat[..MaxDetail] + "…";
    }

    private static string Query(params (string Name, string Value)[] pairs)
    {
        var builder = new StringBuilder();
        foreach (var (name, value) in pairs)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    // ---- Requests, as SuperJournal's OpenAPI document (v1) types them ----

    private sealed record CreateJournalRequest(string SubjectType, string SubjectKey, string? Title, string[] AllowedRoles);

    private sealed record AppendEntryRequest(
        JsonElement Content, string ContentText, IReadOnlyList<string>? Tags, string[]? AllowedRoles);

    private sealed record EditEntryRequest(JsonElement Content, string ContentText, int? ExpectedVersion);

    private sealed record CreateDraftRequest(string[]? AllowedRoles);

    // ---- Responses, as SuperJournal's Dtos/Responses.cs has them ----

    private sealed record JournalResponse(
        string? Id, string? OwnerAccountId, string? App, string? SubjectType, string? SubjectKey, string? Title,
        string[]? AllowedRoles, DateTimeOffset CreatedAt, bool IsArchived);

    private sealed record JournalEntryResponse(
        string? Id, string? JournalId, JsonElement Content, string? ContentText, string[]? Tags, string[]? AttachmentIds,
        string[]? AllowedRoles, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private sealed record EntryRevisionResponse(
        string? Id, string? EntryId, int SupersededVersion, JsonElement PriorContent, string? PriorContentText,
        DateTimeOffset EditedAt);

    private sealed record AttachmentResponse(
        string? Id, string? EntryId, string? JournalId, string? FileName, string? ContentType, long SizeBytes, bool IsImage,
        string? UploadedByAccountId, DateTimeOffset UploadedAt, string? ContentUrl, string? ThumbnailUrl);

    private sealed record PagedResponse<T>(IReadOnlyList<T>? Items, int Page, int PageSize, long TotalCount);
}

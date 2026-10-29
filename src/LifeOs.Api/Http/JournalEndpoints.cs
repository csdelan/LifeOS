using System.Security.Claims;
using System.Text.Json;
using LifeOs.Api.Auth;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Journals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace LifeOs.Api.Http;

/// <summary>
/// Journals and reviews kept in SuperJournal (D19, csdelan/LifeOS#28), for the signed-in owner.
/// The browser talks only to LifeOS; LifeOS calls SuperJournal server to server, signing a token
/// for the person, and writes a pointer to <c>bsk</c> for every saved version. Thin adapters over
/// <see cref="SuperJournalService"/>: no business logic lives here.
/// <list type="bullet">
/// <item>Entries carry <c>content</c> (the TipTap/ProseMirror document) and <c>contentText</c>
/// (its plain text); SuperJournal takes both.</item>
/// <item>An edit names the version it was made from (<c>expectedVersion</c>); when the entry has
/// moved on since, the answer is 409 and nothing is saved.</item>
/// <item>Uploads are multipart/form-data with the file in a part named <c>file</c>, streamed
/// through to SuperJournal as they arrive, up to 25 MB.</item>
/// <item>Adding an entry, starting a draft and uploading take an optional <c>Idempotency-Key</c>
/// header (1 to 255 characters, e.g. a UUID made when the save starts), passed on to SuperJournal:
/// a retry with the same key gets what the first try made, not a copy.</item>
/// <item>Without <c>SuperJournal:BaseUrl</c>, every call answers 503.</item>
/// </list>
/// </summary>
public static class JournalEndpoints
{
    private const string Immutable = "private, max-age=31536000, immutable";

    /// <summary>The header a create's idempotency key comes in, passed on to SuperJournal as it is.</summary>
    public const string IdempotencyKey = "Idempotency-Key";

    // Room for the multipart boundaries and headers around a file at the limit.
    private const long UploadOverhead = 64 * 1024;

    public static void MapJournalEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/journals")
            .WithTags("Journals")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapPost("/", Open).WithName("OpenJournal")
            .Produces<JournalView>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable);
        api.MapGet("/", Find).WithName("FindJournal")
            .Produces<JournalView>().Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/reconcile", Reconcile).WithName("ReconcileJournalPointers")
            .Produces<JournalReconcile>();
        api.MapGet("/{journalId}", GetJournal).WithName("GetJournalById")
            .Produces<JournalView>().Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapGet("/{journalId}/entries", GetEntries).WithName("GetJournalEntries")
            .Produces<JournalPageView<JournalEntryView>>();
        api.MapPost("/{journalId}/entries", AddEntry).WithName("AddJournalEntry")
            .Produces<SavedJournalEntry>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/{journalId}/drafts", StartDraft).WithName("StartJournalDraft")
            .Produces<JournalEntryView>();
        api.MapGet("/entries/{entryId}", GetEntry).WithName("GetJournalEntry")
            .Produces<JournalEntryView>().Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPatch("/entries/{entryId}", EditEntry).WithName("EditJournalEntry")
            .Produces<SavedJournalEntry>().Produces<ApiError>(StatusCodes.Status409Conflict);
        api.MapGet("/entries/{entryId}/history", GetHistory).WithName("GetJournalEntryHistory")
            .Produces<IReadOnlyList<JournalRevisionView>>();
        api.MapPost("/entries/{entryId}/attachments", Upload).WithName("UploadJournalAttachment")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<JournalAttachmentView>().Produces<ApiError>(StatusCodes.Status413PayloadTooLarge)
            .WithMetadata(new RequestSizeLimitAttribute(SuperJournalService.MaxAttachmentBytes + UploadOverhead));
        api.MapGet("/attachments/{attachmentId}/content", (string attachmentId, ClaimsPrincipal user, SuperJournalService journals, HttpContext context, CancellationToken ct)
                => Download(attachmentId, thumbnail: false, user, journals, context, ct))
            .WithName("GetJournalAttachmentContent")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "image/png", "image/jpeg", "image/webp", "image/gif", "application/pdf", "text/plain", "text/csv")
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapGet("/attachments/{attachmentId}/thumbnail", (string attachmentId, ClaimsPrincipal user, SuperJournalService journals, HttpContext context, CancellationToken ct)
                => Download(attachmentId, thumbnail: true, user, journals, context, ct))
            .WithName("GetJournalAttachmentThumbnail")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "image/png")
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Open(
        OpenJournalBody body, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
        => Results.Ok(View(await journals.OpenJournalAsync(
            JournalCallers.For(user), body.SubjectType, body.SubjectKey, body.Title, ct)));

    private static async Task<IResult> Find(
        string? subjectType, string? subjectKey, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
    {
        var found = await journals.FindJournalAsync(JournalCallers.For(user), subjectType ?? "", subjectKey ?? "", ct);
        return found is null
            ? Results.NotFound(new ApiError($"There's no journal for {subjectType} {subjectKey} yet."))
            : Results.Ok(View(found));
    }

    private static async Task<IResult> GetJournal(
        string journalId, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
    {
        var found = await journals.GetJournalAsync(JournalCallers.For(user), journalId, ct);
        return found is null ? Results.NotFound(new ApiError($"SuperJournal has no journal {journalId}.")) : Results.Ok(View(found));
    }

    private static async Task<IResult> GetEntries(
        string journalId, int? page, int? pageSize, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
    {
        var entries = await journals.GetEntriesAsync(JournalCallers.For(user), journalId, page ?? 1, pageSize ?? 20, ct);
        return Results.Ok(new JournalPageView<JournalEntryView>(
            entries.Items.Select(View).ToList(), entries.Page, entries.PageSize, entries.TotalCount));
    }

    private static async Task<IResult> AddEntry(
        string journalId, JournalEntryBody body, [FromHeader(Name = IdempotencyKey)] string? idempotencyKey,
        ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
        => Results.Ok(View(await journals.AddEntryAsync(
            JournalCallers.For(user), journalId, new JournalText(body.Content, body.ContentText!), body.Tags, idempotencyKey, ct)));

    private static async Task<IResult> StartDraft(
        string journalId, [FromHeader(Name = IdempotencyKey)] string? idempotencyKey, ClaimsPrincipal user,
        SuperJournalService journals, CancellationToken ct)
        => Results.Ok(View(await journals.CreateDraftAsync(JournalCallers.For(user), journalId, idempotencyKey, ct)));

    private static async Task<IResult> GetEntry(
        string entryId, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
    {
        var entry = await journals.GetEntryAsync(JournalCallers.For(user), entryId, ct);
        return entry is null ? Results.NotFound(new ApiError($"SuperJournal has no entry {entryId}.")) : Results.Ok(View(entry));
    }

    private static async Task<IResult> EditEntry(
        string entryId, JournalEditBody body, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
        => Results.Ok(View(await journals.EditEntryAsync(
            JournalCallers.For(user), entryId, new JournalText(body.Content, body.ContentText!), body.ExpectedVersion, ct)));

    private static async Task<IResult> GetHistory(
        string entryId, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
    {
        var revisions = await journals.GetHistoryAsync(JournalCallers.For(user), entryId, ct);
        return Results.Ok(revisions
            .Select(r => new JournalRevisionView(r.SupersededVersion, r.PriorContent, r.PriorContentText, r.EditedAt))
            .ToList());
    }

    private static async Task<IResult> Reconcile(
        string? journal, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
        => Results.Ok(await journals.ReconcileAsync(JournalCallers.For(user), journal, ct));

    /// <summary>
    /// The file part of a multipart/form-data body, streamed through to SuperJournal as it's read:
    /// it is never buffered whole, in memory or on disk.
    /// </summary>
    private static async Task<IResult> Upload(
        string entryId, HttpRequest request, ClaimsPrincipal user, SuperJournalService journals, CancellationToken ct)
    {
        var caller = JournalCallers.For(user);
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            || !string.Equals(type.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 } boundary)
        {
            return Results.BadRequest(new ApiError("Send the file as multipart/form-data, in a part named file."));
        }

        var reader = new MultipartReader(boundary, request.Body);
        for (var section = await reader.ReadNextSectionAsync(ct); section is not null; section = await reader.ReadNextSectionAsync(ct))
        {
            // Other parts are skipped: the next read drains them.
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, "file", StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = HeaderUtilities.RemoveQuotes(
                disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
            var attachment = await journals.UploadAttachmentAsync(
                caller, entryId, section.Body, fileName, section.ContentType, request.Headers[IdempotencyKey].FirstOrDefault(), ct);
            return Results.Ok(View(attachment));
        }

        return Results.BadRequest(new ApiError("Send the file as multipart/form-data, in a part named file."));
    }

    /// <summary>
    /// An attachment's bytes, streamed from SuperJournal. They never change, so the answer is
    /// cached for a year in the browser. Images show inline; anything else downloads, sandboxed,
    /// so a stored page can never run as this site.
    /// </summary>
    private static async Task<IResult> Download(
        string attachmentId, bool thumbnail, ClaimsPrincipal user, SuperJournalService journals, HttpContext context,
        CancellationToken ct)
    {
        var download = await journals.DownloadAttachmentAsync(JournalCallers.For(user), attachmentId, thumbnail, ct);
        if (download is null)
        {
            return Results.NotFound(new ApiError(thumbnail
                ? $"Attachment {attachmentId} has no thumbnail."
                : $"SuperJournal has no attachment {attachmentId}."));
        }

        context.Response.RegisterForDisposeAsync(download);
        var headers = context.Response.Headers;
        headers.CacheControl = Immutable;
        var image = IsImage(download.ContentType);
        if (!image)
        {
            headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        }

        return Results.Stream(
            download.Content, download.ContentType,
            fileDownloadName: image ? null : download.FileName ?? $"attachment-{attachmentId}");
    }

    private static bool IsImage(string contentType)
        => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
           && !contentType.StartsWith("image/svg", StringComparison.OrdinalIgnoreCase);

    private static JournalView View(SuperJournalJournal j)
        => new(j.Id, j.SubjectType, j.SubjectKey, j.Title, j.CreatedAt, j.IsArchived);

    private static JournalEntryView View(SuperJournalEntry e)
        => new(e.Id, e.JournalId, e.Content, e.ContentText, e.Tags, e.AttachmentIds, e.Version, e.CreatedAt, e.UpdatedAt);

    private static SavedJournalEntry View(JournalSave save)
        => new(View(save.Entry), save.PointerEventId, save.PointerPending);

    private static JournalAttachmentView View(SuperJournalAttachment a)
        => new(a.Id, a.EntryId, a.FileName, a.ContentType, a.SizeBytes, a.IsImage, a.HasThumbnail, a.UploadedAt);
}

public sealed class OpenJournalBody
{
    /// <summary>What kind of thing the journal is about, e.g. <c>daily</c> or <c>weekly</c>.</summary>
    public string SubjectType { get; set; } = "";

    /// <summary>Which one, e.g. <c>2026-10-28</c>.</summary>
    public string SubjectKey { get; set; } = "";

    public string? Title { get; set; }
}

public sealed class JournalEntryBody
{
    /// <summary>The TipTap/ProseMirror document.</summary>
    public JsonElement Content { get; set; }

    /// <summary>Its plain text; may be empty, but must be sent.</summary>
    public string? ContentText { get; set; }

    public List<string>? Tags { get; set; }
}

public sealed class JournalEditBody
{
    public JsonElement Content { get; set; }

    public string? ContentText { get; set; }

    /// <summary>The version the edit was made from.</summary>
    public int ExpectedVersion { get; set; }
}

public sealed record JournalView(
    string Id, string SubjectType, string SubjectKey, string? Title, DateTimeOffset CreatedAt, bool IsArchived);

public sealed record JournalEntryView(
    string Id,
    string JournalId,
    JsonElement Content,
    string ContentText,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> AttachmentIds,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A save: the entry at its new version, and the <c>bsk</c> pointer event for it. When
/// <paramref name="PointerPending"/> the pointer couldn't be written yet; the text is safe in
/// SuperJournal and the pointer is retried.
/// </summary>
public sealed record SavedJournalEntry(JournalEntryView Entry, Guid? PointerEventId, bool PointerPending);

/// <summary>A version an edit replaced, and when.</summary>
public sealed record JournalRevisionView(int Version, JsonElement Content, string ContentText, DateTimeOffset ReplacedAt);

public sealed record JournalAttachmentView(
    string Id, string EntryId, string FileName, string ContentType, long SizeBytes, bool IsImage, bool HasThumbnail,
    DateTimeOffset UploadedAt);

public sealed record JournalPageView<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);

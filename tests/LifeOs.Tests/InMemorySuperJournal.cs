using System.Collections.Concurrent;
using System.Text.Json;
using LifeOs.Application.Abstractions;

namespace LifeOs.Tests;

/// <summary>
/// SuperJournal in memory, for the journal tests (D19): journals keyed by person and subject,
/// entries with versions and the history edits leave, attachments whose bytes are read as
/// they're streamed, and the calls it was asked for. It behaves as SuperJournal does where LifeOS
/// relies on it: an edit from a version the entry has moved on from is a conflict, a draft starts
/// at version 1 with an empty document, another person's journal is forbidden, and a create
/// retried with its idempotency key gets what the first made (the same key with another request
/// is a conflict). No test calls the real SuperJournal through it.
/// </summary>
internal sealed class InMemorySuperJournal : ISuperJournal
{
    public const string App = "lifeos";

    private static readonly JsonElement EmptyDoc = JsonSerializer.SerializeToElement(new { type = "doc", content = Array.Empty<object>() });

    private readonly object gate = new();
    private readonly Dictionary<string, (string Owner, SuperJournalJournal Journal)> journals = new();
    private readonly Dictionary<string, SuperJournalEntry> entries = new();
    private readonly Dictionary<string, List<SuperJournalRevision>> history = new();
    private readonly Dictionary<string, (SuperJournalAttachment Attachment, byte[] Bytes)> attachments = new();
    private readonly Dictionary<(string Owner, string Key), (string Request, string Id)> keys = new();

    /// <summary>When the next save happens (each save moves it on a second), so tests can place versions in time.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Thrown by every call while set, as when SuperJournal is down.</summary>
    public SuperJournalException? Failure { get; set; }

    /// <summary>Who each call was for, in order.</summary>
    public ConcurrentQueue<JournalCaller> Callers { get; } = new();

    /// <summary>The idempotency key each create came with (null for none), in order.</summary>
    public ConcurrentQueue<string?> IdempotencyKeys { get; } = new();

    /// <summary>How many bytes each upload had in all.</summary>
    public List<long> UploadSizes { get; } = [];

    /// <summary>
    /// Set once an upload's first bytes have arrived here, before the rest is read: a test that
    /// holds the rest of its body back until then shows the upload streams through.
    /// </summary>
    public TaskCompletionSource FirstUploadBytes { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<SuperJournalJournal> OpenJournalAsync(
        JournalCaller caller, string subjectType, string subjectKey, string? title, CancellationToken cancellationToken = default)
        => Run(caller, () =>
        {
            var found = journals.Values.FirstOrDefault(j =>
                j.Owner == caller.Subject && j.Journal.SubjectType == subjectType && j.Journal.SubjectKey == subjectKey);
            if (found.Journal is not null)
            {
                return found.Journal;
            }

            var journal = new SuperJournalJournal(NewId(), App, subjectType, subjectKey, title, Tick(), false);
            journals[journal.Id] = (caller.Subject, journal);
            return journal;
        });

    public Task<SuperJournalJournal?> FindJournalAsync(
        JournalCaller caller, string subjectType, string subjectKey, CancellationToken cancellationToken = default)
        => Run(caller, () => journals.Values
            .Where(j => j.Owner == caller.Subject && j.Journal.SubjectType == subjectType && j.Journal.SubjectKey == subjectKey)
            .Select(j => j.Journal)
            .FirstOrDefault());

    public Task<SuperJournalJournal?> GetJournalAsync(JournalCaller caller, string journalId, CancellationToken cancellationToken = default)
        => Run(caller, () => journals.ContainsKey(journalId) ? Owned(caller, journalId) : null);

    public Task<SuperJournalPage<SuperJournalJournal>> ListJournalsAsync(
        JournalCaller caller, int page, int pageSize, CancellationToken cancellationToken = default)
        => Run(caller, () => Page(
            journals.Values.Where(j => j.Owner == caller.Subject).Select(j => j.Journal).OrderBy(j => j.CreatedAt).ToList(),
            page, pageSize));

    public Task<SuperJournalPage<SuperJournalEntry>> GetEntriesAsync(
        JournalCaller caller, string journalId, int page, int pageSize, CancellationToken cancellationToken = default)
        => Run(caller, () =>
        {
            Owned(caller, journalId);
            return Page(
                entries.Values.Where(e => e.JournalId == journalId).OrderByDescending(e => e.CreatedAt).ToList(), page, pageSize);
        });

    public Task<SuperJournalEntry> AppendEntryAsync(
        JournalCaller caller, string journalId, JournalText text, IReadOnlyList<string>? tags,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => Append(caller, journalId, text, tags, idempotencyKey,
            $"entry|{journalId}|{text.Content.GetRawText()}|{text.ContentText}|{string.Join(',', tags ?? [])}");

    public Task<SuperJournalEntry> CreateDraftAsync(
        JournalCaller caller, string journalId, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => Append(caller, journalId, new JournalText(EmptyDoc, ""), null, idempotencyKey, $"draft|{journalId}");

    private Task<SuperJournalEntry> Append(
        JournalCaller caller, string journalId, JournalText text, IReadOnlyList<string>? tags, string? idempotencyKey, string request)
        => Run(caller, () =>
        {
            IdempotencyKeys.Enqueue(idempotencyKey);
            Owned(caller, journalId);
            if (Replayed(caller, idempotencyKey, request) is { } id)
            {
                return entries[id];
            }

            var at = Tick();
            var entry = new SuperJournalEntry(
                NewId(), journalId, text.Content.Clone(), text.ContentText, tags?.ToList() ?? [], [], 1, at, at);
            entries[entry.Id] = entry;
            Remember(caller, idempotencyKey, request, entry.Id);
            return entry;
        });

    public Task<SuperJournalEntry?> GetEntryAsync(JournalCaller caller, string entryId, CancellationToken cancellationToken = default)
        => Run(caller, () =>
        {
            if (!entries.TryGetValue(entryId, out var entry))
            {
                return null;
            }

            Owned(caller, entry.JournalId);
            return entry;
        });

    public Task<SuperJournalEntry> EditEntryAsync(
        JournalCaller caller, string entryId, JournalText text, int expectedVersion, CancellationToken cancellationToken = default)
        => Run(caller, () =>
        {
            var current = Entry(caller, entryId);
            if (current.Version != expectedVersion)
            {
                throw new SuperJournalException(SuperJournalFailure.Conflict,
                    $"The entry changed since it was read (it's at version {current.Version}).");
            }

            var at = Tick();
            var revisions = history.TryGetValue(entryId, out var list) ? list : history[entryId] = [];
            revisions.Add(new SuperJournalRevision(NewId(), entryId, current.Version, current.Content, current.ContentText, at));
            var edited = current with
            {
                Content = text.Content.Clone(), ContentText = text.ContentText, Version = current.Version + 1, UpdatedAt = at,
            };
            entries[entryId] = edited;
            return edited;
        });

    public Task<IReadOnlyList<SuperJournalRevision>> GetHistoryAsync(
        JournalCaller caller, string entryId, CancellationToken cancellationToken = default)
        => Run(caller, () =>
        {
            Entry(caller, entryId);
            return (IReadOnlyList<SuperJournalRevision>)(history.TryGetValue(entryId, out var list) ? list.ToList() : []);
        });

    public async Task<SuperJournalAttachment> UploadAttachmentAsync(
        JournalCaller caller, string entryId, Stream content, string fileName, string contentType,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        IdempotencyKeys.Enqueue(idempotencyKey);
        await Run(caller, () => Entry(caller, entryId));
        var copy = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copy.Write(buffer, 0, read);
            FirstUploadBytes.TrySetResult();
        }
        var request = $"upload|{entryId}|{fileName}|{contentType}|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(copy.ToArray()))}";
        return await Run(caller, () =>
        {
            var entry = Entry(caller, entryId);
            if (Replayed(caller, idempotencyKey, request) is { } id)
            {
                return attachments[id].Attachment;
            }

            var image = contentType.StartsWith("image/", StringComparison.Ordinal);
            var attachment = new SuperJournalAttachment(
                NewId(), entryId, entry.JournalId, fileName, contentType, copy.Length, image, image, Tick());
            attachments[attachment.Id] = (attachment, copy.ToArray());
            entries[entryId] = entry with { AttachmentIds = [.. entry.AttachmentIds, attachment.Id] };
            UploadSizes.Add(copy.Length);
            Remember(caller, idempotencyKey, request, attachment.Id);
            return attachment;
        });
    }

    public Task<SuperJournalDownload?> DownloadAttachmentAsync(
        JournalCaller caller, string attachmentId, bool thumbnail, CancellationToken cancellationToken = default)
        => Run(caller, () =>
        {
            if (!attachments.TryGetValue(attachmentId, out var found) || (thumbnail && !found.Attachment.HasThumbnail))
            {
                return null;
            }

            Owned(caller, found.Attachment.JournalId);
            return new SuperJournalDownload(
                new MemoryStream(found.Bytes, writable: false),
                thumbnail ? "image/png" : found.Attachment.ContentType,
                found.Attachment.FileName);
        });

    public Task<SuperJournalPage<SuperJournalEntry>> SearchAsync(
        JournalCaller caller, string query, string? app, int page, int pageSize, CancellationToken cancellationToken = default)
        => Run(caller, () => Page(
            entries.Values
                .Where(e => journals[e.JournalId].Owner == caller.Subject
                            && e.ContentText.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.UpdatedAt)
                .ToList(),
            page, pageSize));

    /// <summary>The entry as SuperJournal holds it now.</summary>
    public SuperJournalEntry Stored(string entryId)
    {
        lock (gate)
        {
            return entries[entryId];
        }
    }

    /// <summary>A document with one paragraph of <paramref name="text"/>, and its plain text.</summary>
    public static JournalText Text(string text)
        => new(
            JsonSerializer.SerializeToElement(new
            {
                type = "doc",
                content = new object[] { new { type = "paragraph", content = new object[] { new { type = "text", text } } } },
            }),
            text);

    private Task<T> Run<T>(JournalCaller caller, Func<T> call)
    {
        Callers.Enqueue(caller);
        if (Failure is { } failure)
        {
            return Task.FromException<T>(failure);
        }

        lock (gate)
        {
            try
            {
                return Task.FromResult(call());
            }
            catch (SuperJournalException e)
            {
                return Task.FromException<T>(e);
            }
        }
    }

    // What the first create with this key made, or null for a new key. The same key with another request is refused.
    private string? Replayed(JournalCaller caller, string? key, string request)
    {
        if (key is null || !keys.TryGetValue((caller.Subject, key), out var first))
        {
            return null;
        }

        return first.Request == request
            ? first.Id
            : throw new SuperJournalException(SuperJournalFailure.Conflict,
                "That Idempotency-Key was already used for a different request, so nothing was saved. Send a new key.");
    }

    private void Remember(JournalCaller caller, string? key, string request, string id)
    {
        if (key is not null)
        {
            keys[(caller.Subject, key)] = (request, id);
        }
    }

    private SuperJournalJournal Owned(JournalCaller caller, string journalId)
    {
        if (!journals.TryGetValue(journalId, out var found))
        {
            throw new SuperJournalException(SuperJournalFailure.NotFound, $"Journal '{journalId}' not found.");
        }

        return found.Owner == caller.Subject
            ? found.Journal
            : throw new SuperJournalException(SuperJournalFailure.Forbidden, "SuperJournal says that journal isn't the signed-in person's.");
    }

    private SuperJournalEntry Entry(JournalCaller caller, string entryId)
    {
        if (!entries.TryGetValue(entryId, out var entry))
        {
            throw new SuperJournalException(SuperJournalFailure.NotFound, $"Entry '{entryId}' not found.");
        }

        Owned(caller, entry.JournalId);
        return entry;
    }

    private DateTimeOffset Tick()
    {
        var at = Now;
        Now = Now.AddSeconds(1);
        return at;
    }

    private static SuperJournalPage<T> Page<T>(IReadOnlyList<T> all, int page, int pageSize)
        => new(all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, all.Count);

    private static string NewId() => Guid.NewGuid().ToString("n");
}

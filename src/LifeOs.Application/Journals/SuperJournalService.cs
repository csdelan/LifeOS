using System.Collections.Concurrent;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Journals;

/// <summary>
/// Journal and review text in SuperJournal, with a pointer in <c>bsk</c> for every saved version
/// (D19). Every door (the web through <c>/api</c>, an agent through <c>/mcp</c>) saves through
/// here, as a <see cref="JournalCaller"/>.
/// <list type="bullet">
/// <item><b>Write order.</b> SuperJournal first, then the pointer, so a pointer never points at
/// nothing. Once SuperJournal has a version, neither write is cancelled with the request.</item>
/// <item><b>Once per version.</b> The pointer's <c>external_id</c> is the entry id and version,
/// and migration 0049 keeps one pointer per entry version from any source, so a repeated write
/// changes nothing.</item>
/// <item><b>Reconcile.</b> A pointer that couldn't be written is kept here, with who saved it,
/// and written by the next save or by <see cref="RetryPendingAsync"/> (the API runs it every
/// minute). Each save also checks the newest entries of its journal, and
/// <see cref="ReconcileAsync"/> checks every journal, for versions with no pointer at all (the
/// server stopped between the two writes); those get one marked <c>reconciled</c>, with the
/// reconciling door as source, since who saved them is lost. Versions saved in the last
/// <see cref="SettleWindow"/> are left to the save still writing them.</item>
/// <item><b>Drafts.</b> An empty draft (version 1 with no text and nothing in it) has no
/// pointer; its first edit has.</item>
/// </list>
/// </summary>
public sealed class SuperJournalService(
    ISuperJournal? journal,
    IJournalPointerStore pointers,
    IClock clock,
    Action<string, Exception>? warn = null)
{
    /// <summary>How long a just-saved version is left to its own save before a reconcile writes its pointer.</summary>
    public static readonly TimeSpan SettleWindow = TimeSpan.FromMinutes(2);

    /// <summary>The largest page asked of SuperJournal.</summary>
    public const int MaxPageSize = 100;

    /// <summary>The largest attachment: SuperJournal's default limit, 25 MB.</summary>
    public const long MaxAttachmentBytes = ArtifactLimits.DefaultMaxBytes;

    /// <summary>The longest idempotency key SuperJournal takes.</summary>
    public const int MaxIdempotencyKeyLength = 255;

    /// <summary>How many of a journal's newest entries a save checks for missing pointers.</summary>
    private const int SaveCheckSize = 20;

    // What SuperJournal's AttachmentPolicy takes, checked here so a refused upload isn't streamed first.
    private static readonly HashSet<string> AttachmentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/webp", "image/gif",
        "application/pdf", "text/csv", "text/plain",
    };

    // ProseMirror nodes that hold no content of their own.
    private static readonly HashSet<string> EmptyNodes = new(StringComparer.Ordinal)
    {
        "doc", "paragraph", "text", "hardBreak",
    };

    private readonly ConcurrentDictionary<(string EntryId, int Version), JournalPointer> pending = new();
    private readonly ConcurrentDictionary<string, JournalSubject> subjects = new(StringComparer.Ordinal);

    /// <summary>Whether this server can reach SuperJournal (<c>SuperJournal:BaseUrl</c> is set).</summary>
    public bool IsConfigured => journal is not null;

    /// <summary>Pointers whose write didn't land yet, waiting for the next save or retry.</summary>
    public int PendingCount => pending.Count;

    /// <summary>The person's journal for this subject, made the first time it's asked for.</summary>
    public async Task<SuperJournalJournal> OpenJournalAsync(
        JournalCaller caller, string subjectType, string subjectKey, string? title,
        CancellationToken cancellationToken = default)
    {
        var sj = Require();
        var type = Required(subjectType, "subjectType", 200);
        var key = Required(subjectKey, "subjectKey", 400);
        var name = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (name is { Length: > 500 })
        {
            throw Invalid("title can be at most 500 characters.");
        }

        return Remember(await sj.OpenJournalAsync(caller, type, key, name, cancellationToken));
    }

    /// <summary>The person's journal for this subject, or null when there isn't one yet.</summary>
    public async Task<SuperJournalJournal?> FindJournalAsync(
        JournalCaller caller, string subjectType, string subjectKey, CancellationToken cancellationToken = default)
    {
        var found = await Require().FindJournalAsync(
            caller, Required(subjectType, "subjectType", 200), Required(subjectKey, "subjectKey", 400), cancellationToken);
        return found is null ? null : Remember(found);
    }

    /// <summary>A journal by id, or null.</summary>
    public async Task<SuperJournalJournal?> GetJournalAsync(
        JournalCaller caller, string journalId, CancellationToken cancellationToken = default)
    {
        var found = await Require().GetJournalAsync(caller, Required(journalId, "journalId", 200), cancellationToken);
        return found is null ? null : Remember(found);
    }

    /// <summary>A journal's entries, newest first.</summary>
    public Task<SuperJournalPage<SuperJournalEntry>> GetEntriesAsync(
        JournalCaller caller, string journalId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        => Require().GetEntriesAsync(
            caller, Required(journalId, "journalId", 200), Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken);

    /// <summary>An entry at its current version, or null.</summary>
    public Task<SuperJournalEntry?> GetEntryAsync(JournalCaller caller, string entryId, CancellationToken cancellationToken = default)
        => Require().GetEntryAsync(caller, Required(entryId, "entryId", 200), cancellationToken);

    /// <summary>The versions an entry's edits replaced, oldest first.</summary>
    public Task<IReadOnlyList<SuperJournalRevision>> GetHistoryAsync(
        JournalCaller caller, string entryId, CancellationToken cancellationToken = default)
        => Require().GetHistoryAsync(caller, Required(entryId, "entryId", 200), cancellationToken);

    /// <summary>The person's entries matching <paramref name="query"/>, across their apps unless <paramref name="app"/> narrows it.</summary>
    public Task<SuperJournalPage<SuperJournalEntry>> SearchAsync(
        JournalCaller caller, string query, string? app = null, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Require().SearchAsync(
            caller, Required(query, "q", 500), string.IsNullOrWhiteSpace(app) ? null : app.Trim(),
            Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken);

    /// <summary>
    /// Adds an entry to the journal, then its pointer. An entry with no text and nothing in it is
    /// refused: start a draft to add pictures first. With <paramref name="idempotencyKey"/>, a
    /// retry of the same save gets the entry the first one made (and its pointer), not a second one.
    /// </summary>
    public async Task<JournalSave> AddEntryAsync(
        JournalCaller caller, string journalId, JournalText text, IReadOnlyList<string>? tags = null,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var sj = Require();
        journalId = Required(journalId, "journalId", 200);
        idempotencyKey = Key(idempotencyKey);
        CheckText(text);
        if (IsEmpty(text.ContentText, text.Content))
        {
            throw Invalid("An entry needs some text or a picture; to add pictures first, start a draft.");
        }

        var cleanTags = CleanTags(tags);
        var subject = await SubjectOfAsync(sj, caller, journalId, cancellationToken);
        await RetryPendingAsync(cancellationToken);

        var entry = await sj.AppendEntryAsync(caller, journalId, text, cleanTags, idempotencyKey, CancellationToken.None);
        var save = await PointAsync(caller, entry, subject);
        await CheckJournalAsync(sj, caller, journalId, subject, cancellationToken);
        return save;
    }

    /// <summary>Starts an empty entry, so pictures can be uploaded before there's text. It has no pointer until its first edit.</summary>
    public Task<SuperJournalEntry> CreateDraftAsync(
        JournalCaller caller, string journalId, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => Require().CreateDraftAsync(caller, Required(journalId, "journalId", 200), Key(idempotencyKey), cancellationToken);

    /// <summary>
    /// Saves a new version of the entry, then its pointer. <paramref name="expectedVersion"/> is the
    /// version the edit was made from: when the entry has moved on since, nothing is saved and
    /// <see cref="SuperJournalFailure.Conflict"/> says to read it again.
    /// </summary>
    public async Task<JournalSave> EditEntryAsync(
        JournalCaller caller, string entryId, JournalText text, int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var sj = Require();
        entryId = Required(entryId, "entryId", 200);
        CheckText(text);
        if (expectedVersion < 1)
        {
            throw Invalid("expectedVersion must be the version the edit was made from (1 or more).");
        }

        var current = await sj.GetEntryAsync(caller, entryId, cancellationToken)
                      ?? throw new SuperJournalException(SuperJournalFailure.NotFound, $"SuperJournal has no entry {entryId}.");
        var subject = await SubjectOfAsync(sj, caller, current.JournalId, cancellationToken);
        await RetryPendingAsync(cancellationToken);

        var entry = await sj.EditEntryAsync(caller, entryId, text, expectedVersion, CancellationToken.None);
        var save = await PointAsync(caller, entry, subject);
        await CheckJournalAsync(sj, caller, entry.JournalId, subject, cancellationToken);
        return save;
    }

    /// <summary>
    /// Attaches a file to an entry, streaming it to SuperJournal. Refused before anything is sent
    /// when its type isn't one SuperJournal takes, and stopped once it passes
    /// <see cref="MaxAttachmentBytes"/>.
    /// </summary>
    public Task<SuperJournalAttachment> UploadAttachmentAsync(
        JournalCaller caller, string entryId, Stream content, string? fileName, string? contentType,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var sj = Require();
        entryId = Required(entryId, "entryId", 200);
        idempotencyKey = Key(idempotencyKey);
        var name = Path.GetFileName(fileName?.Trim() ?? "");
        if (name.Length == 0)
        {
            throw Invalid("The file needs a name.");
        }

        var type = contentType?.Split(';')[0].Trim() ?? "";
        if (!AttachmentTypes.Contains(type))
        {
            throw Invalid(
                $"SuperJournal takes PNG, JPEG, WebP or GIF pictures, PDFs, CSV and plain text, not '{(type.Length == 0 ? "no type" : type)}'.");
        }

        return sj.UploadAttachmentAsync(
            caller, entryId, new LimitedStream(content, MaxAttachmentBytes), name, type.ToLowerInvariant(), idempotencyKey,
            cancellationToken);
    }

    /// <summary>An attachment's bytes, or its thumbnail, streamed from SuperJournal; null when there's none.</summary>
    public Task<SuperJournalDownload?> DownloadAttachmentAsync(
        JournalCaller caller, string attachmentId, bool thumbnail = false, CancellationToken cancellationToken = default)
        => Require().DownloadAttachmentAsync(caller, Required(attachmentId, "attachmentId", 200), thumbnail, cancellationToken);

    /// <summary>
    /// Writes the pointers whose own write didn't land, with the source that saved them. Stops at
    /// the first that still fails; the rest wait for the next try. Returns how many it wrote.
    /// </summary>
    public async Task<int> RetryPendingAsync(CancellationToken cancellationToken = default)
    {
        var written = 0;
        foreach (var (key, pointer) in pending.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var write = await pointers.AppendAsync(pointer, clock.UtcNow, CancellationToken.None);
                pending.TryRemove(key, out _);
                written += write.Written ? 1 : 0;
            }
            catch (Exception e)
            {
                warn?.Invoke($"A journal pointer is still waiting to be written (entry {pointer.EntryId}, version {pointer.Version}).", e);
                break;
            }
        }

        return written;
    }

    /// <summary>
    /// Writes every missing pointer for the person's journals in LifeOS, or only for
    /// <paramref name="journalId"/>: first the ones waiting here, then any version SuperJournal
    /// has that <c>bsk</c> has no pointer for (see the class summary).
    /// </summary>
    public async Task<JournalReconcile> ReconcileAsync(
        JournalCaller caller, string? journalId = null, CancellationToken cancellationToken = default)
    {
        var sj = Require();
        var written = await RetryPendingAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(journalId))
        {
            var id = journalId.Trim();
            var subject = await SubjectOfAsync(sj, caller, id, cancellationToken);
            written += await ReconcileJournalAsync(sj, caller, id, subject, int.MaxValue, cancellationToken);
            return new JournalReconcile(1, written, PendingCount);
        }

        var journals = 0;
        for (var page = 1; ; page++)
        {
            var list = await sj.ListJournalsAsync(caller, page, MaxPageSize, cancellationToken);
            foreach (var found in list.Items)
            {
                Remember(found);
                var subject = new JournalSubject(found.SubjectType, found.SubjectKey);
                written += await ReconcileJournalAsync(sj, caller, found.Id, subject, int.MaxValue, cancellationToken);
                journals++;
            }

            if (list.Items.Count < MaxPageSize || (long)page * MaxPageSize >= list.TotalCount)
            {
                break;
            }
        }

        return new JournalReconcile(journals, written, PendingCount);
    }

    // The pointer for a version SuperJournal just saved. Its write isn't cancelled with the
    // request; when it fails the pointer waits here.
    private async Task<JournalSave> PointAsync(JournalCaller caller, SuperJournalEntry entry, JournalSubject subject)
    {
        var pointer = new JournalPointer(
            entry.JournalId, entry.Id, entry.Version, subject.Type, subject.Key,
            JournalPointers.Excerpt(entry.ContentText), entry.UpdatedAt, caller.Source);
        try
        {
            var write = await pointers.AppendAsync(pointer, clock.UtcNow, CancellationToken.None);
            return new JournalSave(entry, write.EventId, PointerPending: false);
        }
        catch (Exception e)
        {
            pending[(entry.Id, entry.Version)] = pointer;
            warn?.Invoke(
                $"SuperJournal saved entry {entry.Id} version {entry.Version}, but its pointer wasn't written; it will be retried.", e);
            return new JournalSave(entry, null, PointerPending: true);
        }
    }

    // After a save: the journal's newest entries, for versions with no pointer. Never fails the save.
    private async Task CheckJournalAsync(
        ISuperJournal sj, JournalCaller caller, string journalId, JournalSubject subject, CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileJournalAsync(sj, caller, journalId, subject, SaveCheckSize, cancellationToken);
        }
        catch (Exception e)
        {
            warn?.Invoke($"Couldn't check journal {journalId} for missing pointers after a save.", e);
        }
    }

    private async Task<int> ReconcileJournalAsync(
        ISuperJournal sj, JournalCaller caller, string journalId, JournalSubject subject, int maxEntries,
        CancellationToken cancellationToken)
    {
        var settled = clock.UtcNow - SettleWindow;
        var size = Math.Min(MaxPageSize, maxEntries);
        var written = 0;
        for (var page = 1; ; page++)
        {
            var entries = await sj.GetEntriesAsync(caller, journalId, page, size, cancellationToken);
            if (entries.Items.Count == 0)
            {
                break;
            }

            var have = await pointers.GetVersionsAsync(entries.Items.Select(e => e.Id).ToList(), cancellationToken);
            foreach (var entry in entries.Items)
            {
                written += await ReconcileEntryAsync(
                    sj, caller, entry, subject, have.GetValueOrDefault(entry.Id), settled, cancellationToken);
            }

            if (entries.Items.Count < size || (long)page * size >= Math.Min(entries.TotalCount, maxEntries))
            {
                break;
            }
        }

        return written;
    }

    private async Task<int> ReconcileEntryAsync(
        ISuperJournal sj, JournalCaller caller, SuperJournalEntry entry, JournalSubject subject,
        IReadOnlySet<int>? have, DateTimeOffset settled, CancellationToken cancellationToken)
    {
        var missing = Enumerable.Range(1, Math.Max(entry.Version, 0))
            .Where(v => have?.Contains(v) != true && !pending.ContainsKey((entry.Id, v)))
            .ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        // Each version's text and when it was saved: the current one from the entry, older ones
        // from its history (version v was saved when v - 1 was replaced, or when the entry was made).
        var versions = new Dictionary<int, (string Text, JsonElement Content, DateTimeOffset SavedAt)>
        {
            [entry.Version] = (entry.ContentText, entry.Content, entry.UpdatedAt),
        };
        if (missing.Any(v => v < entry.Version))
        {
            var history = (await sj.GetHistoryAsync(caller, entry.Id, cancellationToken))
                .GroupBy(r => r.SupersededVersion)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var (version, revision) in history)
            {
                DateTimeOffset? savedAt = version == 1
                    ? entry.CreatedAt
                    : history.TryGetValue(version - 1, out var before) ? before.EditedAt : null;
                if (savedAt is { } at && version < entry.Version)
                {
                    versions[version] = (revision.PriorContentText, revision.PriorContent, at);
                }
            }
        }

        var written = 0;
        foreach (var version in missing)
        {
            if (!versions.TryGetValue(version, out var saved)
                || saved.SavedAt > settled
                || (version == 1 && IsEmpty(saved.Text, saved.Content)))
            {
                continue;
            }

            var pointer = new JournalPointer(
                entry.JournalId, entry.Id, version, subject.Type, subject.Key,
                JournalPointers.Excerpt(saved.Text), saved.SavedAt, caller.Source, Reconciled: true);
            var write = await pointers.AppendAsync(pointer, clock.UtcNow, CancellationToken.None);
            written += write.Written ? 1 : 0;
        }

        return written;
    }

    private async Task<JournalSubject> SubjectOfAsync(
        ISuperJournal sj, JournalCaller caller, string journalId, CancellationToken cancellationToken)
    {
        if (subjects.TryGetValue(journalId, out var known))
        {
            return known;
        }

        var found = await sj.GetJournalAsync(caller, journalId, cancellationToken)
                    ?? throw new SuperJournalException(SuperJournalFailure.NotFound, $"SuperJournal has no journal {journalId}.");
        Remember(found);
        return new JournalSubject(found.SubjectType, found.SubjectKey);
    }

    // A journal's subject never changes, so it's kept for the pointers of later saves.
    private SuperJournalJournal Remember(SuperJournalJournal found)
    {
        subjects[found.Id] = new JournalSubject(found.SubjectType, found.SubjectKey);
        return found;
    }

    private ISuperJournal Require() => journal ?? throw SuperJournalException.NotSetUp();

    private static void CheckText(JournalText text)
    {
        if (text.Content.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("content must be a TipTap/ProseMirror document (a JSON object).");
        }

        if (text.ContentText is null)
        {
            throw Invalid("contentText is required: the entry's plain text (it may be empty).");
        }
    }

    private static IReadOnlyList<string>? CleanTags(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return null;
        }

        var clean = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (clean.Any(t => t.Length > 100))
        {
            throw Invalid("A tag can be at most 100 characters.");
        }

        return clean.Count == 0 ? null : clean;
    }

    // No text, and no node that holds something (a picture, say).
    private static bool IsEmpty(string? text, JsonElement content)
        => string.IsNullOrWhiteSpace(text) && !HoldsSomething(content);

    private static bool HoldsSomething(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (node.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && !EmptyNodes.Contains(type.GetString()!))
        {
            return true;
        }

        return node.TryGetProperty("content", out var children)
               && children.ValueKind == JsonValueKind.Array
               && children.EnumerateArray().Any(HoldsSomething);
    }

    private static string Required(string? value, string name, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw Invalid($"{name} is required.");
        }

        return trimmed.Length <= max ? trimmed : throw Invalid($"{name} can be at most {max} characters.");
    }

    // SuperJournal's Idempotency-Key: 1 to 255 characters, or none.
    private static string? Key(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return key.Length <= MaxIdempotencyKeyLength
            ? key
            : throw Invalid($"An Idempotency-Key can be at most {MaxIdempotencyKeyLength} characters.");
    }

    private static SuperJournalException Invalid(string message) => new(SuperJournalFailure.Invalid, message);

    private readonly record struct JournalSubject(string Type, string Key);

    /// <summary>A read-only stream that stops with <see cref="SuperJournalFailure.TooLarge"/> once more than the limit has been read.</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int n)
        {
            read += n;
            return read <= limit
                ? n
                : throw new SuperJournalException(
                    SuperJournalFailure.TooLarge, $"The file is over SuperJournal's limit of {limit / (1024 * 1024)} MB.");
        }
    }
}

/// <summary>
/// A save: the entry at its new version, and its pointer event, or null with
/// <paramref name="PointerPending"/> when the pointer's write didn't land and waits to be retried.
/// </summary>
public sealed record JournalSave(SuperJournalEntry Entry, Guid? PointerEventId, bool PointerPending);

/// <summary>What a reconcile did: how many journals it checked, how many pointers it wrote, and how many still wait.</summary>
public sealed record JournalReconcile(int Journals, int Written, int StillPending);

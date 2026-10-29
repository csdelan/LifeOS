using System.Text.Json;
using Dapper;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Journals;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The text in SuperJournal, a pointer in <c>bsk</c> for every saved version (D19,
/// csdelan/LifeOS#28): SuperJournal first and then the pointer, one pointer per version whichever
/// door writes it, a pointer that couldn't be written kept and written later with who saved it,
/// and the reconcile step for versions that have none. SuperJournal is played by
/// <see cref="InMemorySuperJournal"/>; the pointers are real events in the test database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SuperJournalServiceTests(PostgresFixture postgres)
{
    private const string ClientId = "client-7";
    private static readonly JournalCaller Web = new("user-1", KernelSources.Api);
    private static readonly JournalCaller Agent = new("user-1", KernelSources.Agent(ClientId));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JournalText Text(string text) => InMemorySuperJournal.Text(text);

    [Fact]
    public async Task Each_saved_version_gets_one_pointer_saying_where_its_text_is_and_who_saved_it()
    {
        var (service, _, _) = Setup();
        var journal = await service.OpenJournalAsync(Web, "daily", "2026-10-28", "Wednesday", Ct);

        var first = await service.AddEntryAsync(Web, journal.Id, Text("  Up early.\n\nWalked   the ridge. "), ["morning"], cancellationToken: Ct);
        var second = await service.EditEntryAsync(Agent, first.Entry.Id, Text("Up early. Walked the ridge twice."), 1, Ct);

        Assert.False(first.PointerPending);
        Assert.Equal(2, second.Entry.Version);
        var rows = await PointersAsync(first.Entry.Id);
        Assert.Equal([first.PointerEventId!.Value, second.PointerEventId!.Value], rows.Select(r => r.Id).ToArray());
        Assert.Equal(["api", "mcp:client-7"], rows.Select(r => r.Source).ToArray());
        Assert.Equal([$"{first.Entry.Id}:1", $"{first.Entry.Id}:2"], rows.Select(r => r.ExternalId).ToArray());
        Assert.All(rows, r => Assert.Equal("declared", r.Provenance));
        Assert.Equal(first.Entry.UpdatedAt.UtcDateTime, rows[0].OccurredAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(second.Entry.UpdatedAt.UtcDateTime, rows[1].OccurredAt, TimeSpan.FromMilliseconds(1));

        using var payload = JsonDocument.Parse(rows[0].Payload);
        var pointer = payload.RootElement.GetProperty("superjournal");
        Assert.Equal(journal.Id, pointer.GetProperty("journal_id").GetString());
        Assert.Equal(first.Entry.Id, pointer.GetProperty("entry_id").GetString());
        Assert.Equal(1, pointer.GetProperty("version").GetInt32());
        Assert.Equal("daily", pointer.GetProperty("subject_type").GetString());
        Assert.Equal("2026-10-28", pointer.GetProperty("subject_key").GetString());
        Assert.Equal("Up early. Walked the ridge.", pointer.GetProperty("excerpt").GetString());
        Assert.False(pointer.TryGetProperty("reconciled", out _));
    }

    [Fact]
    public async Task Writing_a_versions_pointer_again_from_any_door_keeps_the_one_it_has()
    {
        var store = new NpgsqlJournalPointerStore(postgres.ConnectionString);
        var entryId = Guid.NewGuid().ToString("n");
        var pointer = new JournalPointer("j1", entryId, 1, "daily", "2026-10-28", "Up early.", DateTimeOffset.UtcNow, "api");

        var first = await store.AppendAsync(pointer, DateTimeOffset.UtcNow, Ct);
        var again = await store.AppendAsync(pointer, DateTimeOffset.UtcNow, Ct);
        var elsewhere = await store.AppendAsync(pointer with { Source = "mcp:client-7", Reconciled = true }, DateTimeOffset.UtcNow, Ct);

        Assert.True(first.Written);
        Assert.False(again.Written);
        Assert.False(elsewhere.Written);
        Assert.Equal(first.EventId, again.EventId);
        Assert.Equal(first.EventId, elsewhere.EventId);
        Assert.Equal("api", Assert.Single(await PointersAsync(entryId)).Source);
        var versions = await store.GetVersionsAsync([entryId, "no-such-entry"], Ct);
        Assert.Equal([1], versions[entryId].Order().ToArray());
        Assert.False(versions.ContainsKey("no-such-entry"));
    }

    [Fact]
    public async Task A_pointer_that_cannot_be_written_waits_and_the_next_save_writes_it_as_who_saved_it()
    {
        var store = new FlakyPointerStore(new NpgsqlJournalPointerStore(postgres.ConnectionString));
        var warnings = new List<string>();
        var (service, _, _) = Setup(store, warnings);
        var journal = await service.OpenJournalAsync(Web, "daily", "2026-10-28", null, Ct);

        store.Failing = true;
        var lost = await service.AddEntryAsync(Web, journal.Id, Text("First."), null, cancellationToken: Ct);

        Assert.True(lost.PointerPending);
        Assert.Null(lost.PointerEventId);
        Assert.Equal(1, service.PendingCount);
        Assert.Contains(warnings, w => w.Contains(lost.Entry.Id, StringComparison.Ordinal));
        Assert.Empty(await PointersAsync(lost.Entry.Id));

        store.Failing = false;
        var next = await service.AddEntryAsync(Agent, journal.Id, Text("Second."), null, cancellationToken: Ct);

        Assert.Equal(0, service.PendingCount);
        var kept = Assert.Single(await PointersAsync(lost.Entry.Id));
        Assert.Equal("api", kept.Source);
        Assert.DoesNotContain("reconciled", kept.Payload, StringComparison.Ordinal);
        Assert.Equal("mcp:client-7", Assert.Single(await PointersAsync(next.Entry.Id)).Source);
    }

    [Fact]
    public async Task The_retry_writes_what_waits_and_stops_while_the_database_still_fails()
    {
        var store = new FlakyPointerStore(new NpgsqlJournalPointerStore(postgres.ConnectionString));
        var (service, _, _) = Setup(store);
        var journal = await service.OpenJournalAsync(Web, "daily", "2026-10-28", null, Ct);
        store.Failing = true;
        var one = await service.AddEntryAsync(Web, journal.Id, Text("One."), null, cancellationToken: Ct);
        var two = await service.AddEntryAsync(Web, journal.Id, Text("Two."), null, cancellationToken: Ct);

        Assert.Equal(0, await service.RetryPendingAsync(Ct));
        Assert.Equal(2, service.PendingCount);

        store.Failing = false;
        Assert.Equal(2, await service.RetryPendingAsync(Ct));
        Assert.Equal(0, service.PendingCount);
        Assert.Single(await PointersAsync(one.Entry.Id));
        Assert.Single(await PointersAsync(two.Entry.Id));
    }

    [Fact]
    public async Task A_reconcile_points_at_the_versions_superjournal_has_and_bsk_lost_marked_as_reconciled()
    {
        var (service, fake, clock) = Setup();
        var journal = await service.OpenJournalAsync(Web, "weekly", "2026-W44", null, Ct);

        // SuperJournal saved these half an hour ago, and the server stopped before their pointers.
        fake.Now = clock.UtcNow.AddMinutes(-30);
        var crashed = await fake.AppendEntryAsync(Web, journal.Id, Text("First go."), null, cancellationToken: Ct);
        await fake.EditEntryAsync(Web, crashed.Id, Text("Second go."), 1, Ct);
        var current = await fake.EditEntryAsync(Web, crashed.Id, Text("Third go."), 2, Ct);
        var draft = await fake.CreateDraftAsync(Web, journal.Id, cancellationToken: Ct);
        await fake.EditEntryAsync(Web, draft.Id, Text("Filled in later."), 1, Ct);
        var empty = await fake.CreateDraftAsync(Web, journal.Id, cancellationToken: Ct);
        // Saved just now: its own save may still be writing its pointer.
        fake.Now = clock.UtcNow;
        var settling = await fake.AppendEntryAsync(Web, journal.Id, Text("Just now."), null, cancellationToken: Ct);

        var result = await service.ReconcileAsync(Agent, cancellationToken: Ct);

        Assert.Equal(new JournalReconcile(1, 4, 0), result);
        var rows = await PointersAsync(crashed.Id);
        Assert.Equal(["First go.", "Second go.", "Third go."], rows.Select(r => Excerpt(r.Payload)).ToArray());
        Assert.All(rows, r => Assert.Equal("mcp:client-7", r.Source));
        Assert.All(rows, r => Assert.Contains("\"reconciled\":true", r.Payload.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal));
        var history = await fake.GetHistoryAsync(Web, crashed.Id, Ct);
        Assert.Equal(crashed.CreatedAt.UtcDateTime, rows[0].OccurredAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(history[0].EditedAt.UtcDateTime, rows[1].OccurredAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(current.UpdatedAt.UtcDateTime, rows[2].OccurredAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, Assert.Single(await PointersAsync(draft.Id)).Version);
        Assert.Empty(await PointersAsync(empty.Id));
        Assert.Empty(await PointersAsync(settling.Id));

        Assert.Equal(0, (await service.ReconcileAsync(Agent, journal.Id, Ct)).Written);
        clock.Advance(SuperJournalService.SettleWindow + TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await service.ReconcileAsync(Web, journal.Id, Ct)).Written);
        Assert.Equal("api", Assert.Single(await PointersAsync(settling.Id)).Source);
    }

    [Fact]
    public async Task A_save_retried_with_its_idempotency_key_is_the_same_entry_with_the_same_pointer()
    {
        var (service, fake, _) = Setup();
        var journal = await service.OpenJournalAsync(Web, "daily", "2026-10-28", null, Ct);

        var first = await service.AddEntryAsync(Web, journal.Id, Text("Once."), null, "save-1", Ct);
        var retry = await service.AddEntryAsync(Web, journal.Id, Text("Once."), null, "save-1", Ct);
        var reused = await Assert.ThrowsAsync<SuperJournalException>(
            () => service.AddEntryAsync(Web, journal.Id, Text("Something else."), null, "save-1", Ct));
        var tooLong = await Assert.ThrowsAsync<SuperJournalException>(
            () => service.AddEntryAsync(Web, journal.Id, Text("x"), null, new string('k', 256), Ct));

        Assert.Equal(first.Entry.Id, retry.Entry.Id);
        Assert.Equal(first.PointerEventId, retry.PointerEventId);
        Assert.Equal(SuperJournalFailure.Conflict, reused.Failure);
        Assert.Equal(SuperJournalFailure.Invalid, tooLong.Failure);
        Assert.Single(await PointersAsync(first.Entry.Id));
        Assert.Equal(1, (await fake.GetEntriesAsync(Web, journal.Id, 1, 10, Ct)).TotalCount);
    }

    [Fact]
    public async Task When_superjournal_doesnt_save_nothing_is_pointed_at()
    {
        var (service, fake, _) = Setup();
        var journal = await service.OpenJournalAsync(Web, "daily", "2026-10-28", null, Ct);
        var entry = await service.AddEntryAsync(Web, journal.Id, Text("Kept."), null, cancellationToken: Ct);

        var conflict = await Assert.ThrowsAsync<SuperJournalException>(
            () => service.EditEntryAsync(Web, entry.Entry.Id, Text("From an old copy."), expectedVersion: 2, Ct));
        fake.Failure = new SuperJournalException(SuperJournalFailure.Unavailable, "SuperJournal couldn't be reached; try again later.");
        var down = await Assert.ThrowsAsync<SuperJournalException>(
            () => service.AddEntryAsync(Web, journal.Id, Text("Lost?"), null, cancellationToken: Ct));

        Assert.Equal(SuperJournalFailure.Conflict, conflict.Failure);
        Assert.Equal(SuperJournalFailure.Unavailable, down.Failure);
        Assert.Equal(1, await CountForJournalAsync(journal.Id));
        Assert.Equal(0, service.PendingCount);
    }

    [Fact]
    public async Task An_entry_needs_text_or_something_in_it_and_a_draft_has_no_pointer_until_its_first_edit()
    {
        var (service, _, _) = Setup();
        var journal = await service.OpenJournalAsync(Web, "daily", "2026-10-28", null, Ct);
        var picture = new JournalText(JsonSerializer.SerializeToElement(new
        {
            type = "doc",
            content = new object[] { new { type = "image", attrs = new { src = "attachment:a1" } } },
        }), "");

        var blank = await Assert.ThrowsAsync<SuperJournalException>(
            () => service.AddEntryAsync(Web, journal.Id, Text("   "), null, cancellationToken: Ct));
        var noText = await Assert.ThrowsAsync<SuperJournalException>(
            () => service.AddEntryAsync(Web, journal.Id, new JournalText(Text("x").Content, null!), null, cancellationToken: Ct));
        var pictureOnly = await service.AddEntryAsync(Web, journal.Id, picture, null, cancellationToken: Ct);
        var draft = await service.CreateDraftAsync(Web, journal.Id, cancellationToken: Ct);

        Assert.Equal(SuperJournalFailure.Invalid, blank.Failure);
        Assert.Equal(SuperJournalFailure.Invalid, noText.Failure);
        Assert.Equal("", Excerpt(Assert.Single(await PointersAsync(pictureOnly.Entry.Id)).Payload));
        Assert.Equal(1, draft.Version);
        Assert.Empty(await PointersAsync(draft.Id));

        var filled = await service.EditEntryAsync(Web, draft.Id, Text("Now with words."), 1, Ct);
        Assert.Equal(2, Assert.Single(await PointersAsync(draft.Id)).Version);
        Assert.False(filled.PointerPending);
    }

    [Fact]
    public async Task An_upload_is_refused_before_sending_when_superjournal_wont_take_it_and_stopped_past_the_limit()
    {
        var (service, fake, _) = Setup();
        var journal = await service.OpenJournalAsync(Web, "daily", "2026-10-28", null, Ct);
        var entry = (await service.AddEntryAsync(Web, journal.Id, Text("With a file."), null, cancellationToken: Ct)).Entry;
        var calls = fake.Callers.Count;

        var zip = await Assert.ThrowsAsync<SuperJournalException>(
            () => service.UploadAttachmentAsync(Web, entry.Id, new MemoryStream([1, 2, 3]), "notes.zip", "application/zip", cancellationToken: Ct));
        Assert.Equal(SuperJournalFailure.Invalid, zip.Failure);
        Assert.Equal(calls, fake.Callers.Count);

        var tooBig = await Assert.ThrowsAsync<SuperJournalException>(() => service.UploadAttachmentAsync(
            Web, entry.Id, new ZeroStream(SuperJournalService.MaxAttachmentBytes + 1), "big.png", "image/png", cancellationToken: Ct));
        Assert.Equal(SuperJournalFailure.TooLarge, tooBig.Failure);
        Assert.Empty(fake.UploadSizes);

        var kept = await service.UploadAttachmentAsync(
            Web, entry.Id, new ZeroStream(SuperJournalService.MaxAttachmentBytes), "../ridge.PNG", "IMAGE/PNG; charset=binary", cancellationToken: Ct);
        Assert.Equal("ridge.PNG", kept.FileName);
        Assert.Equal("image/png", kept.ContentType);
        Assert.Equal([SuperJournalService.MaxAttachmentBytes], fake.UploadSizes);
        Assert.Equal(1, Assert.Single(await PointersAsync(entry.Id)).Version);
    }

    [Fact]
    public async Task Without_superjournal_every_call_says_it_isnt_set_up()
    {
        var service = new SuperJournalService(
            null, new NpgsqlJournalPointerStore(postgres.ConnectionString), new TimerTestClock(DateTimeOffset.UtcNow));

        var error = await Assert.ThrowsAsync<SuperJournalException>(
            () => service.OpenJournalAsync(Web, "daily", "2026-10-28", null, Ct));

        Assert.False(service.IsConfigured);
        Assert.Equal(SuperJournalFailure.NotSetUp, error.Failure);
        Assert.Equal(SuperJournalException.NotSetUpMessage, error.Message);
    }

    [Fact]
    public async Task Recent_changes_shows_an_entry_once_as_its_newest_version_with_its_excerpt_and_who_saved_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        // Every reading of the clock is a second later, so a reconciled version is recorded after newer ones.
        var clock = new TickingClock(DateTimeOffset.UtcNow);
        var fake = new InMemorySuperJournal { Now = clock.Start.AddMinutes(-30) };
        var service = new SuperJournalService(fake, new NpgsqlJournalPointerStore(db.ConnectionString), clock);
        var journal = await service.OpenJournalAsync(Web, "daily", "2026-10-28", null, Ct);

        var first = await service.AddEntryAsync(Web, journal.Id, Text("One."), null, cancellationToken: Ct);
        await fake.EditEntryAsync(Web, first.Entry.Id, Text("Two."), 1, Ct);
        var third = await service.EditEntryAsync(Agent, first.Entry.Id, Text("Three."), 2, Ct);

        Assert.Equal(3, (await PointersAsync(first.Entry.Id, db.ConnectionString)).Count);
        var changes = new ChangeReader(BskReader.ConnectionStringFrom(db.ConnectionString));
        foreach (var byOccurred in new[] { false, true })
        {
            var row = Assert.Single(changes.GetChanges(
                clock.Start.AddHours(-1), [EventKinds.Journal], new HashSet<string>(), 50, byOccurred));
            Assert.Equal(third.PointerEventId, row.Id);
            Assert.Equal("Three.", row.Text);
            Assert.Equal("mcp:client-7", row.Source);
            Assert.Equal(3, row.Versions);
            using var payload = JsonDocument.Parse(row.Payload!);
            var pointer = payload.RootElement.GetProperty("superjournal");
            Assert.Equal(3, pointer.GetProperty("version").GetInt32());
            Assert.False(pointer.TryGetProperty("excerpt", out _));
        }
    }

    private (SuperJournalService Service, InMemorySuperJournal Fake, TimerTestClock Clock) Setup(
        IJournalPointerStore? store = null, List<string>? warnings = null)
    {
        var clock = new TimerTestClock(DateTimeOffset.UtcNow);
        var fake = new InMemorySuperJournal { Now = clock.UtcNow };
        var service = new SuperJournalService(
            fake, store ?? new NpgsqlJournalPointerStore(postgres.ConnectionString), clock,
            (message, _) => warnings?.Add(message));
        return (service, fake, clock);
    }

    private async Task<List<PointerRow>> PointersAsync(string entryId, string? connectionString = null)
    {
        await using var connection = new NpgsqlConnection(connectionString ?? postgres.ConnectionString);
        var rows = await connection.QueryAsync<PointerRow>(new CommandDefinition(
            """
            SELECT id AS Id, source_id AS Source, external_id AS ExternalId, provenance AS Provenance,
                   occurred_at AS OccurredAt, payload::text AS Payload,
                   (payload->'superjournal'->>'version')::int AS Version
            FROM bsk.event
            WHERE kind = 'journal' AND payload->'superjournal'->>'entry_id' = @entryId
            ORDER BY (payload->'superjournal'->>'version')::int
            """,
            new { entryId }, cancellationToken: Ct));
        return rows.AsList();
    }

    private async Task<long> CountForJournalAsync(string journalId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'journal' AND payload->'superjournal'->>'journal_id' = @journalId",
            new { journalId }, cancellationToken: Ct));
    }

    private static string Excerpt(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("superjournal").GetProperty("excerpt").GetString()!;
    }

    private static int Version(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("superjournal").GetProperty("version").GetInt32();
    }

    private sealed class PointerRow
    {
        public Guid Id { get; set; }
        public string Source { get; set; } = "";
        public string ExternalId { get; set; } = "";
        public string Provenance { get; set; } = "";
        public DateTime OccurredAt { get; set; }
        public string Payload { get; set; } = "";
        public int Version { get; set; }
    }

    /// <summary>The pointer store, with a switch that makes its writes fail as a restarting database would.</summary>
    private sealed class FlakyPointerStore(IJournalPointerStore inner) : IJournalPointerStore
    {
        public bool Failing { get; set; }

        public Task<JournalPointerWrite> AppendAsync(
            JournalPointer pointer, DateTimeOffset recordedAt, CancellationToken cancellationToken = default)
            => Failing
                ? Task.FromException<JournalPointerWrite>(new TimeoutException("The database didn't answer."))
                : inner.AppendAsync(pointer, recordedAt, cancellationToken);

        public Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> GetVersionsAsync(
            IReadOnlyCollection<string> entryIds, CancellationToken cancellationToken = default)
            => inner.GetVersionsAsync(entryIds, cancellationToken);
    }

    /// <summary>A clock a second later every time it's read.</summary>
    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private int reads;

        public DateTimeOffset Start { get; } = start;

        public DateTimeOffset UtcNow => Start.AddSeconds(Interlocked.Increment(ref reads));
    }

    /// <summary><paramref name="length"/> zero bytes, read forward once, without holding them.</summary>
    private sealed class ZeroStream(long length) : Stream
    {
        private long position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - position);
            Array.Clear(buffer, offset, n);
            position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>The excerpt a pointer keeps of a version's text (D19).</summary>
public sealed class JournalPointerExcerptTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("   \n\t ", "")]
    [InlineData("  Up early.\n\n Walked\tthe   ridge.  ", "Up early. Walked the ridge.")]
    public void Whitespace_runs_become_one_space(string? text, string excerpt)
        => Assert.Equal(excerpt, JournalPointers.Excerpt(text));

    [Fact]
    public void Text_up_to_the_limit_is_kept_whole()
    {
        var text = new string('a', JournalPointers.ExcerptLength);
        Assert.Equal(text, JournalPointers.Excerpt(text));
    }

    [Fact]
    public void Longer_text_is_cut_at_a_word_with_an_ellipsis_inside_the_limit()
    {
        var text = string.Concat(Enumerable.Repeat("ridge ", 200));

        var excerpt = JournalPointers.Excerpt(text);

        Assert.True(excerpt.Length <= JournalPointers.ExcerptLength, $"{excerpt.Length} characters");
        Assert.EndsWith("ridge…", excerpt, StringComparison.Ordinal);
        Assert.StartsWith("ridge ridge", excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_word_is_cut_where_it_must_be_and_never_through_a_surrogate_pair()
    {
        var word = new string('a', 600);
        var emoji = new string('a', 498) + "😀" + new string('b', 20);

        Assert.Equal(new string('a', 499) + "…", JournalPointers.Excerpt(word));
        Assert.Equal(new string('a', 498) + "…", JournalPointers.Excerpt(emoji));
    }

    [Fact]
    public void The_external_id_is_the_entry_and_its_version()
        => Assert.Equal("4f2a:3", JournalPointers.ExternalId("4f2a", 3));
}

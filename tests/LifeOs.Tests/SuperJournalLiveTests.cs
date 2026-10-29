using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using LifeOs.Api.Auth;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Journals;
using LifeOs.Domain;
using LifeOs.Infrastructure.Integrations;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Opt-in: LifeOS's SuperJournal adapter, token and reconcile against a real SuperJournal
/// (D19, csdelan/LifeOS#28). Skipped unless both are set:
/// <list type="bullet">
/// <item><c>LIFEOS_SUPERJOURNAL_LIVE_URL</c> — its base URL, e.g. <c>http://127.0.0.1:5297/</c>
/// for a local one (<c>dotnet run --project src/JournalService.Api</c>).</item>
/// <item><c>LIFEOS_SUPERJOURNAL_LIVE_KEY_FILE</c> — a PEM file with the private key whose public
/// half that SuperJournal trusts for the <c>lifeos</c> issuer.</item>
/// </list>
/// Each run writes as a new made-up person, so runs never meet; nothing here deletes.
/// </summary>
public sealed class SuperJournalLiveTests
{
    // A 1×1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_journal_round_trip_through_a_real_superjournal()
    {
        var (journals, signingKey) = Connect();
        using var _ = signingKey;
        var run = Guid.NewGuid().ToString("n");
        var chris = new JournalCaller($"lifeos-live-{run}", KernelSources.Api);
        var someoneElse = new JournalCaller($"lifeos-live-other-{run}", KernelSources.Api);

        var journal = await journals.OpenJournalAsync(chris, "live-test", run, "Live test", Ct);
        Assert.Equal(journal.Id, (await journals.OpenJournalAsync(chris, "live-test", run, null, Ct)).Id);
        Assert.Equal(journal.Id, (await journals.FindJournalAsync(chris, "live-test", run, Ct))!.Id);
        Assert.Null(await journals.FindJournalAsync(chris, "live-test", "no-such-" + run, Ct));
        Assert.Equal("lifeos", journal.App);
        Assert.Contains((await journals.ListJournalsAsync(chris, 1, 100, Ct)).Items, j => j.Id == journal.Id);

        var word = "ridge" + run[..8];
        var added = await journals.AppendEntryAsync(chris, journal.Id, InMemorySuperJournal.Text($"Walked the {word} at dawn."), ["live"], cancellationToken: Ct);
        Assert.Equal(1, added.Version);
        var read = await journals.GetEntryAsync(chris, added.Id, Ct);
        Assert.Equal($"Walked the {word} at dawn.", read!.ContentText);
        Assert.Equal("doc", read.Content.GetProperty("type").GetString());
        Assert.Equal(["live"], read.Tags);

        var edited = await journals.EditEntryAsync(chris, added.Id, InMemorySuperJournal.Text($"Walked the {word} twice."), 1, Ct);
        Assert.Equal(2, edited.Version);
        var stale = await Assert.ThrowsAsync<SuperJournalException>(
            () => journals.EditEntryAsync(chris, added.Id, InMemorySuperJournal.Text("From an old copy."), 1, Ct));
        Assert.Equal(SuperJournalFailure.Conflict, stale.Failure);
        var history = await journals.GetHistoryAsync(chris, added.Id, Ct);
        var replaced = Assert.Single(history);
        Assert.Equal(1, replaced.SupersededVersion);
        Assert.Equal($"Walked the {word} at dawn.", replaced.PriorContentText);

        var draft = await journals.CreateDraftAsync(chris, journal.Id, cancellationToken: Ct);
        Assert.Equal(1, draft.Version);
        Assert.Equal("", draft.ContentText);
        var entries = await journals.GetEntriesAsync(chris, journal.Id, 1, 10, Ct);
        Assert.Equal(draft.Id, entries.Items[0].Id);
        Assert.Contains(entries.Items, e => e.Id == added.Id && e.Version == 2);

        // Uploads go chunked from a stream that can't seek, as from the browser.
        var picture = await journals.UploadAttachmentAsync(chris, draft.Id, new ForwardOnly(Png), "dot.png", "image/png", cancellationToken: Ct);
        Assert.Equal(Png.Length, picture.SizeBytes);
        Assert.True(picture.IsImage);
        var csv = "date,steps\n2026-10-28,9000\n"u8.ToArray();
        var table = await journals.UploadAttachmentAsync(chris, draft.Id, new ForwardOnly(csv), "steps.csv", "text/csv", cancellationToken: Ct);
        await using (var download = await journals.DownloadAttachmentAsync(chris, table.Id, thumbnail: false, Ct))
        {
            Assert.NotNull(download);
            Assert.StartsWith("text/csv", download.ContentType, StringComparison.Ordinal);
            using var copy = new MemoryStream();
            await download.Content.CopyToAsync(copy, Ct);
            Assert.Equal(csv, copy.ToArray());
        }

        Assert.Equal(1, (await journals.GetEntryAsync(chris, draft.Id, Ct))!.Version);
        Assert.Contains((await journals.SearchAsync(chris, word, "lifeos", 1, 10, Ct)).Items, e => e.Id == added.Id);

        // Another person can't read or write Chris's journal.
        var notTheirs = await Record.ExceptionAsync(() => journals.GetEntriesAsync(someoneElse, journal.Id, 1, 10, Ct));
        Assert.True(notTheirs is SuperJournalException { Failure: SuperJournalFailure.Forbidden or SuperJournalFailure.NotFound },
            notTheirs?.ToString() ?? "no failure");
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var (_, signingKey) = Connect();
        signingKey.Dispose();
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = SuperJournalOptions.Resolve(Setting)!;
        var journals = new SuperJournalHttpClient(
            SuperJournalHttpClient.CreateHttpClient(), options, new SuperJournalTokenSigner(stranger, new TimerTestClock(DateTimeOffset.UtcNow)));

        var error = await Assert.ThrowsAsync<SuperJournalException>(
            () => journals.ListJournalsAsync(new JournalCaller("lifeos-live-stranger", "api"), 1, 10, Ct));

        Assert.Equal(SuperJournalFailure.Refused, error.Failure);
    }

    [Fact]
    public async Task The_reconcile_reads_a_real_entrys_history_into_one_pointer_per_version()
    {
        var (journals, signingKey) = Connect();
        using var _ = signingKey;
        var pointers = new MemoryPointerStore();
        // Ten minutes on, so every version here is past the settle window.
        var service = new SuperJournalService(journals, pointers, new TimerTestClock(DateTimeOffset.UtcNow.AddMinutes(10)));
        var run = Guid.NewGuid().ToString("n");
        var chris = new JournalCaller($"lifeos-live-{run}", KernelSources.Api);
        var journal = await service.OpenJournalAsync(chris, "live-test", run, null, Ct);

        var saved = await service.AddEntryAsync(chris, journal.Id, InMemorySuperJournal.Text("One."), null, cancellationToken: Ct);
        // Saved straight to SuperJournal, as if the server stopped before the pointers.
        await journals.EditEntryAsync(chris, saved.Entry.Id, InMemorySuperJournal.Text("Two."), 1, Ct);
        var third = await journals.EditEntryAsync(chris, saved.Entry.Id, InMemorySuperJournal.Text("Three."), 2, Ct);
        var agent = chris with { Source = KernelSources.Agent("live-client") };

        var result = await service.ReconcileAsync(agent, journal.Id, Ct);

        Assert.Equal(2, result.Written);
        var kept = pointers.For(saved.Entry.Id);
        Assert.Equal(["One.", "Two.", "Three."], kept.Select(p => p.Excerpt).ToArray());
        Assert.Equal([false, true, true], kept.Select(p => p.Reconciled).ToArray());
        Assert.Equal(["api", "mcp:live-client", "mcp:live-client"], kept.Select(p => p.Source).ToArray());
        Assert.Equal(third.UpdatedAt, kept[2].SavedAt);
        Assert.True(kept[0].SavedAt <= kept[1].SavedAt && kept[1].SavedAt <= kept[2].SavedAt);
        Assert.Equal(0, (await service.ReconcileAsync(agent, journal.Id, Ct)).Written);
    }

    private static string? Setting(string key) => key == SuperJournalOptions.BaseUrlKey
        ? Environment.GetEnvironmentVariable("LIFEOS_SUPERJOURNAL_LIVE_URL")
        : null;

    private static (SuperJournalHttpClient Journals, ECDsa Key) Connect()
    {
        var url = Environment.GetEnvironmentVariable("LIFEOS_SUPERJOURNAL_LIVE_URL");
        var keyFile = Environment.GetEnvironmentVariable("LIFEOS_SUPERJOURNAL_LIVE_KEY_FILE");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(keyFile))
        {
            Assert.Skip("Set LIFEOS_SUPERJOURNAL_LIVE_URL and LIFEOS_SUPERJOURNAL_LIVE_KEY_FILE to run against a real SuperJournal.");
        }

        var key = SuperJournalTokenSigner.ParseKey(File.ReadAllText(keyFile));
        var journals = new SuperJournalHttpClient(
            SuperJournalHttpClient.CreateHttpClient(), SuperJournalOptions.Resolve(Setting)!,
            new SuperJournalTokenSigner(key, new TimerTestClock(DateTimeOffset.UtcNow)));
        return (journals, key);
    }

    private sealed class MemoryPointerStore : IJournalPointerStore
    {
        private readonly ConcurrentDictionary<(string EntryId, int Version), JournalPointer> pointers = new();

        public List<JournalPointer> For(string entryId)
            => pointers.Values.Where(p => p.EntryId == entryId).OrderBy(p => p.Version).ToList();

        public Task<JournalPointerWrite> AppendAsync(
            JournalPointer pointer, DateTimeOffset recordedAt, CancellationToken cancellationToken = default)
            => Task.FromResult(new JournalPointerWrite(Guid.NewGuid(), pointers.TryAdd((pointer.EntryId, pointer.Version), pointer)));

        public Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> GetVersionsAsync(
            IReadOnlyCollection<string> entryIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlySet<int>>>(pointers.Keys
                .Where(k => entryIds.Contains(k.EntryId))
                .GroupBy(k => k.EntryId)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.Select(k => k.Version).ToHashSet()));
    }

    private sealed class ForwardOnly(byte[] bytes) : Stream
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

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// Opt-in, with the same settings as <see cref="SuperJournalLiveTests"/>: the API host as it runs,
/// its <c>SuperJournal__*</c> settings, signer and HTTP client, against a real SuperJournal, with
/// the pointers landing in a test database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SuperJournalLiveApiTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lifeos_saves_through_a_real_superjournal_with_one_pointer_per_version()
    {
        var url = Environment.GetEnvironmentVariable("LIFEOS_SUPERJOURNAL_LIVE_URL");
        var keyFile = Environment.GetEnvironmentVariable("LIFEOS_SUPERJOURNAL_LIVE_KEY_FILE");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(keyFile))
        {
            Assert.Skip("Set LIFEOS_SUPERJOURNAL_LIVE_URL and LIFEOS_SUPERJOURNAL_LIVE_KEY_FILE to run against a real SuperJournal.");
        }

        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, new Dictionary<string, string?>(DomainTestDatabase.ApiZone)
        {
            [SuperJournalOptions.BaseUrlKey] = url,
            [SuperJournalOptions.SigningKeyKey] = File.ReadAllText(keyFile),
        });
        var client = host.Client;
        var run = Guid.NewGuid().ToString("n");
        var doc = new
        {
            type = "doc",
            content = new object[] { new { type = "paragraph", content = new object[] { new { type = "text", text = "Up early." } } } },
        };

        var journal = await Json(await client.PostAsJsonAsync("/api/journals", new { subjectType = "live-test", subjectKey = run }, Ct));
        var id = journal.GetProperty("id").GetString()!;
        var saved = await Json(await client.PostAsJsonAsync($"/api/journals/{id}/entries", new { content = doc, contentText = "Up early." }, Ct));
        var entryId = saved.GetProperty("entry").GetProperty("id").GetString()!;
        var edited = await Json(await client.PatchAsJsonAsync(
            $"/api/journals/entries/{entryId}", new { content = doc, contentText = "Up early, twice.", expectedVersion = 1 }, Ct));
        var stale = await client.PatchAsJsonAsync(
            $"/api/journals/entries/{entryId}", new { content = doc, contentText = "Old copy.", expectedVersion = 1 }, Ct);
        var read = await Json(await client.GetAsync($"/api/journals/entries/{entryId}", Ct));
        var history = await Json(await client.GetAsync($"/api/journals/entries/{entryId}/history", Ct));
        var reconcile = await Json(await client.PostAsync($"/api/journals/reconcile?journal={id}", null, Ct));

        Assert.Equal(2, edited.GetProperty("entry").GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Up early, twice.", read.GetProperty("contentText").GetString());
        Assert.Equal("Up early.", Assert.Single(history.EnumerateArray()).GetProperty("contentText").GetString());
        Assert.Equal(0, reconcile.GetProperty("written").GetInt32());
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var pointers = (await connection.QueryAsync<(string Source, string ExternalId)>(new CommandDefinition(
            "SELECT source_id, external_id FROM bsk.event WHERE kind = 'journal' ORDER BY external_id", cancellationToken: Ct))).ToList();
        Assert.Equal([("api", $"{entryId}:1"), ("api", $"{entryId}:2")], pointers);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}

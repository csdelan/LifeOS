using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Api.Read;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Reads that grow with an item's history take a limit, and cut in SQL (code review CR54): an
/// item's journal and status history over <c>/api</c>, and the Inbox read one item at a time or
/// a few, as the connector's <c>inbox</c> and dismiss read it.
/// </summary>
public sealed class ReadLimitTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ApiTestHost> StartAsync() => ApiTestHost.StartAsync(
        classDb.ConnectionString, Ct,
        new Dictionary<string, string?> { [HomeZone.ConfigurationKey] = PostgresFixture.HabitViewZone });

    private static async Task<JsonElement> SendAsync(HttpClient client, string path, object? body = null)
    {
        var response = body is null ? await client.GetAsync(path, Ct) : await client.PostAsJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{path} → {(int)response.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    [Fact]
    public async Task An_items_journal_and_history_read_only_the_newest_when_asked()
    {
        await using var host = await StartAsync();
        var c = host.Client;
        var id = (await SendAsync(c, "/api/subjects", new { type = SubjectTypes.Task, title = $"Fix the gate {Guid.NewGuid():N}" }))
            .GetProperty("id").GetGuid();
        foreach (var text in new[] { "one", "two", "three" })
        {
            await SendAsync(c, "/api/journal", new { subject = id.ToString(), text });
        }

        foreach (var status in new[] { "In progress", "Completed" })
        {
            await SendAsync(c, "/api/status", new { subject = id.ToString(), status });
        }

        static List<string?> Contents(JsonElement rows) => rows.EnumerateArray().Select(r => r.GetProperty("content").GetString()).ToList();

        // The newest two, still oldest first, as the whole journal reads.
        Assert.Equal(["two", "three"], Contents(await SendAsync(c, $"/api/subjects/{id}/journal?limit=2")));
        Assert.Equal(["one", "two", "three"], Contents(await SendAsync(c, $"/api/subjects/{id}/journal")));

        var newest = Assert.Single((await SendAsync(c, $"/api/subjects/{id}/history?limit=1")).EnumerateArray());
        Assert.Equal("Completed", newest.GetProperty("status").GetString());
        Assert.True((await SendAsync(c, $"/api/subjects/{id}/history")).GetArrayLength() >= 2);

        foreach (var bad in new[] { "0", "1001", "-3" })
        {
            var response = await c.GetAsync($"/api/subjects/{id}/journal?limit={bad}", Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("limit is a whole number from 1 to 1000", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_inbox_reads_one_item_a_few_or_none_of_a_hidden_type()
    {
        await using var host = await StartAsync();
        var c = host.Client;
        var ns = Guid.NewGuid().ToString("N")[..8];
        await SendAsync(c, "/api/capture", new { text = $"First thought {ns}" });
        await SendAsync(c, "/api/capture", new { text = $"Second thought {ns}" });
        var task = (await SendAsync(c, "/api/subjects", new { type = SubjectTypes.Task, title = $"Flagged task {ns}" })).GetProperty("id").GetGuid();
        await SendAsync(c, "/api/flag", new { item = task.ToString() });

        var reader = new SubjectReader(classDb.ConnectionString);
        var all = await reader.GetInboxAsync(cancellationToken: Ct);
        var first = all.Single(i => (i.EventContent ?? "").Contains($"First thought {ns}", StringComparison.Ordinal));

        var one = Assert.Single(await reader.GetInboxAsync(itemId: first.ItemId, cancellationToken: Ct));
        Assert.Equal(first.ItemId, one.ItemId);
        Assert.Empty(await reader.GetInboxAsync(itemId: Guid.NewGuid(), cancellationToken: Ct));

        // Newest first, cut in SQL: the flagged Task, then the second thought.
        var two = await reader.GetInboxAsync(limit: 2, cancellationToken: Ct);
        Assert.Equal([task, all[1].ItemId], two.Select(i => i.ItemId));

        var noTasks = await reader.GetInboxAsync(hiddenTypes: [SubjectTypes.Task], cancellationToken: Ct);
        Assert.DoesNotContain(noTasks, i => i.ItemId == task);
        Assert.Equal(all.Count - 1, noTasks.Count);
    }
}

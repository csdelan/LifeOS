using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR76: <c>/api/subjects</c> narrows by status and cuts to a limit in SQL, the
/// palette searches on the server, and Focus counts the Inbox, so a page that shows open items no
/// longer downloads every repetition ever done and filters it in the browser.
/// </summary>
public sealed class SubjectListReadTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_reads_open_or_closed_items_and_stops_at_its_limit()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var subjects = db.Get<SubjectService>();
        await subjects.CreateAsync(SubjectTypes.Task, "Open one", cancellationToken: Ct);
        var done = await subjects.CreateAsync(SubjectTypes.Task, "Done one", cancellationToken: Ct);
        await db.Get<StatusService>().ChangeStatusAsync(done.Urn, "Completed", Ct);
        var shelved = await subjects.CreateAsync(SubjectTypes.Task, "Shelved one", cancellationToken: Ct);
        await db.Get<ArchiveService>().ArchiveAsync(shelved.Urn, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        Assert.Equal(["Open one"], await TitlesAsync(host, "/api/subjects?type=Task&status=open"));
        Assert.Equal(["Done one"], await TitlesAsync(host, "/api/subjects?type=Task&status=closed"));
        Assert.Equal(["Done one"], await TitlesAsync(host, "/api/subjects?type=Task&status=completed"));
        Assert.Equal(["Open one", "Shelved one"], await TitlesAsync(host, "/api/subjects?type=Task&status=open&includeArchived=true"));
        Assert.Equal(["Done one", "Open one"], await TitlesAsync(host, "/api/subjects?type=Task"));
        Assert.Single(await TitlesAsync(host, "/api/subjects?type=Task&limit=1"));

        var tooMany = await host.Client.GetAsync("/api/subjects?limit=0", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task The_palette_search_matches_every_word_and_ranks_as_the_palette_did()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var subjects = db.Get<SubjectService>();
        var attributes = db.Get<AttributeService>();
        var health = await subjects.CreateAsync(SubjectTypes.Area, "Health", cancellationToken: Ct);
        var sqlite = await subjects.CreateAsync(SubjectTypes.Decision, "Use SQLite for local storage", cancellationToken: Ct);
        await db.Get<TagService>().ApplyAsync(sqlite.Urn, ["lifeos", "storage"], [], Ct);
        await subjects.CreateAsync(SubjectTypes.Task, "Horseshoes", cancellationToken: Ct);
        var race = await subjects.CreateAsync(SubjectTypes.Task, "Buy race shoes", cancellationToken: Ct);
        await attributes.SetAsync(race.Urn, [new AttributeAssignment("area", health.Urn)], Ct);
        await subjects.CreateAsync(SubjectTypes.Task, "Shoes for the race", cancellationToken: Ct);
        await subjects.CreateAsync(SubjectTypes.Task, "Shoes", cancellationToken: Ct);
        var old = await subjects.CreateAsync(SubjectTypes.Task, "Old shoes", cancellationToken: Ct);
        await db.Get<ArchiveService>().ArchiveAsync(old.Urn, Ct);
        await subjects.CreateAsync(SubjectTypes.Value, "Kind", """{"statement": "I am kind."}""", cancellationToken: Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        // The title itself, then starting with it, then a word starting with it, then containing
        // it; the archived one after every live one.
        var shoes = await SearchAsync(host, "q=Shoes");
        Assert.Equal(["Shoes", "Shoes for the race", "Buy race shoes", "Horseshoes", "Old shoes"], shoes.Titles);
        Assert.Equal(5, shoes.Total);
        var best = await SearchAsync(host, "q=shoes&limit=2");
        Assert.Equal(["Shoes", "Shoes for the race"], best.Titles);
        Assert.Equal(5, best.Total);

        // Every word, in any order, in the title, type, label, URN, area or tags.
        Assert.Equal(["Use SQLite for local storage"], (await SearchAsync(host, "q=storage%20sqlite")).Titles);
        Assert.Equal(["Use SQLite for local storage"], (await SearchAsync(host, "q=decision%20lifeos")).Titles);
        Assert.Equal(["Buy race shoes"], (await SearchAsync(host, "q=race%20health")).Titles);
        Assert.Equal(["Use SQLite for local storage"], (await SearchAsync(host, $"q={Uri.EscapeDataString(sqlite.Urn)}")).Titles);
        Assert.Equal(["Kind"], (await SearchAsync(host, "q=identity")).Titles);

        // Narrowed to types; nothing typed finds nothing; a query of the pattern's own characters is text.
        var none = await SearchAsync(host, "q=shoes&types=Decision&types=Goal");
        Assert.Empty(none.Titles);
        Assert.Equal(0, none.Total);
        Assert.Equal(["Use SQLite for local storage"], (await SearchAsync(host, "q=storage&types=Decision&types=Task")).Titles);
        Assert.Empty((await SearchAsync(host, "q=%20%20")).Titles);
        Assert.Empty((await SearchAsync(host, "q=" + Uri.EscapeDataString("(sh[o]es+ 100%_\\"))).Titles);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/subjects/search?q=x&limit=500", Ct)).StatusCode);
    }

    [Fact]
    public async Task Focus_counts_the_inbox_without_reading_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        foreach (var text in new[] { "call the dentist", "a gift for Maya" })
        {
            (await host.Client.PostAsJsonAsync("/api/capture", new { text }, Ct)).EnsureSuccessStatusCode();
        }

        var count = await JsonAsync(await host.Client.GetAsync("/api/inbox/count", Ct));
        var inbox = await JsonAsync(await host.Client.GetAsync("/api/inbox", Ct));

        Assert.Equal(2, count.GetProperty("count").GetInt32());
        Assert.Equal(inbox.GetArrayLength(), count.GetProperty("count").GetInt32());
    }

    private async Task<string[]> TitlesAsync(ApiTestHost host, string url)
        => (await JsonAsync(await host.Client.GetAsync(url, Ct))).EnumerateArray()
            .Select(r => r.GetProperty("title").GetString()!).ToArray();

    private async Task<(string[] Titles, int Total)> SearchAsync(ApiTestHost host, string query)
    {
        var found = await JsonAsync(await host.Client.GetAsync("/api/subjects/search?" + query, Ct));
        return (found.GetProperty("matches").EnumerateArray().Select(r => r.GetProperty("title").GetString()!).ToArray(),
            found.GetProperty("total").GetInt32());
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, text);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}

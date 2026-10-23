using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Cover images over HTTP (COVER-1, D15; cover-images plan §3.4, C2): searching through the
/// server, setting by photo id, moving and clearing, and the cover on the subject reads — with
/// Unsplash played by <see cref="FakeUnsplashClient"/>. Each test has a database of its own.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CoverEndpointTests(PostgresFixture postgres)
{
    private const string Referral = "utm_source=lifeos&utm_medium=referral";

    // Settings win over environment variables, so a key on this machine can't switch covers on.
    private static readonly Dictionary<string, string?> WithoutKey = new(DomainTestDatabase.ApiZone)
    {
        ["Unsplash:AccessKey"] = "",
    };

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<ApiTestHost> StartWithUnsplashAsync(
        DomainTestDatabase db, FakeUnsplashClient unsplash, CancellationToken ct)
        => ApiTestHost.StartAsync(
            db.ConnectionString, ct, WithoutKey, services => services.AddSingleton<IUnsplashClient>(unsplash));

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<Guid> CreateAsync(ApiTestHost host, string type, string title)
    {
        object body = type == "Value"
            ? new { type, title, attrs = new Dictionary<string, string> { ["statement"] = "I am someone who looks up at the hills." } }
            : new { type, title };
        var created = await JsonAsync(await host.Client.PostAsJsonAsync("/api/subjects", body, Ct));
        return created.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ListItemAsync(ApiTestHost host, string type, Guid id)
    {
        var items = await JsonAsync(await host.Client.GetAsync($"/api/subjects?type={type}", Ct));
        return items.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
    }

    private static void AssertCover(JsonElement cover, int positionY)
    {
        Assert.Equal(JsonValueKind.Object, cover.ValueKind);
        Assert.Equal("unsplash", cover.GetProperty("source").GetString());
        Assert.Equal(FakeUnsplashClient.Photo.Id, cover.GetProperty("photoId").GetString());
        Assert.Equal(FakeUnsplashClient.Photo.RawUrl, cover.GetProperty("url").GetString());
        Assert.Equal("#6e633a", cover.GetProperty("color").GetString());
        Assert.Equal("Joe Example", cover.GetProperty("photographer").GetString());
        Assert.Equal(positionY, cover.GetProperty("positionY").GetInt32());
        Assert.Equal($"https://unsplash.com/@exampleuser?{Referral}", cover.GetProperty("photographerLink").GetString());
        Assert.Equal($"https://unsplash.com/photos/Dwu85P9SOIk?{Referral}", cover.GetProperty("photoLink").GetString());
        Assert.Equal($"https://unsplash.com/?{Referral}", cover.GetProperty("unsplashLink").GetString());
    }

    [Fact]
    public async Task Without_a_key_search_says_not_configured_and_setting_is_refused()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, WithoutKey);
        var goal = await CreateAsync(host, "Goal", "Walk the ridge");

        var search = await JsonAsync(await host.Client.GetAsync("/api/covers/search?q=mountain", Ct));
        Assert.False(search.GetProperty("configured").GetBoolean());
        Assert.Empty(search.GetProperty("photos").EnumerateArray());
        Assert.Equal($"https://unsplash.com/?{Referral}", search.GetProperty("unsplashLink").GetString());

        // The words are still checked, so the picker learns about them before a key exists.
        await JsonAsync(await host.Client.GetAsync("/api/covers/search?q=%20%20", Ct), HttpStatusCode.BadRequest);

        var refused = await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover", new { subject = goal.ToString(), photoId = "Dwu85P9SOIk" }, Ct),
            HttpStatusCode.Conflict);
        Assert.Contains("Unsplash__AccessKey", refused.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Search_pages_photos_for_the_picker()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var unsplash = new FakeUnsplashClient();
        await using var host = await StartWithUnsplashAsync(db, unsplash, Ct);

        var search = await JsonAsync(await host.Client.GetAsync("/api/covers/search?q=%20mountain%20%20sunrise&page=2", Ct));
        Assert.True(search.GetProperty("configured").GetBoolean());
        Assert.Equal("mountain sunrise", search.GetProperty("query").GetString());
        Assert.Equal(2, search.GetProperty("page").GetInt32());
        Assert.Equal(("mountain sunrise", 2, 24), Assert.Single(unsplash.Searched));

        var photo = Assert.Single(search.GetProperty("photos").EnumerateArray());
        Assert.Equal("Dwu85P9SOIk", photo.GetProperty("id").GetString());
        Assert.Equal(
            "https://images.unsplash.com/photo-1417325384643-aac51acc9e5d?ixid=M3w2NTQ3&w=400&q=70&auto=format&fit=max",
            photo.GetProperty("thumbUrl").GetString());
        Assert.Equal("#6e633a", photo.GetProperty("color").GetString());
        Assert.Equal("man drinking coffee", photo.GetProperty("alt").GetString());
        Assert.Equal($"https://unsplash.com/@exampleuser?{Referral}", photo.GetProperty("photographerLink").GetString());

        var blank = await JsonAsync(await host.Client.GetAsync("/api/covers/search?q=", Ct), HttpStatusCode.BadRequest);
        Assert.Contains("search for", blank.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        await JsonAsync(
            await host.Client.GetAsync($"/api/covers/search?q={new string('a', 101)}", Ct), HttpStatusCode.BadRequest);
        await JsonAsync(await host.Client.GetAsync("/api/covers/search?q=hills&page=51", Ct), HttpStatusCode.BadRequest);
        Assert.Single(unsplash.Searched);
    }

    [Fact]
    public async Task A_cover_set_shows_on_the_list_item_and_the_detail()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var unsplash = new FakeUnsplashClient();
        await using var host = await StartWithUnsplashAsync(db, unsplash, Ct);
        var goal = await CreateAsync(host, "Goal", "Walk the ridge");
        var value = await CreateAsync(host, "Value", "Hill walker");
        var task = await CreateAsync(host, "Task", "Pack the boots");

        var set = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/cover", new { subject = goal.ToString(), photoId = "Dwu85P9SOIk", positionY = 30 }, Ct));
        Assert.Equal(goal, set.GetProperty("subject").GetProperty("id").GetGuid());
        AssertCover(set.GetProperty("cover"), 30);
        Assert.True(set.GetProperty("downloadCounted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, set.GetProperty("downloadProblem").ValueKind);
        Assert.Single(unsplash.Downloads);

        // By title too, and an identity statement can have one.
        await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/cover", new { subject = "Hill walker", photoId = "Dwu85P9SOIk" }, Ct));

        // The goal has no bounty of its own: its cover must not ride on the bounty's column.
        AssertCover((await ListItemAsync(host, "Goal", goal)).GetProperty("cover"), 30);
        AssertCover((await ListItemAsync(host, "Value", value)).GetProperty("cover"), 50);
        AssertCover((await JsonAsync(await host.Client.GetAsync($"/api/subjects/{goal}", Ct))).GetProperty("cover"), 30);
        AssertCover((await JsonAsync(await host.Client.GetAsync($"/api/subjects/{goal}/list-item", Ct))).GetProperty("cover"), 30);
        Assert.Equal(JsonValueKind.Null, (await ListItemAsync(host, "Task", task)).GetProperty("cover").ValueKind);

        // With a bounty of its own as well, both read.
        await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/bounty", new { subject = goal.ToString(), amounts = new Dictionary<string, long> { ["karma"] = 3 } }, Ct));
        var both = await ListItemAsync(host, "Goal", goal);
        AssertCover(both.GetProperty("cover"), 30);
        Assert.Equal(3, both.GetProperty("bounty").GetProperty("karma").GetInt64());
    }

    [Fact]
    public async Task Moving_and_clearing_a_cover()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var unsplash = new FakeUnsplashClient();
        await using var host = await StartWithUnsplashAsync(db, unsplash, Ct);
        var project = await CreateAsync(host, "Project", "Build the shed");

        var noCover = await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover/position", new { subject = project.ToString(), positionY = 20 }, Ct),
            HttpStatusCode.BadRequest);
        Assert.Contains("has no cover to move", noCover.GetProperty("error").GetString());

        await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/cover", new { subject = project.ToString(), photoId = "Dwu85P9SOIk" }, Ct));
        var moved = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/cover/position", new { subject = project.ToString(), positionY = 80 }, Ct));
        AssertCover(moved.GetProperty("cover"), 80);
        Assert.False(moved.GetProperty("downloadCounted").GetBoolean());
        Assert.Single(unsplash.Downloads);
        AssertCover((await ListItemAsync(host, "Project", project)).GetProperty("cover"), 80);

        await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover/position", new { subject = project.ToString(), positionY = 101 }, Ct),
            HttpStatusCode.BadRequest);
        await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover/position", new { subject = project.ToString() }, Ct),
            HttpStatusCode.BadRequest);

        var cleared = await JsonAsync(await host.Client.PostAsJsonAsync("/api/cover/clear", new { subject = project.ToString() }, Ct));
        Assert.True(cleared.GetProperty("removed").GetBoolean());
        Assert.Equal(project, cleared.GetProperty("subject").GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await host.Client.GetAsync($"/api/subjects/{project}", Ct))).GetProperty("cover").ValueKind);

        var again = await JsonAsync(await host.Client.PostAsJsonAsync("/api/cover/clear", new { subject = project.ToString() }, Ct));
        Assert.False(again.GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task Refusals_come_back_in_words()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var unsplash = new FakeUnsplashClient();
        await using var host = await StartWithUnsplashAsync(db, unsplash, Ct);
        var goal = await CreateAsync(host, "Goal", "Walk the ridge");
        var task = await CreateAsync(host, "Task", "Pack the boots");

        var wrongType = await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover", new { subject = task.ToString(), photoId = "Dwu85P9SOIk" }, Ct),
            HttpStatusCode.BadRequest);
        Assert.Contains("only identity statements", wrongType.GetProperty("error").GetString());

        await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover", new { subject = goal.ToString() }, Ct),
            HttpStatusCode.BadRequest);
        await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover", new { subject = goal.ToString(), photoId = "../../me" }, Ct),
            HttpStatusCode.BadRequest);
        await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover", new { subject = "No such goal", photoId = "Dwu85P9SOIk" }, Ct),
            HttpStatusCode.NotFound);

        unsplash.Failure = new UnsplashException(UnsplashFailure.NotFound, "That photo is no longer on Unsplash.");
        var gone = await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover", new { subject = goal.ToString(), photoId = "Dwu85P9SOIk" }, Ct),
            HttpStatusCode.NotFound);
        Assert.Equal("That photo is no longer on Unsplash.", gone.GetProperty("error").GetString());

        unsplash.Failure = new UnsplashException(UnsplashFailure.RateLimited, "Unsplash's hourly limit is used up.");
        await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/cover", new { subject = goal.ToString(), photoId = "Dwu85P9SOIk" }, Ct),
            HttpStatusCode.BadGateway);

        Assert.Empty(unsplash.Downloads);
        Assert.Equal(JsonValueKind.Null, (await ListItemAsync(host, "Goal", goal)).GetProperty("cover").ValueKind);
    }

    [Fact]
    public async Task A_malformed_stored_cover_reads_as_none()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, WithoutKey);
        var goal = await CreateAsync(host, "Goal", "Walk the ridge");

        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE bsk.subject
                SET attributes = attributes || '{"cover":{"source":"unsplash","url":"https://evil.example/x.jpg"}}'::jsonb
                WHERE id = @goal;
                """,
                new { goal }, cancellationToken: Ct));
        }

        Assert.Equal(JsonValueKind.Null, (await ListItemAsync(host, "Goal", goal)).GetProperty("cover").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await host.Client.GetAsync($"/api/subjects/{goal}", Ct))).GetProperty("cover").ValueKind);

        // Clearing still works on it, without a key.
        var cleared = await JsonAsync(await host.Client.PostAsJsonAsync("/api/cover/clear", new { subject = goal.ToString() }, Ct));
        Assert.True(cleared.GetProperty("removed").GetBoolean());
    }
}

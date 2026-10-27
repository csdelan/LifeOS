using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-9, D18 (environment plan §4.5–§4.7): Environment through the kernel and the API. The
/// daily credit carries the Home Score, so a rating write folds the domains before and after,
/// answers <c>progress</c> and pays any rank-up it unlocks, and <c>GET /api/domains</c> reads
/// Environment's consistency as <c>kind: "home"</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EnvironmentDomainTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_backdated_walk_through_lifts_Environment_pays_its_rank_and_reads_as_home()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        await db.OpenWalletAsync(daysAgo: 40);
        for (var ago = 20; ago >= 1; ago--)
        {
            await db.CreditAsync(today.AddDays(-ago), 100, Currencies.EnvironmentXp);
        }

        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        // 2,000 Environment XP is lifetime level 22, but with nothing rated the home holds level 1.
        var before = await EnvironmentRowAsync(host);
        Assert.True(before.GetProperty("configured").GetBoolean());
        Assert.Equal("home", before.GetProperty("consistency").GetProperty("kind").GetString());
        Assert.Equal(22, before.GetProperty("lifetime").GetProperty("level").GetInt32());
        Assert.Equal((1, "consistency"), (before.GetProperty("shown").GetProperty("level").GetInt32(),
            before.GetProperty("shown").GetProperty("limitedBy").GetString()));
        Assert.Equal(JsonValueKind.Null, before.GetProperty("current").GetProperty("todayHomeScore").ValueKind);
        var settledThrough = DateOnly.ParseExact(
            before.GetProperty("current").GetProperty("settledThrough").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // A spotless office rated 15 days ago keeps every settled day since well above the bar:
        // the condition level is one less than the kept-up days (level L asks L + 1 so far).
        var office = await JsonAsync(await host.Client.PostAsJsonAsync("/api/environment/spaces", new { name = "Office", weight = 5, pace = 1 }, Ct));
        var walk = await JsonAsync(await host.Client.PostAsJsonAsync("/api/environment/ratings", new
        {
            day = Day(today.AddDays(-15)),
            ratings = new object[] { new { space = office.GetProperty("id").GetString(), tidiness = 5, cleanliness = 5, aesthetics = 5 } }
        }, Ct));
        var keptUp = settledThrough.DayNumber - today.AddDays(-15).DayNumber + 1;
        var change = Assert.Single(walk.GetProperty("progress").EnumerateArray());
        Assert.Equal(
            (Currencies.EnvironmentXp, 1, keptUp - 1, 0, 1, "Apprentice"),
            (change.GetProperty("domain").GetString(), change.GetProperty("levelFrom").GetInt32(),
                change.GetProperty("levelTo").GetInt32(), change.GetProperty("rankFrom").GetInt32(),
                change.GetProperty("rankTo").GetInt32(), change.GetProperty("rankTitle").GetString()));
        var rank = Assert.Single(walk.GetProperty("awards").EnumerateArray(), a => a.GetProperty("trigger").GetString() == LifeDomains.RankTrigger);
        Assert.Equal("rank:environment_xp:10", rank.GetProperty("awardKey").GetString());
        Assert.Equal("Environment Apprentice", rank.GetProperty("note").GetString());
        Assert.Equal(1, rank.GetProperty("amounts").GetProperty(Currencies.Gems).GetInt64());

        // The domains read shows the home's side: the bars, the average Home Score and today's.
        var after = await EnvironmentRowAsync(host);
        var consistency = after.GetProperty("consistency");
        Assert.Equal(("home", keptUp - 1, keptUp), (consistency.GetProperty("kind").GetString(),
            consistency.GetProperty("level").GetInt32(), consistency.GetProperty("windowDays").GetInt32()));
        Assert.InRange(consistency.GetProperty("averageHomeScore").GetDecimal(), 85m, 100m);
        var held = consistency.GetProperty("held");
        Assert.Equal(LifeDomains.EnvironmentCondition.Bar(keptUp - 1), held.GetProperty("bar").GetDecimal());
        Assert.Equal(keptUp, held.GetProperty("activeDays").GetInt32());
        Assert.Equal(LifeDomains.EnvironmentCondition.Bar(keptUp), consistency.GetProperty("next").GetProperty("bar").GetDecimal());
        Assert.InRange(after.GetProperty("current").GetProperty("todayHomeScore").GetDecimal(), 80m, 100m);
        Assert.StartsWith("a Home Score of at least", after.GetProperty("current").GetProperty("rule").GetString());

        // Health's row is unchanged in shape.
        var health = (await host.Client.GetFromJsonAsync<JsonElement>("/api/domains", Ct)).GetProperty("domains")
            .EnumerateArray().Single(d => d.GetProperty("key").GetString() == Currencies.HealthXp);
        Assert.Equal("xp", health.GetProperty("consistency").GetProperty("kind").GetString());

        // Deleting the walk-through takes the level back down; the Gem stays paid.
        var ratingId = walk.GetProperty("ratings")[0].GetProperty("id").GetString();
        var deleted = await JsonAsync(await host.Client.PostAsync($"/api/environment/ratings/{ratingId}/delete", null, Ct));
        var down = Assert.Single(deleted.GetProperty("progress").EnumerateArray());
        Assert.Equal((keptUp - 1, 1), (down.GetProperty("levelFrom").GetInt32(), down.GetProperty("levelTo").GetInt32()));
        Assert.DoesNotContain(deleted.GetProperty("awards").EnumerateArray(), a => a.GetProperty("trigger").GetString() == LifeDomains.RankTrigger);
    }

    private async Task<JsonElement> EnvironmentRowAsync(ApiTestHost host)
        => (await host.Client.GetFromJsonAsync<JsonElement>("/api/domains", Ct)).GetProperty("domains")
            .EnumerateArray().Single(d => d.GetProperty("key").GetString() == Currencies.EnvironmentXp);

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, text);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

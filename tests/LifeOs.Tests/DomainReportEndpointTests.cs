using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 / REWARD-7 (xp-domains plan X7, §3.11): <c>GET /api/domains/report</c> answers
/// every life domain over its last N days: Health's counted, exception and active days, XP,
/// levels, bests and ranks, ending at the last settled day; the others' XP only. Runs in a database of its own (<see cref="DomainTestDatabase"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DomainReportEndpointTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_report_answers_health_in_full_and_the_other_domains_their_xp()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await database.TodayAsync();
        await database.CreditDaysAsync(today, fromAgo: 20, toAgo: 1, xp: 70);
        await database.CreditAsync(today.AddDays(-3), 15, Currencies.SocialXp);
        await database.CreditAsync(today.AddDays(-10), 40, Currencies.SocialXp);

        await using var host = await ApiTestHost.StartAsync(database.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var report = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains/report?days=7", Ct);

        Assert.Equal(Day(today), report.GetProperty("today").GetString());
        Assert.Equal(7, report.GetProperty("days").GetInt32());
        var domains = report.GetProperty("domains").EnumerateArray().ToList();
        Assert.Equal(
            [Currencies.HealthXp, Currencies.SocialXp, Currencies.EnvironmentXp, Currencies.FinanceXp],
            domains.Select(d => d.GetProperty("key").GetString()));

        // Health's week ends at the last settled day: yesterday, or the day before it until
        // 4 a.m. Seven active days of 70 either way.
        var health = domains[0];
        Assert.True(health.GetProperty("configured").GetBoolean());
        var to = DateOnly.Parse(health.GetProperty("to").GetString()!);
        Assert.Contains(to, new[] { today.AddDays(-1), today.AddDays(-2) });
        Assert.Equal(Day(to.AddDays(-6)), health.GetProperty("from").GetString());
        Assert.Equal((7, 0, 7), (health.GetProperty("daysCounted").GetInt32(), health.GetProperty("exceptionDays").GetInt32(),
            health.GetProperty("activeDays").GetInt32()));
        Assert.Equal((1m, 70m), (health.GetProperty("activeShare").GetDecimal(), health.GetProperty("averageBase").GetDecimal()));
        Assert.Equal((490L, 490L), (health.GetProperty("base").GetInt64(), health.GetProperty("paid").GetInt64()));
        // 12 or 13 days before it (840 or 910 XP) is level 7; 19 or 20 days (1,330 or 1,400 XP)
        // is level 9, which the days support. Still Novice.
        Assert.Equal((7, 9), (health.GetProperty("levelFrom").GetInt32(), health.GetProperty("levelTo").GetInt32()));
        Assert.Equal((7, 9), (health.GetProperty("bestLevelFrom").GetInt32(), health.GetProperty("bestLevelTo").GetInt32()));
        Assert.Equal((0, 0), (health.GetProperty("rankFrom").GetInt32(), health.GetProperty("rankTo").GetInt32()));
        Assert.Equal(("Novice", 0, "Novice"), (health.GetProperty("rankTitle").GetString(), health.GetProperty("bestRank").GetInt32(),
            health.GetProperty("bestRankTitle").GetString()));
        Assert.Empty(health.GetProperty("ranksReached").EnumerateArray());
        var ranks = health.GetProperty("ranks").EnumerateArray().ToList();
        Assert.Equal(7, ranks.Count);
        Assert.Equal((Day(to), 9, 0, false), (ranks[^1].GetProperty("day").GetString(), ranks[^1].GetProperty("level").GetInt32(),
            ranks[^1].GetProperty("rank").GetInt32(), ranks[^1].GetProperty("exception").GetBoolean()));

        // Social has no rules: the XP credited to the 7 days ending today, and nothing else.
        var social = domains[1];
        Assert.False(social.GetProperty("configured").GetBoolean());
        Assert.Equal((Day(today.AddDays(-6)), Day(today)),
            (social.GetProperty("from").GetString(), social.GetProperty("to").GetString()));
        Assert.Equal(15, social.GetProperty("paid").GetInt64());
        Assert.Equal(JsonValueKind.Null, social.GetProperty("daysCounted").ValueKind);
        Assert.Equal(JsonValueKind.Null, social.GetProperty("activeDays").ValueKind);
        Assert.Equal(JsonValueKind.Null, social.GetProperty("ranksReached").ValueKind);
        Assert.Equal(JsonValueKind.Null, social.GetProperty("ranks").ValueKind);

        var month = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains/report", Ct);
        Assert.Equal(30, month.GetProperty("days").GetInt32());
        // Twenty days old: the days from the first through the last settled day count, all active.
        var monthHealth = month.GetProperty("domains")[0];
        var counted = to.DayNumber - today.AddDays(-20).DayNumber + 1;
        Assert.Equal((counted, counted, 1m), (monthHealth.GetProperty("daysCounted").GetInt32(),
            monthHealth.GetProperty("activeDays").GetInt32(), monthHealth.GetProperty("activeShare").GetDecimal()));
        Assert.Equal(55, month.GetProperty("domains")[1].GetProperty("paid").GetInt64());

        foreach (var bad in new[] { "days=0", "days=732" })
        {
            var response = await host.Client.GetAsync($"/api/domains/report?{bad}", Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd");
}

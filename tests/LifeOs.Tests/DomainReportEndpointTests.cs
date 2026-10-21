using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 / REWARD-7 (xp-domains plan X7, §3.11): <c>GET /api/domains/report</c> answers
/// every life domain over its last N days: Health's consistency, XP, levels and ranks; the
/// others' XP only. Runs in a database of its own (<see cref="DomainTestDatabase"/>).
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

        // Nothing yet today, so Health's week ends yesterday: 7 good days of 70.
        var health = domains[0];
        Assert.True(health.GetProperty("configured").GetBoolean());
        Assert.Equal((Day(today.AddDays(-7)), Day(today.AddDays(-1))),
            (health.GetProperty("from").GetString(), health.GetProperty("to").GetString()));
        Assert.Equal((7, 7), (health.GetProperty("daysCounted").GetInt32(), health.GetProperty("successfulDays").GetInt32()));
        Assert.Equal((1m, 70m), (health.GetProperty("consistency").GetDecimal(), health.GetProperty("averageBase").GetDecimal()));
        Assert.Equal((490L, 490L), (health.GetProperty("base").GetInt64(), health.GetProperty("paid").GetInt64()));
        // 13 days before it (910 XP) is level 3; all 20 (1,400 XP) is level 5. Still Novice.
        Assert.Equal((3, 5), (health.GetProperty("levelFrom").GetInt32(), health.GetProperty("levelTo").GetInt32()));
        Assert.Equal((0, 0), (health.GetProperty("rankFrom").GetInt32(), health.GetProperty("rankTo").GetInt32()));
        Assert.Equal("Novice", health.GetProperty("rankTitle").GetString());
        Assert.Empty(health.GetProperty("ranksReached").EnumerateArray());

        // Social has no rules: the XP credited to the 7 days ending today, and nothing else.
        var social = domains[1];
        Assert.False(social.GetProperty("configured").GetBoolean());
        Assert.Equal((Day(today.AddDays(-6)), Day(today)),
            (social.GetProperty("from").GetString(), social.GetProperty("to").GetString()));
        Assert.Equal(15, social.GetProperty("paid").GetInt64());
        Assert.Equal(JsonValueKind.Null, social.GetProperty("daysCounted").ValueKind);
        Assert.Equal(JsonValueKind.Null, social.GetProperty("consistency").ValueKind);
        Assert.Equal(JsonValueKind.Null, social.GetProperty("ranksReached").ValueKind);

        var month = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains/report", Ct);
        Assert.Equal(30, month.GetProperty("days").GetInt32());
        // Twenty days old: 20 of the 30 days counted, all of them successful.
        var monthHealth = month.GetProperty("domains")[0];
        Assert.Equal((20, 20, 1m), (monthHealth.GetProperty("daysCounted").GetInt32(),
            monthHealth.GetProperty("successfulDays").GetInt32(), monthHealth.GetProperty("consistency").GetDecimal()));
        Assert.Equal(55, month.GetProperty("domains")[1].GetProperty("paid").GetInt64());

        foreach (var bad in new[] { "days=0", "days=732" })
        {
            var response = await host.Client.GetAsync($"/api/domains/report?{bad}", Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd");
}

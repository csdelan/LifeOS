using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7 (xp-domains plan X5, §3.6): the multiplier on earns. A configured domain's XP is
/// paid at the multiplier of the day it's credited to, from the record as it stands when it's
/// paid, and frozen there: the earn stamps <c>base</c> and <c>multipliers</c>, nothing re-prices
/// it, a reversal gives back exactly what it paid, and success rules read the base.
/// <para>
/// Each test runs in a database of its own (<see cref="DomainTestDatabase"/>): the multiplier
/// reads every Health day in it.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DomainMultiplierTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, long> Health(long xp) => new() { [Currencies.HealthXp] = xp };

    /// <summary><paramref name="amounts"/> with Health XP set to <paramref name="healthXp"/>: what the multiplier changes, and all it changes.</summary>
    private static Dictionary<string, long> WithHealth(IReadOnlyDictionary<string, long> amounts, long healthXp)
        => new(amounts) { [Currencies.HealthXp] = healthXp };

    private static Dictionary<string, long> Negated(IReadOnlyDictionary<string, long> amounts)
        => amounts.ToDictionary(kv => kv.Key, kv => -kv.Value);

    [Fact]
    public async Task A_day_logged_on_the_day_pays_at_todays_multiplier_but_succeeds_on_its_base()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await database.TodayAsync();
        await database.OpenWalletAsync(daysAgo: 40);
        // 26 of the 30 days before today succeeded: ×1.3.
        await database.CreditDaysAsync(today, fromAgo: 30, toAgo: 5, xp: 70);
        var habit = await database.HabitAsync(today, startOffset: 3, healthXp: 50);

        await using var host = await ApiTestHost.StartAsync(database.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var response = await host.Client.PostAsJsonAsync(
            "/api/adhere", new { habit = habit.Id.ToString(), state = "followed", on = today.ToString("yyyy-MM-dd") }, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var award = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("awards").EnumerateArray());

        // 50 × 1.3 = 65, and the earn says how. The day's default Freedom and Life XP aren't
        // domain XP, so they pay as priced.
        var earned = award.GetProperty("amounts").Deserialize<Dictionary<string, long>>()!;
        Assert.Equal(65, earned[Currencies.HealthXp]);
        Assert.Equal(5, earned[Currencies.Freedom]);
        Assert.Equal(50, award.GetProperty("base").GetProperty(Currencies.HealthXp).GetInt64());
        Assert.Equal(1.3m, award.GetProperty("multipliers").GetProperty(Currencies.HealthXp).GetDecimal());
        var payload = await database.PayloadAsync(award.GetProperty("id").GetGuid());
        Assert.Equal(65, payload.GetProperty("amounts").GetProperty(Currencies.HealthXp).GetInt64());
        Assert.Equal(50, payload.GetProperty("base").GetProperty(Currencies.HealthXp).GetInt64());
        Assert.Equal(1.3m, payload.GetProperty("multipliers").GetProperty(Currencies.HealthXp).GetDecimal());

        // Paid 65, but the day's base is 50: under the 60, so today hasn't succeeded.
        var health = (await database.Get<DomainService>().GetAsync(Ct)).Domains
            .Single(d => d.Currency.Key == Currencies.HealthXp).Progress!;
        Assert.Equal((50L, false), (health.TodayBase, health.TodaySucceeded));
        Assert.Equal(65, health.Periods[^1].Paid);

        // The Character page's chart reads the same days: base against the 60, paid beside it.
        var days = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains/health/days?days=6", Ct);
        Assert.Equal(("health_xp", 60L), (days.GetProperty("key").GetString(), days.GetProperty("target").GetInt64()));
        Assert.Equal(today.ToString("yyyy-MM-dd"), days.GetProperty("today").GetString());
        var rows = days.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(6, rows.Count);
        Assert.Equal((today.AddDays(-5).ToString("yyyy-MM-dd"), 70L, true),
            (rows[0].GetProperty("day").GetString(), rows[0].GetProperty("base").GetInt64(), rows[0].GetProperty("succeeded").GetBoolean()));
        Assert.Equal((0L, false), (rows[1].GetProperty("base").GetInt64(), rows[1].GetProperty("succeeded").GetBoolean()));
        Assert.Equal((50L, 65L, false),
            (rows[5].GetProperty("base").GetInt64(), rows[5].GetProperty("paid").GetInt64(), rows[5].GetProperty("succeeded").GetBoolean()));
        Assert.Equal(30, (await host.Client.GetFromJsonAsync<JsonElement>("/api/domains/health/days", Ct)).GetProperty("days").GetArrayLength());
        foreach (var bad in new[] { "/api/domains/health/days?days=0", "/api/domains/health/days?days=732", "/api/domains/coins/days" })
        {
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await host.Client.GetAsync(bad, Ct)).StatusCode);
        }

        // Undoing the day gives back exactly what it paid.
        var undone = await database.Get<AdherenceService>().RecordAsync(habit.Urn, AdherenceResults.Missed, today, cancellationToken: Ct);
        var reversal = Assert.Single(undone.Awards);
        Assert.Equal(LedgerEntries.Reversal, reversal.Entry);
        Assert.Equal(Negated(earned), reversal.Amounts);
        Assert.Null(reversal.Base);
        Assert.Null(reversal.Multipliers);
    }

    [Fact]
    public async Task A_day_logged_late_is_priced_from_the_days_before_it()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await database.TodayAsync();
        await database.OpenWalletAsync(daysAgo: 40);
        // The last 25 days succeeded: ×1.2 today, but only 5 of the 30 days before 20 days ago.
        await database.CreditDaysAsync(today, fromAgo: 25, toAgo: 1, xp: 70);
        var habit = await database.HabitAsync(today, startOffset: 30, healthXp: 60);
        var adherence = database.Get<AdherenceService>();

        var late = Assert.Single((await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-20), cancellationToken: Ct)).Awards);
        var onTime = Assert.Single((await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today, cancellationToken: Ct)).Awards);

        Assert.Equal(60, late.Amounts[Currencies.HealthXp]);
        Assert.Null(late.Base);
        Assert.False((await database.PayloadAsync(late.EventId)).TryGetProperty("multipliers", out _));
        Assert.Equal(WithHealth(late.Amounts, 72), onTime.Amounts);
        Assert.Equal(Health(60), onTime.Base);
        Assert.Equal(new Dictionary<string, decimal> { [Currencies.HealthXp] = 1.2m }, onTime.Multipliers);
    }

    [Fact]
    public async Task Two_entries_on_one_day_can_pay_at_two_multipliers_and_nothing_re_prices_the_first()
    {
        // §3.6: Monday's run paid 40 at ×1.0. Backfilling the days before Monday lifts its window
        // to ×1.2, so yoga logged for Monday afterwards pays at ×1.2, and the run keeps its 40.
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await database.TodayAsync();
        var monday = today.AddDays(-5);
        await database.OpenWalletAsync(daysAgo: 40);
        var run = await database.HabitAsync(today, startOffset: 10, healthXp: 40);
        var yoga = await database.HabitAsync(today, startOffset: 10, healthXp: 25);
        var adherence = database.Get<AdherenceService>();

        var ran = Assert.Single((await adherence.RecordAsync(run.Urn, AdherenceResults.Followed, monday, cancellationToken: Ct)).Awards);
        await database.CreditDaysAsync(monday, fromAgo: 22, toAgo: 1, xp: 70); // 22 of 30: 73%
        var stretched = Assert.Single((await adherence.RecordAsync(yoga.Urn, AdherenceResults.Followed, monday, cancellationToken: Ct)).Awards);

        Assert.Equal(40, ran.Amounts[Currencies.HealthXp]);
        Assert.Null(ran.Multipliers);
        Assert.Equal(30, stretched.Amounts[Currencies.HealthXp]);
        Assert.Equal(new Dictionary<string, decimal> { [Currencies.HealthXp] = 1.2m }, stretched.Multipliers);

        // The sweep leaves the run's 40 alone; Monday pays 70 on a base of 65.
        Assert.Empty(await database.Get<RewardService>().ReconcileAllAsync(Ct));
        Assert.Equal(40, Assert.Single(await database.Get<ILedgerRepository>().GetPaidAsync(run.Id, Ct)).Net[Currencies.HealthXp]);
        var day = (await database.Get<ILedgerRepository>().GetDomainDaysAsync([Currencies.HealthXp], Ct)).Days
            .Single(d => d.Day == monday);
        Assert.Equal((65L, 70L), (day.Base, day.Paid));
    }

    [Fact]
    public async Task Completing_again_is_priced_anew_and_reopening_gives_back_what_was_paid()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await database.TodayAsync();
        await database.OpenWalletAsync(daysAgo: 40);
        var task = await database.Get<SubjectService>().CreateAsync(SubjectTypes.Task, "stretch routine", "{}", cancellationToken: Ct);
        var price = new Dictionary<string, long> { [Currencies.HealthXp] = 25, [Currencies.SocialXp] = 25, [Currencies.Xp] = 10 };
        await database.Get<BountyService>().SetAsync(task.Urn, BountyTriggers.Completed, price, cancellationToken: Ct);
        var status = database.Get<StatusService>();

        // No successful days yet: ×1.0, and the reopen gives back exactly that.
        var first = Assert.Single((await status.ChangeStatusAsync(task.Urn, "Completed", Ct)).Awards);
        var reopened = Assert.Single((await status.ChangeStatusAsync(task.Urn, "In progress", Ct)).Awards);
        Assert.All(price, kv => Assert.Equal(kv.Value, first.Amounts[kv.Key]));
        Assert.Null(first.Multipliers);
        Assert.Equal(Negated(first.Amounts), reopened.Amounts);

        // 26 of the last 30 days succeed since: completing again pays Health at ×1.3. Social XP
        // has no rules and Life XP is no domain, so neither multiplies.
        await database.CreditDaysAsync(today, fromAgo: 30, toAgo: 5, xp: 70);
        var again = Assert.Single((await status.ChangeStatusAsync(task.Urn, "Completed", Ct)).Awards);
        Assert.Equal(WithHealth(first.Amounts, 33), again.Amounts);
        Assert.Equal(Health(25), again.Base);
        Assert.Equal(new Dictionary<string, decimal> { [Currencies.HealthXp] = 1.3m }, again.Multipliers);

        var reopenedAgain = Assert.Single((await status.ChangeStatusAsync(task.Urn, "In progress", Ct)).Awards);
        Assert.Equal(Negated(again.Amounts), reopenedAgain.Amounts);
    }
}

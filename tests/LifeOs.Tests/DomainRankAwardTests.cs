using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7 / REWARD-8 (xp-domains plan X4, X10, §3.7, §7.2): rank-up Gems and what earning
/// writes say about levels and ranks. A rank pays its Gems once, the first time the best rank
/// reaches it, from the write that gets there, the sweep, or <c>POST /api/domains/settle</c> —
/// never twice, never for a rank first reached before the wallet opened, and never reversed.
/// <c>GET /api/domains</c> reads the fold.
/// <para>
/// Each test runs in a database of its own (<see cref="DomainTestDatabase"/>): a wallet opens
/// once, and a rank pays once, per database. Every day that decides something is two or more
/// days back, so it has settled whatever the hour.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DomainRankAwardTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> RankEntryAsync(string connectionString, string awardKey)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var row = await connection.QuerySingleAsync<(string Payload, string Provenance, Guid[] DerivedFrom, long Edges)>(
            """
            SELECT e.payload::text, e.provenance, e.derived_from,
                   (SELECT count(*) FROM bsk.subject_event se WHERE se.event_id = e.id)
            FROM bsk.event e
            WHERE e.kind = 'ledger' AND e.payload->>'award_key' = @awardKey;
            """,
            new { awardKey });
        using var document = JsonDocument.Parse(row.Payload);
        var payload = document.RootElement.Clone();
        Assert.Equal(Provenances.Derived, row.Provenance);
        Assert.Equal(0, row.Edges);
        return JsonSerializer.SerializeToElement(new { payload, derivedFrom = row.DerivedFrom });
    }

    [Fact]
    public async Task The_write_that_reaches_a_rank_pays_its_gems_once_and_says_the_level_and_rank_moved()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var kernel = database.Kernel;
        var today = await database.TodayAsync();
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-40), Ct);
        var habit = await database.HabitAsync(today, startOffset: 30, healthXp: 200);
        var adherence = kernel.GetRequiredService<AdherenceService>();

        // Ten followed days: 2,000 XP is lifetime level 11, but ten active days only let the
        // last 90 days hold level 9 (level L asks L + 1 active days so far). No rank yet.
        for (var ago = 13; ago > 3; ago--)
        {
            var earlier = await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-ago), cancellationToken: Ct);
            Assert.DoesNotContain(earlier.Awards, a => a.Trigger == LifeDomains.RankTrigger);
        }

        // The eleventh lets it hold level 10: Apprentice, on the day it was followed.
        await using var host = await ApiTestHost.StartAsync(database.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var response = await host.Client.PostAsJsonAsync(
            "/api/adhere",
            new { habit = habit.Id.ToString(), state = "followed", on = today.AddDays(-3).ToString("yyyy-MM-dd") },
            Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var earned = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        var rank = Assert.Single(earned.GetProperty("awards").EnumerateArray(), a => a.GetProperty("trigger").GetString() == "rank");
        Assert.Equal("rank:health_xp:10", rank.GetProperty("awardKey").GetString());
        Assert.Equal("earn", rank.GetProperty("entry").GetString());
        Assert.Equal(JsonValueKind.Null, rank.GetProperty("subject").ValueKind);
        Assert.Equal("Health Apprentice", rank.GetProperty("note").GetString());
        Assert.Equal(1, rank.GetProperty("amounts").GetProperty(Currencies.Gems).GetInt64());
        var change = Assert.Single(earned.GetProperty("progress").EnumerateArray());
        Assert.Equal(
            ("health_xp", 9, 10, 0, 1, "Apprentice"),
            (change.GetProperty("domain").GetString(), change.GetProperty("levelFrom").GetInt32(),
                change.GetProperty("levelTo").GetInt32(), change.GetProperty("rankFrom").GetInt32(),
                change.GetProperty("rankTo").GetInt32(), change.GetProperty("rankTitle").GetString()));
        Assert.Equal(
            (9, 10, 0, 1),
            (change.GetProperty("bestLevelFrom").GetInt32(), change.GetProperty("bestLevelTo").GetInt32(),
                change.GetProperty("bestRankFrom").GetInt32(), change.GetProperty("bestRankTo").GetInt32()));

        // Credited to the day it was first reached, citing that day's latest Health earn.
        var entry = await RankEntryAsync(database.ConnectionString, "rank:health_xp:10");
        var payload = entry.GetProperty("payload");
        Assert.Equal(today.AddDays(-3).ToString("yyyy-MM-dd"), payload.GetProperty("credited_on").GetString());
        Assert.Equal(Currencies.HealthXp, payload.GetProperty("domain").GetString());
        Assert.False(payload.TryGetProperty("subject_id", out _));
        var reachedOn = await kernel.GetRequiredService<ILedgerRepository>()
            .GetLatestCreditEventAsync(Currencies.HealthXp, today.AddDays(-3), Ct);
        Assert.Equal(reachedOn, entry.GetProperty("derivedFrom")[0].GetGuid());

        // Never twice: the next day lifts the level to 11, but the rank stays and pays nothing,
        // and neither the sweep nor a settle pays it again.
        var twelfth = await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-2), cancellationToken: Ct);
        Assert.DoesNotContain(twelfth.Awards, a => a.Trigger == LifeDomains.RankTrigger);
        Assert.Equal([new DomainChange(Currencies.HealthXp, 10, 11, 1, 1, "Apprentice", 10, 11, 1, 1)], twelfth.Progress);
        Assert.DoesNotContain(await rewards.ReconcileAllAsync(Ct), a => a.Trigger == LifeDomains.RankTrigger);
        Assert.Empty((await rewards.SettleDomainsAsync(Ct)).Awards);
        Assert.Equal(
            ["rank:health_xp:10"],
            await kernel.GetRequiredService<ILedgerRepository>().GetPaidKeysAsync(LifeDomains.RankTrigger, Ct));
    }

    [Fact]
    public async Task A_rank_reached_without_a_write_pays_when_settled_and_celebrates_from_the_rank_below()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var kernel = database.Kernel;
        var today = await database.TodayAsync();
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-40), Ct);

        // Eleven active days of 200, credited with no write after them: 2,200 XP is lifetime
        // level 12, and the eleventh active day lets the last 90 days hold level 10.
        await database.CreditDaysAsync(today, fromAgo: 13, toAgo: 3, xp: 200);

        await using var host = await ApiTestHost.StartAsync(database.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var domains = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains", Ct);
        var health = domains.GetProperty("domains").EnumerateArray().Single(d => d.GetProperty("key").GetString() == "health_xp");
        Assert.True(health.GetProperty("configured").GetBoolean());
        Assert.Equal(2_200, health.GetProperty("totalXp").GetInt64());
        var lifetime = health.GetProperty("lifetime");
        // Level 12 starts at 2,121 and is 370 long: 79 into it, 291 to go.
        Assert.Equal((12, 370L, 79L, 291L),
            (lifetime.GetProperty("level").GetInt32(), lifetime.GetProperty("levelXp").GetInt64(),
                lifetime.GetProperty("xpIntoLevel").GetInt64(), lifetime.GetProperty("xpToNext").GetInt64()));
        Assert.Equal(10, health.GetProperty("consistency").GetProperty("level").GetInt32());
        var shown = health.GetProperty("shown");
        Assert.Equal((10, 10, 1, "Apprentice", "consistency"),
            (shown.GetProperty("level").GetInt32(), shown.GetProperty("target").GetInt32(), shown.GetProperty("rank").GetInt32(),
                shown.GetProperty("rankTitle").GetString(), shown.GetProperty("limitedBy").GetString()));
        var best = health.GetProperty("best");
        Assert.Equal((10, 1, today.AddDays(-3).ToString("yyyy-MM-dd")),
            (best.GetProperty("level").GetInt32(), best.GetProperty("rank").GetInt32(), best.GetProperty("rankOn").GetString()));
        Assert.Equal(11, health.GetProperty("ranks").GetArrayLength());
        Assert.False(domains.GetProperty("domains").EnumerateArray().Single(d => d.GetProperty("key").GetString() == "social_xp").GetProperty("configured").GetBoolean());

        var settled = await host.Client.PostAsync("/api/domains/settle", null, Ct);
        Assert.True(settled.IsSuccessStatusCode, await settled.Content.ReadAsStringAsync(Ct));
        var earned = await settled.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var rank = Assert.Single(earned.GetProperty("awards").EnumerateArray());
        Assert.Equal(("rank:health_xp:10", "Health Apprentice"), (rank.GetProperty("awardKey").GetString(), rank.GetProperty("note").GetString()));

        // Nothing moved at the settle itself, so it celebrates from just below the rank it paid.
        var change = Assert.Single(earned.GetProperty("progress").EnumerateArray());
        Assert.Equal((9, 10, 0, 1), (change.GetProperty("levelFrom").GetInt32(), change.GetProperty("levelTo").GetInt32(),
            change.GetProperty("rankFrom").GetInt32(), change.GetProperty("rankTo").GetInt32()));
        Assert.Equal((9, 10, 0, 1), (change.GetProperty("bestLevelFrom").GetInt32(), change.GetProperty("bestLevelTo").GetInt32(),
            change.GetProperty("bestRankFrom").GetInt32(), change.GetProperty("bestRankTo").GetInt32()));

        // Settling again pays and announces nothing.
        var again = await (await host.Client.PostAsync("/api/domains/settle", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(0, again.GetProperty("awards").GetArrayLength());
        Assert.Equal(0, again.GetProperty("progress").GetArrayLength());
    }

    [Fact]
    public async Task A_rank_first_reached_before_the_wallet_opened_never_pays_but_still_reads()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var kernel = database.Kernel;
        var today = await database.TodayAsync();
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-3), Ct);

        // Twelve active days of 200 from 25 days ago: Apprentice on the eleventh, 15 days ago.
        await database.CreditDaysAsync(today, fromAgo: 25, toAgo: 14, xp: 200);

        Assert.Empty(await rewards.ReconcileDomainsAsync(Ct));
        Assert.Empty(await rewards.ReconcileAllAsync(Ct));
        Assert.Empty(await kernel.GetRequiredService<ILedgerRepository>().GetPaidKeysAsync(LifeDomains.RankTrigger, Ct));
        var health = (await kernel.GetRequiredService<DomainService>().GetAsync(Ct)).Domains
            .Single(d => d.Currency.Key == Currencies.HealthXp).Progress!;
        Assert.Equal(1, health.BestRank);
        Assert.Equal(new[] { (1, today.AddDays(-15)) }, health.RanksReached.Select(r => (r.Rank, r.QualifiedOn)));
    }

    [Fact]
    public async Task A_rank_the_old_rules_paid_never_pays_again()
    {
        // The old rules (X4) paid Apprentice on a day of their own. The new rules reach it on
        // another day, but under the same award key, so nothing pays twice.
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var kernel = database.Kernel;
        var today = await database.TodayAsync();
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-40), Ct);
        var key = LifeDomains.Health.RankAwardKey(1);
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            entry = LedgerEntries.Earn,
            amounts = new Dictionary<string, long> { [Currencies.Gems] = 1 },
            award_key = key,
            trigger = LifeDomains.RankTrigger,
            credited_on = today.AddDays(-30).ToString("yyyy-MM-dd"),
            domain = Currencies.HealthXp
        });
        await kernel.GetRequiredService<ILedgerRepository>().AppendAsync(
            [new LedgerWrite(
                new NewEvent(EventKinds.Ledger, Provenances.Declared, now, now, KernelSources.Cli, PayloadJson: payload),
                [], GuardBalance: false, AwardKey: key, ExpectPaid: false)],
            Ct);
        await database.CreditDaysAsync(today, fromAgo: 13, toAgo: 3, xp: 200);

        var settled = await rewards.SettleDomainsAsync(Ct);

        Assert.Equal("rank:health_xp:10", key);
        Assert.Empty(settled.Awards);
        Assert.Empty(settled.Progress);
        var health = (await kernel.GetRequiredService<DomainService>().GetAsync(Ct)).Domains
            .Single(d => d.Currency.Key == Currencies.HealthXp).Progress!;
        Assert.Equal((1, today.AddDays(-3)), (health.BestRank, health.RanksReached.Single().QualifiedOn));
        Assert.Equal([key], await kernel.GetRequiredService<ILedgerRepository>().GetPaidKeysAsync(LifeDomains.RankTrigger, Ct));
    }

    [Fact]
    public async Task Before_the_wallet_opens_no_rank_pays()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var kernel = database.Kernel;
        var today = await database.TodayAsync();
        await database.CreditDaysAsync(today, fromAgo: 13, toAgo: 3, xp: 200);

        var settled = await kernel.GetRequiredService<RewardService>().SettleDomainsAsync(Ct);

        Assert.Empty(settled.Awards);
        Assert.Empty(settled.Progress);
    }

    [Fact]
    public async Task A_write_that_moves_only_the_level_says_so_and_one_without_domain_xp_says_nothing()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var kernel = database.Kernel;
        var today = await database.TodayAsync();
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-40), Ct);

        // Nine active days of 100: 900 XP is lifetime level 7, which the last 90 days support.
        await database.CreditDaysAsync(today, fromAgo: 12, toAgo: 4, xp: 100);
        var habit = await database.HabitAsync(today, startOffset: 5, healthXp: 650);
        var status = kernel.GetRequiredService<StatusService>();

        var recorded = await kernel.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-3), cancellationToken: Ct);
        var task = await kernel.GetRequiredService<SubjectService>().CreateAsync(SubjectTypes.Task, "plain task", "{}", cancellationToken: Ct);
        var completed = await status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        // 650 more: lifetime level 10, but ten active days hold 9. Level 7 → 9, still a Novice.
        Assert.Equal([new DomainChange(Currencies.HealthXp, 7, 9, 0, 0, "Novice", 7, 9, 0, 0)], recorded.Progress);
        Assert.NotEmpty(completed.Awards);
        Assert.Empty(completed.Progress);
    }
}

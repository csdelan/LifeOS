using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7 (xp-domains plan X4, §3.7–3.8, §3.11): rank-up Gems and what earning writes say
/// about levels and ranks. A rank pays its Gems once, from the write that reaches it, the
/// sweep, or <c>POST /api/domains/settle</c> — never twice, never for a rank that qualified
/// before the wallet opened, and never reversed. <c>GET /api/domains</c> reads the fold.
/// <para>
/// Each test runs in a database of its own: a wallet opens once, and a rank pays once, per
/// database. Its days are in UTC, the home zone the kernel and the API are given here.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DomainRankAwardTests(PostgresFixture postgres)
{
    private const string Zone = "UTC";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string?> ApiZone = new() { [HomeZone.ConfigurationKey] = Zone };

    /// <summary>A migrated database of its own, dropped afterwards.</summary>
    private sealed class FreshDatabase(string adminConnectionString, string name, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public ServiceProvider Kernel()
            => new ServiceCollection().AddLifeOsKernel(ConnectionString, homeTimeZone: Zone).BuildServiceProvider();

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS {name} WITH (FORCE);");
        }
    }

    private async Task<FreshDatabase> FreshAsync()
    {
        var name = $"rank_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync(Ct);
            await admin.ExecuteAsync(new CommandDefinition($"CREATE DATABASE {name};", cancellationToken: Ct));
            await admin.ExecuteAsync(new CommandDefinition(
                $"ALTER DATABASE {name} SET bsk.home_zone = '{Zone}';", cancellationToken: Ct));
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString;
        await new MigrationRunner(connectionString).ApplyAsync(Ct);
        return new FreshDatabase(postgres.ConnectionString, name, connectionString);
    }

    private async Task<DateOnly> TodayAsync(ServiceProvider kernel)
        => await kernel.GetRequiredService<IHomeCalendar>().DateAtAsync(DateTimeOffset.UtcNow, Ct);

    /// <summary>A daily habit started <paramref name="startOffset"/> days before <paramref name="today"/>, each followed day paying <paramref name="healthXp"/>.</summary>
    private async Task<SubjectRef> HabitAsync(ServiceProvider kernel, DateOnly today, int startOffset, long healthXp)
    {
        var habit = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, $"rank habit {Guid.NewGuid():N}",
            JsonSerializer.Serialize(new
            {
                start = today.AddDays(-startOffset).ToString("yyyy-MM-dd"),
                recurrence = new { freq = "daily" }
            }),
            cancellationToken: Ct);
        await kernel.GetRequiredService<BountyService>().SetAsync(
            habit.Urn, BountyTriggers.Followed, new Dictionary<string, long> { [Currencies.HealthXp] = healthXp },
            cancellationToken: Ct);
        return habit;
    }

    /// <summary>
    /// Health XP credited to <paramref name="day"/> straight into the ledger, with no reconcile
    /// after it — as if time alone, not a write, had opened a rank's gate since.
    /// </summary>
    private async Task CreditAsync(ServiceProvider kernel, DateOnly day, long xp)
    {
        var key = $"test:{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            entry = LedgerEntries.Earn,
            amounts = new Dictionary<string, long> { [Currencies.HealthXp] = xp },
            award_key = key,
            trigger = "test",
            credited_on = day.ToString("yyyy-MM-dd")
        });
        await kernel.GetRequiredService<ILedgerRepository>().AppendAsync(
            [new LedgerWrite(
                new NewEvent(EventKinds.Ledger, Provenances.Declared, now, now, KernelSources.Cli, PayloadJson: payload),
                [], GuardBalance: false, AwardKey: key, ExpectPaid: false)],
            Ct);
    }

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
        await using var database = await FreshAsync();
        await using var kernel = database.Kernel();
        var today = await TodayAsync(kernel);
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-40), Ct);
        var habit = await HabitAsync(kernel, today, startOffset: 30, healthXp: 400);
        var adherence = kernel.GetRequiredService<AdherenceService>();

        // Eight followed days: 3,200 XP, level 9, no rank yet.
        for (var ago = 20; ago > 12; ago--)
        {
            var earlier = await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-ago), cancellationToken: Ct);
            Assert.DoesNotContain(earlier.Awards, a => a.Trigger == LifeDomains.RankTrigger);
        }

        // The ninth makes 3,600: level 10, and Apprentice's window — 9 of 14 days, once the
        // domain is 14 days old — first fits on the day 7 days ago.
        await using var host = await ApiTestHost.StartAsync(database.ConnectionString, Ct, ApiZone);
        var response = await host.Client.PostAsJsonAsync(
            "/api/adhere",
            new { habit = habit.Id.ToString(), state = "followed", on = today.AddDays(-12).ToString("yyyy-MM-dd") },
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

        // Credited to the day it qualified, citing that day's latest Health earn.
        var entry = await RankEntryAsync(database.ConnectionString, "rank:health_xp:10");
        var payload = entry.GetProperty("payload");
        Assert.Equal(today.AddDays(-7).ToString("yyyy-MM-dd"), payload.GetProperty("credited_on").GetString());
        Assert.Equal(Currencies.HealthXp, payload.GetProperty("domain").GetString());
        Assert.False(payload.TryGetProperty("subject_id", out _));
        var dayTwelve = await kernel.GetRequiredService<ILedgerRepository>()
            .GetLatestCreditEventAsync(Currencies.HealthXp, today.AddDays(-7), Ct);
        Assert.Equal(dayTwelve, entry.GetProperty("derivedFrom")[0].GetGuid());

        // Never twice: the next day, the sweep and a settle pay nothing more.
        var tenth = await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-11), cancellationToken: Ct);
        Assert.DoesNotContain(tenth.Awards, a => a.Trigger == LifeDomains.RankTrigger);
        Assert.Empty(tenth.Progress); // 4,000 XP is still level 10
        Assert.DoesNotContain(await rewards.ReconcileAllAsync(Ct), a => a.Trigger == LifeDomains.RankTrigger);
        Assert.Empty((await rewards.SettleDomainsAsync(Ct)).Awards);
        Assert.Equal(
            ["rank:health_xp:10"],
            await kernel.GetRequiredService<ILedgerRepository>().GetPaidKeysAsync(LifeDomains.RankTrigger, Ct));
    }

    [Fact]
    public async Task A_rank_reached_by_time_alone_pays_when_settled_and_celebrates_from_the_rank_below()
    {
        await using var database = await FreshAsync();
        await using var kernel = database.Kernel();
        var today = await TodayAsync(kernel);
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-40), Ct);

        // Seven successful days from 20 days ago, 3,640 XP: Apprentice's gate opened 7 days
        // ago, when the domain turned 14 days old — and no write has run since.
        for (var ago = 20; ago > 13; ago--)
        {
            await CreditAsync(kernel, today.AddDays(-ago), 520);
        }

        await using var host = await ApiTestHost.StartAsync(database.ConnectionString, Ct, ApiZone);
        var domains = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains", Ct);
        var health = domains.GetProperty("domains").EnumerateArray().Single(d => d.GetProperty("key").GetString() == "health_xp");
        Assert.True(health.GetProperty("configured").GetBoolean());
        var lifetime = health.GetProperty("lifetime");
        Assert.Equal((10, 1, "Apprentice"), (lifetime.GetProperty("level").GetInt32(), lifetime.GetProperty("rank").GetInt32(), lifetime.GetProperty("rankTitle").GetString()));
        Assert.Equal(today.AddDays(-7).ToString("yyyy-MM-dd"), lifetime.GetProperty("qualifiedOn").GetString());
        Assert.Equal(3_640, health.GetProperty("totalXp").GetInt64());
        Assert.Equal(3_640 - 3_600, lifetime.GetProperty("xpIntoLevel").GetInt64());
        Assert.Equal("Journeyman", health.GetProperty("nextRank").GetProperty("title").GetString());
        Assert.Equal(70, health.GetProperty("current").GetProperty("target").GetInt64());
        Assert.Equal(11, health.GetProperty("ranks").GetArrayLength());
        Assert.False(domains.GetProperty("domains").EnumerateArray().Single(d => d.GetProperty("key").GetString() == "social_xp").GetProperty("configured").GetBoolean());

        var settled = await host.Client.PostAsync("/api/domains/settle", null, Ct);
        Assert.True(settled.IsSuccessStatusCode, await settled.Content.ReadAsStringAsync(Ct));
        var earned = await settled.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var rank = Assert.Single(earned.GetProperty("awards").EnumerateArray());
        Assert.Equal(("rank:health_xp:10", "Health Apprentice"), (rank.GetProperty("awardKey").GetString(), rank.GetProperty("note").GetString()));
        var change = Assert.Single(earned.GetProperty("progress").EnumerateArray());
        Assert.Equal((9, 10, 0, 1), (change.GetProperty("levelFrom").GetInt32(), change.GetProperty("levelTo").GetInt32(),
            change.GetProperty("rankFrom").GetInt32(), change.GetProperty("rankTo").GetInt32()));

        // Settling again pays and announces nothing.
        var again = await (await host.Client.PostAsync("/api/domains/settle", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(0, again.GetProperty("awards").GetArrayLength());
        Assert.Equal(0, again.GetProperty("progress").GetArrayLength());
    }

    [Fact]
    public async Task A_rank_that_qualified_before_the_wallet_opened_never_pays_but_still_reads()
    {
        await using var database = await FreshAsync();
        await using var kernel = database.Kernel();
        var today = await TodayAsync(kernel);
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-3), Ct);
        for (var ago = 20; ago > 13; ago--)
        {
            await CreditAsync(kernel, today.AddDays(-ago), 520);
        }

        Assert.Empty(await rewards.ReconcileDomainsAsync(Ct));
        Assert.Empty(await rewards.ReconcileAllAsync(Ct));
        Assert.Empty(await kernel.GetRequiredService<ILedgerRepository>().GetPaidKeysAsync(LifeDomains.RankTrigger, Ct));
        var health = (await kernel.GetRequiredService<DomainService>().GetAsync(Ct)).Domains
            .Single(d => d.Currency.Key == Currencies.HealthXp).Progress!;
        Assert.Equal((1, today.AddDays(-7)), (health.Rank, health.RankQualifiedOn));
    }

    [Fact]
    public async Task Before_the_wallet_opens_no_rank_pays()
    {
        await using var database = await FreshAsync();
        await using var kernel = database.Kernel();
        var today = await TodayAsync(kernel);
        for (var ago = 20; ago > 13; ago--)
        {
            await CreditAsync(kernel, today.AddDays(-ago), 520);
        }

        var settled = await kernel.GetRequiredService<RewardService>().SettleDomainsAsync(Ct);

        Assert.Empty(settled.Awards);
        Assert.Empty(settled.Progress);
    }

    [Fact]
    public async Task A_write_that_moves_only_the_level_says_so_and_one_without_domain_xp_says_nothing()
    {
        await using var database = await FreshAsync();
        await using var kernel = database.Kernel();
        var today = await TodayAsync(kernel);
        var rewards = kernel.GetRequiredService<RewardService>();
        await rewards.OpenAsync(DateTimeOffset.UtcNow.AddDays(-40), Ct);
        var habit = await HabitAsync(kernel, today, startOffset: 5, healthXp: 650);
        var status = kernel.GetRequiredService<StatusService>();

        var recorded = await kernel.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-1), cancellationToken: Ct);
        var task = await kernel.GetRequiredService<SubjectService>().CreateAsync(SubjectTypes.Task, "plain task", "{}", cancellationToken: Ct);
        var completed = await status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        // 650 XP: level 1 → 3 (300, then 325 more), still a Novice.
        Assert.Equal([new DomainChange(Currencies.HealthXp, 1, 3, 0, 0, "Novice")], recorded.Progress);
        Assert.NotEmpty(completed.Awards);
        Assert.Empty(completed.Progress);
    }
}

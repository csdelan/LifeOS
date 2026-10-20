using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// The rewards HTTP surface (rewards plan R5): the wallet, the ledger and the catalog
/// reads, redeeming, the catalog writes, and the <c>awards</c> every earning write returns
/// for REWARD-4's feedback. Requests are what the web client sends — subjects named by id.
/// The wallet is global and the database shared, so each test reasons about the change
/// it made.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RewardEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    private Task<ApiTestHost> StartAsync(IReadOnlyDictionary<string, string?>? configuration = null)
        => ApiTestHost.StartAsync(postgres.ConnectionString, Ct, configuration);

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST {path} → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<(HttpStatusCode Status, string Error)> PostRefusedAsync(
        HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, Ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode, json.GetProperty("error").GetString() ?? "");
    }

    private static Task<JsonElement> GetAsync(HttpClient client, string path)
        => client.GetFromJsonAsync<JsonElement>(path, Ct);

    private static async Task<Guid> NewTaskAsync(HttpClient client)
        => (await PostAsync(client, "/api/subjects", new { type = SubjectTypes.Task, title = Unique("rewarded task") }))
            .GetProperty("id").GetGuid();

    /// <summary>
    /// Completes a fresh Task priced at its type default plus 2 Coins (Coins default to zero),
    /// earning 10 XP, 2 Coins and 10 Freedom.
    /// </summary>
    private static async Task EarnTaskDefaultAsync(HttpClient client)
    {
        var task = await NewTaskAsync(client);
        await PostAsync(client, "/api/bounty", new { subject = task.ToString(), amounts = new { coins = 2 } });
        var earned = await PostAsync(client, "/api/status", new { subject = task.ToString(), status = "Completed" });
        Assert.Single(earned.GetProperty("awards").EnumerateArray());
    }

    private static async Task<long> BalanceAsync(HttpClient client, string currency)
    {
        var wallet = await GetAsync(client, "/api/wallet");
        return wallet.GetProperty("balances").EnumerateArray()
            .Single(b => b.GetProperty("currency").GetString() == currency)
            .GetProperty("balance").GetInt64();
    }

    private static long Amount(JsonElement amounts, string currency)
        => amounts.TryGetProperty(currency, out var value) ? value.GetInt64() : 0;

    [Fact]
    public async Task Changing_a_status_returns_what_it_earned_and_what_it_gave_back()
    {
        await using var host = await StartAsync();
        var task = await NewTaskAsync(host.Client);
        var before = await BalanceAsync(host.Client, Currencies.Xp);

        var completed = await PostAsync(host.Client, "/api/status", new { subject = task.ToString(), status = "Completed" });

        var earn = Assert.Single(completed.GetProperty("awards").EnumerateArray());
        Assert.Equal(LedgerEntries.Earn, earn.GetProperty("entry").GetString());
        Assert.Equal(BountyTriggers.Completed, earn.GetProperty("trigger").GetString());
        Assert.Equal(task, earn.GetProperty("subject").GetProperty("id").GetGuid());
        Assert.Equal(10, Amount(earn.GetProperty("amounts"), Currencies.Xp));
        Assert.Equal(0, Amount(earn.GetProperty("amounts"), Currencies.Coins));
        Assert.Equal(before + 10, await BalanceAsync(host.Client, Currencies.Xp));

        var reopened = await PostAsync(host.Client, "/api/status", new { subject = task.ToString(), status = "In progress" });
        var reversal = Assert.Single(reopened.GetProperty("awards").EnumerateArray());
        Assert.Equal(LedgerEntries.Reversal, reversal.GetProperty("entry").GetString());
        Assert.Equal(-10, Amount(reversal.GetProperty("amounts"), Currencies.Xp));

        // A change that pays nothing still answers, with no awards.
        var waiting = await PostAsync(host.Client, "/api/status", new { subject = task.ToString(), status = "Waiting" });
        Assert.Empty(waiting.GetProperty("awards").EnumerateArray());
    }

    [Fact]
    public async Task Following_a_habit_day_returns_its_award()
    {
        await using var host = await StartAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var kernel = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();
        var habit = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, Unique("api habit"),
            JsonSerializer.Serialize(new { start = today.AddDays(-1).ToString("yyyy-MM-dd"), recurrence = new { freq = "daily" } }),
            cancellationToken: Ct);

        var followed = await PostAsync(host.Client, "/api/adhere", new
        {
            habit = habit.Id.ToString(), state = "followed", on = today.ToString("yyyy-MM-dd")
        });

        var award = Assert.Single(followed.GetProperty("awards").EnumerateArray());
        Assert.Equal((LedgerEntries.Earn, BountyTriggers.Followed),
            (award.GetProperty("entry").GetString(), award.GetProperty("trigger").GetString()));
        Assert.Equal(5, Amount(award.GetProperty("amounts"), Currencies.Xp));
    }

    [Fact]
    public async Task Completing_through_the_timer_returns_its_award()
    {
        await using var host = await StartAsync();
        var task = await NewTaskAsync(host.Client);
        await PostAsync(host.Client, "/api/timer", new { task = task.ToString(), action = "start" });

        var done = await PostAsync(host.Client, "/api/timer", new { task = task.ToString(), action = "complete" });

        Assert.True(done.GetProperty("completed").GetBoolean());
        var award = Assert.Single(done.GetProperty("awards").EnumerateArray());
        Assert.Equal(task, award.GetProperty("subject").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_catalog_reward_round_trips_through_add_list_redeem_and_the_ledger()
    {
        await using var host = await StartAsync();
        var movie = await PostAsync(host.Client, "/api/rewards", new
        {
            title = Unique("Movie night"), price = new Dictionary<string, long> { ["freedom"] = 10 },
            description = "Pick a film"
        });
        var fund = await PostAsync(host.Client, "/api/rewards", new
        {
            title = Unique("Bike fund"), kind = RewardKinds.Savings, targetUsd = 100m
        });
        var movieId = movie.GetProperty("id").GetGuid();
        var fundId = fund.GetProperty("id").GetGuid();
        Assert.Equal((RewardKinds.Instant, 10L, "Pick a film"),
            (movie.GetProperty("kind").GetString(), Amount(movie.GetProperty("price"), Currencies.Freedom),
             movie.GetProperty("description").GetString()));
        Assert.Equal((RewardKinds.Savings, 100m, 0m),
            (fund.GetProperty("kind").GetString(), fund.GetProperty("targetUsd").GetDecimal(),
             fund.GetProperty("percentOfTarget").GetDecimal()));

        var catalog = (await GetAsync(host.Client, "/api/rewards")).EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(movieId, catalog);
        Assert.Contains(fundId, catalog);

        await EarnTaskDefaultAsync(host.Client);

        // An instant reward at its price, with no note; then Coins into the savings goal.
        var treat = await PostAsync(host.Client, "/api/redeem", new { reward = movieId.ToString() });
        Assert.Equal(10, Amount(treat.GetProperty("amounts"), Currencies.Freedom));
        Assert.Equal(JsonValueKind.Null, treat.GetProperty("note").ValueKind);
        Assert.Equal(1, treat.GetProperty("reward").GetProperty("redemptions").GetInt64());

        var deposit = await PostAsync(host.Client, "/api/redeem", new
        {
            reward = fundId.ToString(), amounts = new Dictionary<string, long> { ["coins"] = 2 }, note = "First deposit"
        });
        Assert.Equal((1.00m, 0.50m), (deposit.GetProperty("dollars").GetDecimal(), deposit.GetProperty("coinRate").GetDecimal()));
        var progress = deposit.GetProperty("reward");
        Assert.Equal((1.00m, 1.0m, 99.00m),
            (progress.GetProperty("dollarsRedeemed").GetDecimal(), progress.GetProperty("percentOfTarget").GetDecimal(),
             progress.GetProperty("remainingUsd").GetDecimal()));
        Assert.Equal(0.50m, deposit.GetProperty("wallet").GetProperty("coinValueUsd").GetDecimal());

        // Newest first, each naming its Reward.
        var ledger = (await GetAsync(host.Client, "/api/ledger?limit=2")).EnumerateArray().ToList();
        Assert.Equal(2, ledger.Count);
        Assert.Equal((deposit.GetProperty("id").GetGuid(), fundId, "First deposit", 1.00m),
            (ledger[0].GetProperty("id").GetGuid(), ledger[0].GetProperty("subject").GetProperty("id").GetGuid(),
             ledger[0].GetProperty("note").GetString(), ledger[0].GetProperty("dollars").GetDecimal()));
        Assert.Equal((LedgerEntries.Redeem, movieId, -10L),
            (ledger[1].GetProperty("entry").GetString(), ledger[1].GetProperty("subject").GetProperty("id").GetGuid(),
             Amount(ledger[1].GetProperty("amounts"), Currencies.Freedom)));
    }

    [Fact]
    public async Task A_reward_can_be_repriced_and_retargeted_by_id()
    {
        await using var host = await StartAsync();
        var treat = (await PostAsync(host.Client, "/api/rewards", new
        {
            title = Unique("Takeout"), price = new Dictionary<string, long> { ["freedom"] = 30 }
        })).GetProperty("id").GetGuid();
        var goal = (await PostAsync(host.Client, "/api/rewards", new
        {
            title = Unique("Laptop fund"), kind = RewardKinds.Savings, targetUsd = 1200m
        })).GetProperty("id").GetGuid();

        var repriced = await PostAsync(host.Client, "/api/rewards/price", new
        {
            reward = treat.ToString(), price = new Dictionary<string, long> { ["karma"] = 2 }
        });
        var retargeted = await PostAsync(host.Client, "/api/rewards/target", new { reward = goal.ToString(), targetUsd = 900m });
        var cleared = await PostAsync(host.Client, "/api/rewards/target", new { reward = goal.ToString(), targetUsd = (decimal?)null });

        Assert.Equal((2L, 0L), (Amount(repriced.GetProperty("price"), Currencies.Karma), Amount(repriced.GetProperty("price"), Currencies.Freedom)));
        Assert.Equal(900m, retargeted.GetProperty("targetUsd").GetDecimal());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("targetUsd").ValueKind);

        var refused = await PostRefusedAsync(host.Client, "/api/rewards/target", new { reward = treat.ToString(), targetUsd = 10m });
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Contains("not a target", refused.Error);
    }

    [Fact]
    public async Task Redemptions_the_record_cannot_support_are_400s_with_the_reason()
    {
        await using var host = await StartAsync();
        var gems = await BalanceAsync(host.Client, Currencies.Gems);
        var trip = (await PostAsync(host.Client, "/api/rewards", new
        {
            title = Unique("Road trip"), price = new Dictionary<string, long> { ["gems"] = gems + 1 }
        })).GetProperty("id").GetGuid();

        var overdraft = await PostRefusedAsync(host.Client, "/api/redeem", new { reward = trip.ToString() });
        var noNote = await PostRefusedAsync(host.Client, "/api/redeem", new { amounts = new Dictionary<string, long> { ["freedom"] = 1 } });
        var tracked = await PostRefusedAsync(host.Client, "/api/redeem", new { amounts = new Dictionary<string, long> { ["xp"] = 1 }, note = "x" });
        var badRange = await host.Client.GetAsync("/api/ledger?from=2026-10-20T00:00:00Z&to=2026-10-19T00:00:00Z", Ct);

        Assert.Equal((HttpStatusCode.BadRequest, true), (overdraft.Status, overdraft.Error.StartsWith("You have", StringComparison.Ordinal)));
        Assert.Equal((HttpStatusCode.BadRequest, true), (noNote.Status, noNote.Error.Contains("note is required")));
        Assert.Equal((HttpStatusCode.BadRequest, true), (tracked.Status, tracked.Error.Contains("tracked, not spent")));
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);
    }

    [Fact]
    public async Task The_coin_value_comes_from_configuration()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["Rewards:CoinValueUsd"] = "2.00" });
        await EarnTaskDefaultAsync(host.Client);

        var coffee = await PostAsync(host.Client, "/api/redeem", new
        {
            amounts = new Dictionary<string, long> { ["coins"] = 1 }, note = "Coffee out"
        });

        Assert.Equal((2.00m, 2.00m), (coffee.GetProperty("dollars").GetDecimal(), coffee.GetProperty("coinRate").GetDecimal()));
        Assert.Equal(2.00m, (await GetAsync(host.Client, "/api/wallet")).GetProperty("coinValueUsd").GetDecimal());
    }
}

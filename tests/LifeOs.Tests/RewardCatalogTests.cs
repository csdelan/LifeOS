using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-3 (migration 0028, rewards plan R4): the reward catalog. A Reward is an
/// instant reward with a price or a savings goal with a dollar target; redeeming against
/// one is a ledger redeem entry naming it, and bsk.v_reward folds what has been redeemed.
/// Coins are real money: every redemption that spends them stamps the dollars and the
/// rate, so a savings goal's progress never moves when the rate does. The wallet is global
/// and the database shared, so each test earns what it spends and reasons about its own
/// Rewards and the change it made.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RewardCatalogTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Kernel(
        ServiceProvider Provider, RewardService Rewards, RewardCatalogService Catalog, SubjectService Subjects);

    private Kernel Build(decimal? coinValueUsd = null)
    {
        var provider = new ServiceCollection()
            .AddLifeOsKernel(postgres.ConnectionString, coinValueUsd: coinValueUsd)
            .BuildServiceProvider();
        return new Kernel(
            provider,
            provider.GetRequiredService<RewardService>(),
            provider.GetRequiredService<RewardCatalogService>(),
            provider.GetRequiredService<SubjectService>());
    }

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    /// <summary>Earns exactly <paramref name="amounts"/>: a Task priced with that bounty, completed.</summary>
    private async Task EarnAsync(Kernel kernel, IReadOnlyDictionary<string, long> amounts)
    {
        var bounty = JsonSerializer.Serialize(new
        {
            bounty = new Dictionary<string, IReadOnlyDictionary<string, long>> { [BountyTriggers.Completed] = amounts }
        });
        var task = await kernel.Subjects.CreateAsync(
            SubjectTypes.Task, Unique("catalog earning"), bounty, cancellationToken: Ct);
        var completed = await kernel.Provider.GetRequiredService<StatusService>()
            .ChangeStatusAsync(task.Urn, "Completed", Ct);
        Assert.Single(completed.Awards);
    }

    private static Dictionary<string, long> Spend(string currency, long amount) => new() { [currency] = amount };

    /// <summary>What has been redeemed against a Reward (records compare their Price by reference).</summary>
    private static (long, long, decimal, decimal?, decimal?) Progress(CatalogReward reward)
        => (reward.Redemptions, reward.CoinsRedeemed, reward.DollarsRedeemed, reward.PercentOfTarget, reward.RemainingUsd);

    private async Task<long> LedgerEventCountAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'ledger';", cancellationToken: Ct));
    }

    private async Task<(string? SubjectId, string? Note, decimal? Dollars, decimal? CoinRate)> PayloadAsync(Guid eventId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.QuerySingleAsync<(string?, string?, decimal?, decimal?)>(new CommandDefinition(
            """
            SELECT payload->>'subject_id', payload->>'note',
                   (payload->>'dollars')::numeric, (payload->>'coin_rate')::numeric
            FROM bsk.event WHERE id = @eventId;
            """,
            new { eventId }, cancellationToken: Ct));
    }

    [Fact]
    public async Task A_savings_goal_tracks_coins_dollars_and_progress_toward_its_target()
    {
        var kernel = Build();
        var goal = await kernel.Catalog.AddAsync(Unique("Bike fund"), RewardKinds.Savings, targetUsd: 100m, cancellationToken: Ct);
        Assert.Equal((RewardKinds.Savings, 100m, 0L, 0m, 0m), (goal.Kind, goal.TargetUsd, goal.Redemptions, goal.DollarsRedeemed, goal.PercentOfTarget));

        await EarnAsync(kernel, Spend(Currencies.Coins, 80));
        var before = (await kernel.Rewards.GetWalletAsync(Ct)).BalanceOf(Currencies.Coins);

        var first = await kernel.Rewards.RedeemAsync(goal.Reward.Urn, Spend(Currencies.Coins, 30), note: null, Ct);
        var second = await kernel.Rewards.RedeemAsync(goal.Reward.Urn, Spend(Currencies.Coins, 50), "Birthday money", Ct);

        // Each deposit stamps the dollars it moved and the rate.
        Assert.Equal((15.00m, 0.50m), (first.Dollars, first.CoinRate));
        Assert.Equal((25.00m, 0.50m), (second.Dollars, second.CoinRate));
        Assert.Null(first.Note);

        var progress = second.Reward!;
        Assert.Equal(goal.Reward.Id, progress.Reward.Id);
        Assert.Equal(2, progress.Redemptions);
        Assert.Equal(80, progress.CoinsRedeemed);
        Assert.Equal(40.00m, progress.DollarsRedeemed);
        Assert.Equal(40.0m, progress.PercentOfTarget);
        Assert.Equal(60.00m, progress.RemainingUsd);
        Assert.NotNull(progress.LastRedeemedAt);
        Assert.Equal(before - 80, second.Wallet.BalanceOf(Currencies.Coins));

        // The catalog reads the same progress, and the entries name the goal.
        Assert.Equal(Progress(progress), Progress(await kernel.Catalog.GetAsync(goal.Reward.Urn, Ct)));
        var entry = Assert.Single(
            await kernel.Rewards.GetLedgerAsync(limit: 50, cancellationToken: Ct), e => e.EventId == first.EventId);
        Assert.Equal((goal.Reward.Id, -30L, 15.00m, 0.50m), (entry.Subject!.Id, entry.Amounts[Currencies.Coins], entry.Dollars, entry.CoinRate));
    }

    [Fact]
    public async Task A_coin_rate_change_never_rewrites_past_deposits()
    {
        var atHalf = Build();
        var goal = await atHalf.Catalog.AddAsync(Unique("Trip fund"), RewardKinds.Savings, targetUsd: 60m, cancellationToken: Ct);
        await EarnAsync(atHalf, Spend(Currencies.Coins, 40));

        var early = await atHalf.Rewards.RedeemAsync(goal.Reward.Urn, Spend(Currencies.Coins, 20), null, Ct);

        // The rate is raised to a dollar a coin; the next deposit is worth twice as much…
        var atDollar = Build(coinValueUsd: 1.00m);
        var late = await atDollar.Rewards.RedeemAsync(goal.Reward.Urn, Spend(Currencies.Coins, 20), null, Ct);

        Assert.Equal((10.00m, 0.50m), (early.Dollars, early.CoinRate));
        Assert.Equal((20.00m, 1.00m), (late.Dollars, late.CoinRate));

        // …but the first is still worth what it moved: $10 + $20, not 40 coins at today's rate.
        var progress = await atDollar.Catalog.GetAsync(goal.Reward.Urn, Ct);
        Assert.Equal(40, progress.CoinsRedeemed);
        Assert.Equal(30.00m, progress.DollarsRedeemed);
        Assert.Equal(50.0m, progress.PercentOfTarget);
        Assert.Equal(Progress(progress), Progress(await atHalf.Catalog.GetAsync(goal.Reward.Urn, Ct)));
        var stored = await PayloadAsync(early.EventId);
        Assert.Equal((10.00m, 0.50m), (stored.Dollars!.Value, stored.CoinRate!.Value));
        Assert.Equal(1.00m, (await atDollar.Rewards.GetWalletAsync(Ct)).CoinValueUsd);
    }

    [Fact]
    public async Task An_instant_reward_redeems_at_its_catalog_price_with_no_note_needed()
    {
        var kernel = Build();
        var movie = await kernel.Catalog.AddAsync(
            Unique("Movie night"), price: Spend(Currencies.Freedom, 40), cancellationToken: Ct);
        Assert.Equal(RewardKinds.Instant, movie.Kind);
        Assert.Equal(Spend(Currencies.Freedom, 40), movie.Price);
        await EarnAsync(kernel, Spend(Currencies.Freedom, 40));
        var before = (await kernel.Rewards.GetWalletAsync(Ct)).BalanceOf(Currencies.Freedom);

        var redemption = await kernel.Rewards.RedeemAsync(movie.Reward.Title, amounts: null, note: null, Ct);

        Assert.Equal(Spend(Currencies.Freedom, 40), redemption.Amounts);
        Assert.Equal(before - 40, redemption.Wallet.BalanceOf(Currencies.Freedom));
        Assert.Equal((1L, 0L, 0m), (redemption.Reward!.Redemptions, redemption.Reward.CoinsRedeemed, redemption.Reward.DollarsRedeemed));
        Assert.Null(redemption.Reward.PercentOfTarget);

        // No Coins, so no dollars; the entry names the reward and concerns it.
        Assert.Equal((movie.Reward.Id.ToString(), (string?)null, (decimal?)null, (decimal?)null), await PayloadAsync(redemption.EventId));
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var edge = await connection.QuerySingleAsync<(string Relation, string Provenance)>(new CommandDefinition(
            "SELECT relation, provenance FROM bsk.subject_event WHERE event_id = @id AND subject_id = @reward;",
            new { id = redemption.EventId, reward = movie.Reward.Id }, cancellationToken: Ct));
        Assert.Equal((SubjectEventRelations.Concerns, Provenances.Declared), edge);
    }

    [Fact]
    public async Task Amounts_given_override_an_instant_rewards_price_for_that_redemption()
    {
        var kernel = Build();
        var gaming = await kernel.Catalog.AddAsync(
            Unique("Gaming"), price: Spend(Currencies.Freedom, 60), cancellationToken: Ct);
        await EarnAsync(kernel, new Dictionary<string, long> { [Currencies.Freedom] = 20, [Currencies.Coins] = 2 });

        var redemption = await kernel.Rewards.RedeemAsync(
            gaming.Reward.Urn,
            new Dictionary<string, long> { [Currencies.Freedom] = 20, [Currencies.Coins] = 2 },
            "A short session",
            Ct);

        Assert.Equal(new Dictionary<string, long> { [Currencies.Coins] = 2, [Currencies.Freedom] = 20 }, redemption.Amounts);
        Assert.Equal("A short session", redemption.Note);
        // Coins spent on a treat are still real money.
        Assert.Equal((1.00m, 0.50m), (redemption.Dollars, redemption.CoinRate));
        // The catalog price is untouched.
        Assert.Equal(Spend(Currencies.Freedom, 60), redemption.Reward!.Price);
    }

    [Fact]
    public async Task A_custom_redemption_that_spends_coins_stamps_the_dollars_too()
    {
        var kernel = Build();
        await EarnAsync(kernel, Spend(Currencies.Coins, 3));

        var redemption = await kernel.Rewards.RedeemAsync(Spend(Currencies.Coins, 3), "Coffee out", Ct);

        Assert.Null(redemption.Reward);
        Assert.Equal(((string?)null, "Coffee out", 1.50m, 0.50m), await PayloadAsync(redemption.EventId));
    }

    [Fact]
    public async Task A_savings_goal_takes_coins_and_only_coins()
    {
        var kernel = Build();
        var goal = await kernel.Catalog.AddAsync(Unique("Rainy day"), RewardKinds.Savings, cancellationToken: Ct);
        await EarnAsync(kernel, new Dictionary<string, long> { [Currencies.Coins] = 5, [Currencies.Freedom] = 5 });
        var events = await LedgerEventCountAsync();

        var none = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Rewards.RedeemAsync(goal.Reward.Urn, amounts: null, "Nothing", Ct));
        var freedom = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Rewards.RedeemAsync(goal.Reward.Urn, Spend(Currencies.Freedom, 5), null, Ct));

        Assert.Contains("how many Coins", none.Message);
        Assert.Contains("only Coins", freedom.Message);
        Assert.Equal(events, await LedgerEventCountAsync());

        // Without a target there is progress but no percentage.
        var deposit = await kernel.Rewards.RedeemAsync(goal.Reward.Urn, Spend(Currencies.Coins, 5), null, Ct);
        Assert.Equal((2.50m, (decimal?)null, (decimal?)null),
            (deposit.Reward!.DollarsRedeemed, deposit.Reward.PercentOfTarget, deposit.Reward.RemainingUsd));
    }

    [Fact]
    public async Task An_instant_reward_with_no_price_needs_amounts()
    {
        var kernel = Build();
        var treat = await kernel.Catalog.AddAsync(Unique("Something nice"), cancellationToken: Ct);
        Assert.Empty(treat.Price);
        var events = await LedgerEventCountAsync();

        var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Rewards.RedeemAsync(treat.Reward.Urn, amounts: null, note: null, Ct));

        Assert.Contains("no price yet", refused.Message);
        Assert.Equal(events, await LedgerEventCountAsync());
    }

    [Fact]
    public async Task Only_a_live_Reward_can_be_redeemed_against()
    {
        var kernel = Build();
        var task = await kernel.Subjects.CreateAsync(SubjectTypes.Task, Unique("not a reward"), cancellationToken: Ct);
        var retired = await kernel.Catalog.AddAsync(
            Unique("Old treat"), price: Spend(Currencies.Karma, 1), cancellationToken: Ct);
        await kernel.Provider.GetRequiredService<ArchiveService>().ArchiveAsync(retired.Reward.Urn, Ct);
        var events = await LedgerEventCountAsync();

        var notReward = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            kernel.Rewards.RedeemAsync(task.Urn, Spend(Currencies.Karma, 1), "x", Ct));
        var archived = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            kernel.Rewards.RedeemAsync(retired.Reward.Urn, amounts: null, note: null, Ct));

        Assert.Contains("not a Reward", notReward.Message);
        Assert.Contains("archived", archived.Message);
        Assert.Equal(events, await LedgerEventCountAsync());

        // Archived Rewards leave the catalog unless asked for.
        Assert.DoesNotContain(await kernel.Catalog.ListAsync(cancellationToken: Ct), r => r.Reward.Id == retired.Reward.Id);
        Assert.True(Assert.Single(
            await kernel.Catalog.ListAsync(includeArchived: true, Ct), r => r.Reward.Id == retired.Reward.Id).Archived);
    }

    [Fact]
    public async Task A_catalog_price_the_balance_cannot_cover_is_refused()
    {
        var kernel = Build();
        var balance = (await kernel.Rewards.GetWalletAsync(Ct)).BalanceOf(Currencies.Gems);
        var trip = await kernel.Catalog.AddAsync(
            Unique("Road trip"), price: Spend(Currencies.Gems, balance + 1), cancellationToken: Ct);
        var events = await LedgerEventCountAsync();

        var refused = await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            kernel.Rewards.RedeemAsync(trip.Reward.Urn, amounts: null, note: null, Ct));

        Assert.Equal(balance + 1, refused.Requested);
        Assert.Equal(events, await LedgerEventCountAsync());
        Assert.Equal(0, (await kernel.Catalog.GetAsync(trip.Reward.Urn, Ct)).Redemptions);
    }

    [Fact]
    public async Task The_catalog_lists_rewards_by_name()
    {
        var kernel = Build();
        var tag = Guid.NewGuid().ToString("N");
        await kernel.Catalog.AddAsync($"zz catalog {tag}", price: Spend(Currencies.Freedom, 5), cancellationToken: Ct);
        await kernel.Catalog.AddAsync($"AA catalog {tag}", RewardKinds.Savings, targetUsd: 10m, cancellationToken: Ct);

        var mine = (await kernel.Catalog.ListAsync(cancellationToken: Ct))
            .Where(r => r.Reward.Title.EndsWith(tag, StringComparison.Ordinal))
            .Select(r => (r.Reward.Title, r.Kind))
            .ToList();

        Assert.Equal(new[] { ($"AA catalog {tag}", RewardKinds.Savings), ($"zz catalog {tag}", RewardKinds.Instant) }, mine);
    }

    [Theory]
    [InlineData("treat", null, null, "instant or savings")]
    [InlineData("savings", "freedom", null, "no price")]
    [InlineData("instant", null, "50", "Only a savings goal")]
    [InlineData("savings", null, "0", "above zero")]
    [InlineData("instant", "xp", null, "tracked, not spent")]
    [InlineData("instant", "dollars", null, "Unknown currency")]
    public async Task Adding_a_reward_validates_its_kind_price_and_target(
        string kind, string? priceCurrency, string? target, string message)
    {
        var kernel = Build();
        var title = Unique("invalid reward");
        var price = priceCurrency is null ? null : Spend(priceCurrency, 10);
        var targetUsd = target is null ? (decimal?)null : decimal.Parse(target, System.Globalization.CultureInfo.InvariantCulture);

        var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Catalog.AddAsync(title, kind, price, targetUsd, cancellationToken: Ct));

        Assert.Contains(message, refused.Message);
        await Assert.ThrowsAsync<SubjectNotFoundException>(() => kernel.Subjects.ResolveAsync(title, Ct));
    }

    [Fact]
    public async Task Pricing_and_targeting_keep_to_the_rewards_kind()
    {
        var kernel = Build();
        var treat = await kernel.Catalog.AddAsync(
            Unique("Takeout"), price: Spend(Currencies.Freedom, 30), cancellationToken: Ct);
        var goal = await kernel.Catalog.AddAsync(Unique("Laptop fund"), RewardKinds.Savings, targetUsd: 1200m, cancellationToken: Ct);

        // A new price replaces the old one outright.
        var repriced = await kernel.Catalog.SetPriceAsync(
            treat.Reward.Urn, new Dictionary<string, long> { ["Karma"] = 2, ["coins"] = 4 }, Ct);
        Assert.Equal(new Dictionary<string, long> { [Currencies.Coins] = 4, [Currencies.Karma] = 2 }, repriced.Price);

        var retargeted = await kernel.Catalog.SetTargetAsync(goal.Reward.Urn, 999.999m, Ct);
        Assert.Equal(1000.00m, retargeted.TargetUsd);
        var cleared = await kernel.Catalog.SetTargetAsync(goal.Reward.Urn, null, Ct);
        Assert.Null(cleared.TargetUsd);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            kernel.Catalog.SetPriceAsync(goal.Reward.Urn, Spend(Currencies.Coins, 1), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            kernel.Catalog.SetTargetAsync(treat.Reward.Urn, 10m, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Catalog.SetTargetAsync(goal.Reward.Urn, -5m, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Catalog.SetPriceAsync(treat.Reward.Urn, Spend(Currencies.Freedom, 0), Ct));
        var task = await kernel.Subjects.CreateAsync(SubjectTypes.Task, Unique("not priced"), cancellationToken: Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            kernel.Catalog.SetPriceAsync(task.Urn, Spend(Currencies.Freedom, 5), Ct));
    }

    [Fact]
    public async Task The_catalog_reads_hand_edited_attributes_leniently()
    {
        var kernel = Build();
        // bsk set stores text, so a target arrives as "$1,250.50"; a hand-edited price
        // keeps only what can be spent in whole amounts above zero.
        var goal = await kernel.Subjects.CreateAsync(
            SubjectTypes.Reward, Unique("Hand-made goal"),
            """{"kind":" Savings ","target_usd":"$1,250.50"}""", cancellationToken: Ct);
        var treat = await kernel.Subjects.CreateAsync(
            SubjectTypes.Reward, Unique("Hand-made treat"),
            """{"price":{"freedom":"lots","xp":5,"karma":3,"gems":0,"coins":1.5}}""", cancellationToken: Ct);
        var odd = await kernel.Subjects.CreateAsync(
            SubjectTypes.Reward, Unique("Odd one"),
            """{"kind":"savings","target_usd":"soon","price":"free"}""", cancellationToken: Ct);

        var goalRow = await kernel.Catalog.GetAsync(goal.Urn, Ct);
        var treatRow = await kernel.Catalog.GetAsync(treat.Urn, Ct);
        var oddRow = await kernel.Catalog.GetAsync(odd.Urn, Ct);

        Assert.Equal((RewardKinds.Savings, 1250.50m, 0.0m), (goalRow.Kind, goalRow.TargetUsd, goalRow.PercentOfTarget));
        Assert.Equal((RewardKinds.Instant, (decimal?)null), (treatRow.Kind, treatRow.TargetUsd));
        Assert.Equal(Spend(Currencies.Karma, 3), treatRow.Price);
        Assert.Equal((RewardKinds.Savings, (decimal?)null, 0), (oddRow.Kind, oddRow.TargetUsd, oddRow.Price.Count));
    }

    [Fact]
    public async Task The_reader_role_can_read_the_catalog()
    {
        await using var reader = new NpgsqlConnection(BskReader.ConnectionStringFrom(postgres.ConnectionString));
        await reader.OpenAsync(Ct);

        var count = await reader.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.v_reward;", cancellationToken: Ct));

        Assert.True(count >= 0);
    }

    [Theory]
    [InlineData(null, 0.50)]
    [InlineData("  ", 0.50)]
    [InlineData("0.75", 0.75)]
    [InlineData("$1", 1.00)]
    public void A_configured_coin_value_reads_as_dollars(string? configured, double expected)
        => Assert.Equal((decimal)expected, ServiceCollectionExtensions.ParseCoinValueUsd(configured));

    [Theory]
    [InlineData("half a dollar")]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_bad_coin_value_fails_loudly_rather_than_defaulting(string configured)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => ServiceCollectionExtensions.ParseCoinValueUsd(configured));
        Assert.Contains(CoinRates.ConfigurationKey, refused.Message);
    }
}

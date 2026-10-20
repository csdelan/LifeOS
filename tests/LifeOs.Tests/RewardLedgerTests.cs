using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-1 / REWARD-3 / D11 (migration 0025): the rewards ledger. Every earn and
/// redemption is an append-only ledger event; balances are folded by bsk.v_wallet.
/// The wallet is global and the database is shared, so each test reasons about the
/// change it made, never an absolute balance.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RewardLedgerTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private (RewardService Rewards, ILedgerRepository Ledger, ServiceProvider Provider) Build()
    {
        var provider = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();
        return (provider.GetRequiredService<RewardService>(), provider.GetRequiredService<ILedgerRepository>(), provider);
    }

    /// <summary>
    /// Earns <paramref name="amounts"/> the way the reconcile engine (rewards plan R2)
    /// will: a derived ledger entry that cites the completion that caused it and
    /// concerns the completed item.
    /// </summary>
    private async Task<Guid> EarnAsync(
        ServiceProvider provider, IReadOnlyDictionary<string, long> amounts, DateTimeOffset? at = null)
    {
        var task = await provider.GetRequiredService<SubjectService>()
            .CreateAsync(SubjectTypes.Task, $"rewarded task {Guid.NewGuid():N}", cancellationToken: Ct);
        var completed = await provider.GetRequiredService<StatusService>()
            .ChangeStatusAsync(task.Urn, "Completed", Ct);

        var when = at ?? DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            entry = LedgerEntries.Earn,
            amounts,
            award_key = $"completed:{task.Id}",
            trigger = "completed",
            subject_id = task.Id.ToString()
        });
        var earn = new NewEvent(
            EventKinds.Ledger, Provenances.Derived, when, DateTimeOffset.UtcNow, KernelSources.Cli,
            PayloadJson: payload, DerivedFrom: [completed.EventId]);

        var ids = await provider.GetRequiredService<ILedgerRepository>().AppendAsync(
            [new LedgerWrite(earn, [new SubjectEventEdge(task.Id, SubjectEventRelations.Concerns, Provenances.Derived)], GuardBalance: false)],
            Ct);
        return ids[0];
    }

    private async Task<long> LedgerEventCountAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'ledger';", cancellationToken: Ct));
    }

    private static WalletLine Line(Wallet wallet, string currency)
        => wallet.Lines.Single(l => l.Currency.Key == currency);

    [Fact]
    public async Task The_wallet_lists_every_currency_in_display_order_with_tracked_ones_marked()
    {
        var (rewards, _, _) = Build();

        var wallet = await rewards.GetWalletAsync(Ct);

        Assert.Equal(
            Currencies.All.Select(c => c.Key),
            wallet.Lines.Take(Currencies.All.Count).Select(l => l.Currency.Key));
        Assert.Equal(
            new[] { Currencies.Xp, Currencies.Resolve },
            wallet.Lines.Where(l => !l.Currency.Spendable).Select(l => l.Currency.Key));
    }

    [Fact]
    public async Task Earnings_and_redemptions_fold_into_the_balance()
    {
        var (rewards, _, provider) = Build();
        var before = await rewards.GetWalletAsync(Ct);

        await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Freedom] = 30, [Currencies.Xp] = 10 });
        await rewards.RedeemAsync(new Dictionary<string, long> { [Currencies.Freedom] = 12 }, "A short game", Ct);

        var after = await rewards.GetWalletAsync(Ct);
        var freedomBefore = Line(before, Currencies.Freedom);
        var freedomAfter = Line(after, Currencies.Freedom);
        Assert.Equal(freedomBefore.Earned + 30, freedomAfter.Earned);
        Assert.Equal(freedomBefore.Spent + 12, freedomAfter.Spent);
        Assert.Equal(freedomBefore.Balance + 18, freedomAfter.Balance);
        Assert.Equal(Line(before, Currencies.Xp).Balance + 10, Line(after, Currencies.Xp).Balance);
    }

    [Fact]
    public async Task A_redemption_is_a_declared_ledger_entry_with_its_note_and_negative_amounts()
    {
        var (rewards, _, provider) = Build();
        await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Coins] = 5 });

        var redemption = await rewards.RedeemAsync(
            new Dictionary<string, long> { ["Coins"] = 5 }, "  Toward the bike  ", Ct);

        Assert.Equal(5, redemption.Amounts[Currencies.Coins]);
        Assert.Equal("Toward the bike", redemption.Note);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleAsync<(string Kind, string Provenance, string Entry, long Coins, string Note)>(
            new CommandDefinition(
                """
                SELECT kind, provenance, payload->>'entry', (payload->'amounts'->>'coins')::bigint, payload->>'note'
                FROM bsk.event WHERE id = @id;
                """,
                new { id = redemption.EventId }, cancellationToken: Ct));
        Assert.Equal((EventKinds.Ledger, Provenances.Declared, LedgerEntries.Redeem, -5L, "Toward the bike"), row);

        var entry = Assert.Single(await rewards.GetLedgerAsync(limit: 50, cancellationToken: Ct),
            e => e.EventId == redemption.EventId);
        Assert.Equal(LedgerEntries.Redeem, entry.Entry);
        Assert.Equal(-5, entry.Amounts[Currencies.Coins]);
        Assert.Equal("Toward the bike", entry.Note);
        Assert.Null(entry.Subject);
    }

    [Fact]
    public async Task An_earn_reads_back_with_the_item_that_earned_it()
    {
        var (rewards, _, provider) = Build();
        var earnId = await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Xp] = 7, [Currencies.Coins] = 1 });

        var entry = Assert.Single(await rewards.GetLedgerAsync(limit: 50, cancellationToken: Ct), e => e.EventId == earnId);

        Assert.Equal(LedgerEntries.Earn, entry.Entry);
        Assert.Equal(Provenances.Derived, entry.Provenance);
        Assert.Equal("completed", entry.Trigger);
        Assert.Equal(SubjectTypes.Task, entry.Subject?.Type);
        // Amounts come back in display order: Coins before XP.
        Assert.Equal(new[] { Currencies.Coins, Currencies.Xp }, entry.Amounts.Keys);
        Assert.Equal(7, entry.Amounts[Currencies.Xp]);
    }

    [Fact]
    public async Task Redeeming_more_than_the_balance_is_refused_and_writes_nothing()
    {
        var (rewards, _, provider) = Build();
        await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Gems] = 2 });
        var balance = (await rewards.GetWalletAsync(Ct)).BalanceOf(Currencies.Gems);
        var events = await LedgerEventCountAsync();

        var refused = await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            rewards.RedeemAsync(new Dictionary<string, long> { [Currencies.Gems] = balance + 1 }, "A road trip", Ct));

        Assert.Equal(Currencies.Gems, refused.Currency);
        Assert.Equal(balance, refused.Balance);
        Assert.Equal(balance + 1, refused.Requested);
        Assert.Equal($"You have {balance} Gems; this needs {balance + 1}.", refused.Message);
        Assert.Equal(events, await LedgerEventCountAsync());
        Assert.Equal(balance, (await rewards.GetWalletAsync(Ct)).BalanceOf(Currencies.Gems));
    }

    [Fact]
    public async Task A_refused_redemption_leaves_its_other_currencies_untouched_too()
    {
        var (rewards, _, provider) = Build();
        await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Freedom] = 10, [Currencies.Karma] = 1 });
        var before = await rewards.GetWalletAsync(Ct);

        await Assert.ThrowsAsync<InsufficientBalanceException>(() => rewards.RedeemAsync(
            new Dictionary<string, long>
            {
                [Currencies.Freedom] = 1,
                [Currencies.Karma] = before.BalanceOf(Currencies.Karma) + 1
            },
            "Freedom is covered, Karma is not",
            Ct));

        var after = await rewards.GetWalletAsync(Ct);
        Assert.Equal(before.BalanceOf(Currencies.Freedom), after.BalanceOf(Currencies.Freedom));
        Assert.Equal(before.BalanceOf(Currencies.Karma), after.BalanceOf(Currencies.Karma));
    }

    [Theory]
    [InlineData(Currencies.Xp)]
    [InlineData(Currencies.Resolve)]
    public async Task Tracked_currencies_cannot_be_redeemed(string currency)
    {
        var (rewards, _, provider) = Build();
        await EarnAsync(provider, new Dictionary<string, long> { [currency] = 50 });
        var events = await LedgerEventCountAsync();

        var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            rewards.RedeemAsync(new Dictionary<string, long> { [currency] = 1 }, "Spend the unspendable", Ct));

        Assert.Contains("tracked, not spent", refused.Message);
        Assert.Equal(events, await LedgerEventCountAsync());
    }

    [Theory]
    [InlineData("dollars", 5L, "A note")]
    [InlineData("freedom", 0L, "A note")]
    [InlineData("freedom", -5L, "A note")]
    [InlineData("freedom", 5L, "   ")]
    public async Task A_redemption_needs_a_known_currency_a_positive_amount_and_a_note(
        string currency, long amount, string note)
    {
        var (rewards, _, _) = Build();
        var events = await LedgerEventCountAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            rewards.RedeemAsync(new Dictionary<string, long> { [currency] = amount }, note, Ct));

        Assert.Equal(events, await LedgerEventCountAsync());
    }

    [Fact]
    public async Task A_currency_named_twice_in_one_redemption_is_refused()
    {
        var (rewards, _, _) = Build();

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => rewards.RedeemAsync(
            new Dictionary<string, long> { ["freedom"] = 1, ["Freedom"] = 2 }, "Twice", Ct));

        Assert.Contains("listed twice", refused.Message);
    }

    [Fact]
    public async Task Two_redemptions_racing_for_the_same_balance_cannot_both_land()
    {
        var (rewards, _, provider) = Build();
        await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Karma] = 7 });
        var balance = (await rewards.GetWalletAsync(Ct)).BalanceOf(Currencies.Karma);
        var spendAll = new Dictionary<string, long> { [Currencies.Karma] = balance };

        async Task<bool> TryRedeemAsync()
        {
            try
            {
                await rewards.RedeemAsync(spendAll, "Racing for the same Karma", Ct);
                return true;
            }
            catch (InsufficientBalanceException)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(TryRedeemAsync(), TryRedeemAsync());

        Assert.Single(outcomes, landed => landed);
        Assert.Equal(0, (await rewards.GetWalletAsync(Ct)).BalanceOf(Currencies.Karma));
    }

    [Fact]
    public async Task Opening_the_wallet_is_idempotent()
    {
        var (rewards, _, _) = Build();

        var first = await rewards.OpenAsync(cancellationToken: Ct);
        var second = await rewards.OpenAsync(cancellationToken: Ct);

        Assert.False(second.Created);
        Assert.Equal(first.OpenedAt, second.OpenedAt);
        Assert.Equal(first.OpenedAt, (await rewards.GetWalletAsync(Ct)).OpenedAt);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var opens = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'ledger' AND payload->>'entry' = 'open';",
            cancellationToken: Ct));
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task Ledger_entries_read_newest_first_within_a_range_and_a_limit()
    {
        var (rewards, _, provider) = Build();
        // A stretch of the past no other test writes to.
        var t0 = new DateTimeOffset(2001, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var first = await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Xp] = 1 }, t0);
        var second = await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Xp] = 2 }, t0.AddHours(1));
        await EarnAsync(provider, new Dictionary<string, long> { [Currencies.Xp] = 3 }, t0.AddHours(2));

        // The end is exclusive: the entry at t0 + 2h falls outside.
        var inRange = await rewards.GetLedgerAsync(t0, t0.AddHours(2), cancellationToken: Ct);
        Assert.Equal(new[] { second, first }, inRange.Select(e => e.EventId));

        var newest = await rewards.GetLedgerAsync(t0, t0.AddHours(2), limit: 1, cancellationToken: Ct);
        Assert.Equal(new[] { second }, newest.Select(e => e.EventId));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            rewards.GetLedgerAsync(t0.AddHours(2), t0, cancellationToken: Ct));
    }

    [Fact]
    public async Task The_reader_role_can_read_the_ledger_and_wallet()
    {
        await using var reader = new NpgsqlConnection(BskReader.ConnectionStringFrom(postgres.ConnectionString));
        await reader.OpenAsync(Ct);

        foreach (var sql in new[]
                 {
                     "SELECT count(*) FROM bsk.v_ledger;",
                     "SELECT count(*) FROM bsk.v_wallet;",
                     "SELECT count(*) FROM (SELECT bsk.ledger_opened_at()) o;"
                 })
        {
            Assert.True(await reader.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: Ct)) >= 0);
        }
    }
}

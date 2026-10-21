using System.Reflection;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Migrations;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-1 / REWARD-2 / D11 (rewards plan R2, migration 0026): earning on work.
/// Completing a Task, Project or Goal earns its bounty — its own price or the type
/// default — through the reconcile every status change runs; moving it off Completed
/// gives back exactly what it paid. The wallet is global and the database shared, so
/// each test reasons about its own items and the change it made.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RewardEarningTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Kernel(
        ServiceProvider Provider, SubjectService Subjects, StatusService Status, RewardService Rewards,
        BountyService Bounties, ILedgerRepository Ledger);

    private Kernel Build(string? connectionString = null)
    {
        var provider = new ServiceCollection()
            .AddLifeOsKernel(connectionString ?? postgres.ConnectionString)
            .BuildServiceProvider();
        return new Kernel(
            provider,
            provider.GetRequiredService<SubjectService>(),
            provider.GetRequiredService<StatusService>(),
            provider.GetRequiredService<RewardService>(),
            provider.GetRequiredService<BountyService>(),
            provider.GetRequiredService<ILedgerRepository>());
    }

    private Task<SubjectRef> CreateAsync(Kernel kernel, string type, string attributesJson = "{}")
        => kernel.Subjects.CreateAsync(type, $"earning {type} {Guid.NewGuid():N}", attributesJson, cancellationToken: Ct);

    /// <summary>
    /// A status change written straight to the event store — a completion whose own
    /// reconcile never ran (a crash, an import).
    /// </summary>
    private async Task<Guid> RecordStatusWithoutReconcileAsync(Kernel kernel, SubjectRef subject, string status)
    {
        var now = DateTimeOffset.UtcNow;
        return await kernel.Provider.GetRequiredService<IEventStore>().AppendAsync(
            new NewEvent(EventKinds.StateChange, Provenances.Declared, now, now, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new { subject_id = subject.Id.ToString(), status })),
            Ct);
    }

    /// <summary>What the ledger holds for <paramref name="subject"/>, per currency.</summary>
    private async Task<Dictionary<string, long>> NetForAsync(Kernel kernel, SubjectRef subject)
        => (await kernel.Ledger.GetPaidAsync(subject.Id, Ct))
            .SelectMany(p => p.Net)
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));

    private async Task<long> LedgerEntriesForAsync(SubjectRef subject, string? connectionString = null)
    {
        await using var connection = new NpgsqlConnection(connectionString ?? postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'ledger' AND payload->>'subject_id' = @id;",
            new { id = subject.Id.ToString() }, cancellationToken: Ct));
    }

    private static IReadOnlyDictionary<string, long> Default(string type)
        => BountyDefaults.For(type, BountyTriggers.Completed);

    [Fact]
    public async Task Completing_a_task_earns_its_type_default()
    {
        var kernel = Build();
        var task = await CreateAsync(kernel, SubjectTypes.Task);
        var before = await kernel.Rewards.GetWalletAsync(Ct);

        var completed = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        var award = Assert.Single(completed.Awards);
        Assert.Equal(LedgerEntries.Earn, award.Entry);
        Assert.Equal(BountyTriggers.Completed, award.Trigger);
        Assert.Equal(AwardKeys.Completed(task.Id), award.AwardKey);
        Assert.Equal(Default(SubjectTypes.Task), award.Amounts);

        var after = await kernel.Rewards.GetWalletAsync(Ct);
        foreach (var (currency, amount) in Default(SubjectTypes.Task))
        {
            Assert.Equal(before.BalanceOf(currency) + amount, after.BalanceOf(currency));
        }

        // The earn is derived from the completion and concerns the task.
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleAsync<(string Provenance, Guid[] DerivedFrom, long Edges)>(
            new CommandDefinition(
                """
                SELECT e.provenance, e.derived_from,
                       (SELECT count(*) FROM bsk.subject_event se
                        WHERE se.event_id = e.id AND se.subject_id = @task AND se.relation = 'concerns')
                FROM bsk.event e WHERE e.id = @id;
                """,
                new { id = award.EventId, task = task.Id }, cancellationToken: Ct));
        Assert.Equal(Provenances.Derived, row.Provenance);
        Assert.Equal(new[] { completed.EventId }, row.DerivedFrom);
        Assert.Equal(1, row.Edges);
    }

    [Theory]
    [InlineData(SubjectTypes.Project)]
    [InlineData(SubjectTypes.Goal)]
    public async Task Completing_a_project_or_goal_earns_its_bigger_default(string type)
    {
        var kernel = Build();
        var item = await CreateAsync(kernel, type);

        var completed = await kernel.Status.ChangeStatusAsync(item.Urn, "Completed", Ct);

        Assert.Equal(Default(type), Assert.Single(completed.Awards).Amounts);
        Assert.True(Default(type)[Currencies.Gems] > 0);
    }

    [Fact]
    public async Task A_custom_bounty_pays_in_place_of_the_default()
    {
        var kernel = Build();
        var task = await CreateAsync(kernel, SubjectTypes.Task);
        await kernel.Bounties.SetAsync(
            task.Urn, null, new Dictionary<string, long> { ["karma"] = 3, ["xp"] = 25, ["freedom"] = 0 }, cancellationToken: Ct);

        var completed = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        // Starts from the default, adds Karma, raises XP, drops Freedom.
        Assert.Equal(
            new Dictionary<string, long> { [Currencies.Karma] = 3, [Currencies.Xp] = 25 },
            Assert.Single(completed.Awards).Amounts);
    }

    [Fact]
    public async Task A_bounty_of_nothing_pays_nothing()
    {
        var kernel = Build();
        var task = await CreateAsync(kernel, SubjectTypes.Task);
        await kernel.Bounties.SetNothingAsync(task.Urn, null, cancellationToken: Ct);

        var completed = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        Assert.Empty(completed.Awards);
        Assert.Equal(0, await LedgerEntriesForAsync(task));
    }

    [Fact]
    public async Task An_unreadable_bounty_falls_back_to_the_default()
    {
        var kernel = Build();
        var task = await CreateAsync(kernel, SubjectTypes.Task);
        await kernel.Provider.GetRequiredService<AttributeService>().SetAsync(
            task.Urn, [new AttributeAssignment(Bounty.AttributeKey, "lots please")], Ct);

        var completed = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        Assert.Equal(Default(SubjectTypes.Task), Assert.Single(completed.Awards).Amounts);
    }

    [Fact]
    public async Task Reopening_reverses_exactly_and_completing_again_pays_once_more()
    {
        var kernel = Build();
        var task = await CreateAsync(kernel, SubjectTypes.Task);

        var first = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);
        var reopened = await kernel.Status.ChangeStatusAsync(task.Urn, "In progress", Ct);

        var reversal = Assert.Single(reopened.Awards);
        Assert.Equal(LedgerEntries.Reversal, reversal.Entry);
        Assert.Equal(
            Assert.Single(first.Awards).Amounts.ToDictionary(kv => kv.Key, kv => -kv.Value),
            reversal.Amounts);
        Assert.Empty(await NetForAsync(kernel, task));

        // The reversal cites the reopening that undid the award.
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            var derivedFrom = await connection.ExecuteScalarAsync<Guid[]>(new CommandDefinition(
                "SELECT derived_from FROM bsk.event WHERE id = @id;", new { id = reversal.EventId },
                cancellationToken: Ct));
            Assert.Equal(new[] { reopened.EventId }, derivedFrom);
        }

        var again = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);
        Assert.Equal(LedgerEntries.Earn, Assert.Single(again.Awards).Entry);
        Assert.Equal(Default(SubjectTypes.Task), await NetForAsync(kernel, task));
        Assert.Equal(3, await LedgerEntriesForAsync(task));
    }

    [Fact]
    public async Task Saying_completed_twice_pays_once()
    {
        var kernel = Build();
        var task = await CreateAsync(kernel, SubjectTypes.Task);

        await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);
        var repeated = await kernel.Status.ChangeStatusAsync(task.Urn, "completed", Ct);

        Assert.Empty(repeated.Awards);
        Assert.Equal(Default(SubjectTypes.Task), await NetForAsync(kernel, task));
    }

    [Fact]
    public async Task An_edited_bounty_never_reprices_a_past_earn()
    {
        var kernel = Build();
        var task = await CreateAsync(kernel, SubjectTypes.Task);
        await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        await kernel.Bounties.SetAsync(task.Urn, null, new Dictionary<string, long> { ["xp"] = 999 }, cancellationToken: Ct);

        Assert.Empty(await kernel.Rewards.ReconcileAsync(task, cancellationToken: Ct));
        Assert.Equal(Default(SubjectTypes.Task), await NetForAsync(kernel, task));

        // Reopening gives back what was paid, not the new price.
        var reopened = await kernel.Status.ChangeStatusAsync(task.Urn, "Not started", Ct);
        Assert.Equal(-Default(SubjectTypes.Task)[Currencies.Xp], Assert.Single(reopened.Awards).Amounts[Currencies.Xp]);
    }

    [Fact]
    public async Task A_completion_dated_before_the_wallet_opened_never_pays()
    {
        var kernel = Build();
        await kernel.Rewards.OpenAsync(cancellationToken: Ct);
        var task = await CreateAsync(kernel, SubjectTypes.Task);

        // A status change dated well before any opening (the wallet is never opened before 2001).
        var backdated = new StatusService(
            kernel.Subjects,
            kernel.Provider.GetRequiredService<IEventStore>(),
            kernel.Rewards,
            new TimerTestClock(new DateTimeOffset(2000, 6, 1, 12, 0, 0, TimeSpan.Zero)),
            KernelSources.Cli);
        var completed = await backdated.ChangeStatusAsync(task.Urn, "Completed", Ct);

        Assert.Empty(completed.Awards);
        Assert.Empty(await kernel.Rewards.ReconcileAsync(task, cancellationToken: Ct));
        Assert.Equal(0, await LedgerEntriesForAsync(task));
    }

    [Theory]
    [InlineData(SubjectTypes.Task, "Cancelled")]
    [InlineData(SubjectTypes.Project, "Abandoned")]
    [InlineData(SubjectTypes.Goal, "Active")]
    public async Task Other_statuses_earn_nothing(string type, string status)
    {
        var kernel = Build();
        var item = await CreateAsync(kernel, type);

        var changed = await kernel.Status.ChangeStatusAsync(item.Urn, status, Ct);

        Assert.Empty(changed.Awards);
    }

    [Fact]
    public async Task Types_that_do_not_earn_never_pay_and_cannot_be_priced()
    {
        var kernel = Build();
        var appointment = await CreateAsync(kernel, SubjectTypes.Appointment);

        var completed = await kernel.Status.ChangeStatusAsync(appointment.Urn, "Completed", Ct);

        Assert.Empty(completed.Awards);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            kernel.Bounties.SetAsync(appointment.Urn, null, new Dictionary<string, long> { ["xp"] = 5 }, cancellationToken: Ct));
        Assert.Contains("only Task, Project, Goal, Habit earn", refused.Message);
    }

    [Fact]
    public async Task The_trigger_view_covers_exactly_the_earning_types_and_triggers()
    {
        var kernel = Build();
        var events = kernel.Provider.GetRequiredService<IEventStore>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var types = typeof(SubjectTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, FieldType: var t } && t == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        Assert.Contains(SubjectTypes.Task, types);

        foreach (var type in types)
        {
            // Every type the kernel knows, made minimally valid (a Value needs a statement)
            // and given a daily recurrence, then every fact that can pay, all behind
            // reconcile's back: one item Completed; another Established with a week of
            // followed days (enough for the 7-day milestone).
            var attributes = JsonSerializer.Serialize(new
            {
                statement = "a reward drift check",
                start = today.AddDays(-7).ToString("yyyy-MM-dd"),
                recurrence = new { freq = "daily" }
            });
            var completed = await CreateAsync(kernel, type, attributes);
            await RecordStatusWithoutReconcileAsync(kernel, completed, "Completed");

            var established = await CreateAsync(kernel, type, attributes);
            for (var day = 6; day >= 0; day--)
            {
                var now = DateTimeOffset.UtcNow;
                await events.AppendAsync(new NewEvent(
                    EventKinds.Activity, Provenances.Declared, now, now, KernelSources.Cli,
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        subject_id = established.Id.ToString(),
                        occurrence = today.AddDays(-day).ToString("yyyy-MM-dd"),
                        result = AdherenceResults.Followed
                    })), Ct);
            }

            await RecordStatusWithoutReconcileAsync(kernel, established, "Established");

            var triggers = (await kernel.Ledger.GetTriggersAsync(completed.Id, Ct))
                .Concat(await kernel.Ledger.GetTriggersAsync(established.Id, Ct))
                .Select(t => t.Trigger)
                .ToHashSet();
            Assert.True(
                BountyTriggers.Earns(type) == (triggers.Count > 0),
                $"{type}: BountyTriggers says earns={BountyTriggers.Earns(type)}, v_reward_trigger has [{string.Join(", ", triggers)}].");
            Assert.All(triggers, t => Assert.True(BountyTriggers.Pays(type, t), $"{type} has an unpriced trigger '{t}'."));
            // Every trigger has a default except the opt-in ones, which pay only once priced.
            Assert.All(
                BountyTriggers.For(type),
                t => Assert.Equal(!BountyTriggers.OptIn.Contains(t), BountyDefaults.For(type, t).Count > 0));
            Assert.All(BountyTriggers.For(type), t => Assert.False(
                BountyDefaults.For(type, t).ContainsKey(Currencies.Coins), $"{type} {t} defaults to Coins."));
            if (type == SubjectTypes.Habit)
            {
                Assert.Equal(
                    new[] { BountyTriggers.Established, BountyTriggers.Followed, BountyTriggers.Streak(7) },
                    triggers.Order(StringComparer.Ordinal));
            }
        }
    }

    [Fact]
    public async Task The_timers_complete_pays()
    {
        var kernel = Build();
        var timers = kernel.Provider.GetRequiredService<TimerService>();
        var task = await CreateAsync(kernel, SubjectTypes.Task);
        await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);

        var completed = await timers.CompleteAsync(task.Urn, Ct);

        Assert.True(completed.Completed);
        Assert.Equal(Default(SubjectTypes.Task), Assert.Single(completed.Awards).Amounts);
    }

    [Fact]
    public async Task A_sweep_heals_a_completion_whose_reconcile_never_ran()
    {
        var kernel = Build();
        await kernel.Rewards.OpenAsync(cancellationToken: Ct);
        var task = await CreateAsync(kernel, SubjectTypes.Task);
        await RecordStatusWithoutReconcileAsync(kernel, task, "Completed");

        var healed = await kernel.Rewards.ReconcileAllAsync(Ct);

        var earn = Assert.Single(healed, a => a.Subject?.Id == task.Id);
        Assert.Equal(LedgerEntries.Earn, earn.Entry);
        Assert.DoesNotContain(await kernel.Rewards.ReconcileAllAsync(Ct), a => a.Subject?.Id == task.Id);

        // And the other way: reopened behind reconcile's back, the sweep gives it back.
        var reopen = await RecordStatusWithoutReconcileAsync(kernel, task, "In progress");
        var reversal = Assert.Single(await kernel.Rewards.ReconcileAllAsync(Ct), a => a.Subject?.Id == task.Id);
        Assert.Equal(LedgerEntries.Reversal, reversal.Entry);
        Assert.Empty(await NetForAsync(kernel, task));

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var derivedFrom = await connection.ExecuteScalarAsync<Guid[]>(new CommandDefinition(
            "SELECT derived_from FROM bsk.event WHERE id = @id;", new { id = reversal.EventId },
            cancellationToken: Ct));
        Assert.Equal(new[] { reopen }, derivedFrom);
    }

    [Fact]
    public async Task Two_reconciles_racing_on_one_item_pay_it_once()
    {
        var kernel = Build();
        await kernel.Rewards.OpenAsync(cancellationToken: Ct);
        var task = await CreateAsync(kernel, SubjectTypes.Task);
        await RecordStatusWithoutReconcileAsync(kernel, task, "Completed");

        var outcomes = await Task.WhenAll(
            kernel.Rewards.ReconcileAsync(task, cancellationToken: Ct),
            kernel.Rewards.ReconcileAsync(task, cancellationToken: Ct),
            kernel.Rewards.ReconcileAsync(task, cancellationToken: Ct));

        Assert.Single(outcomes.SelectMany(a => a));
        Assert.Equal(Default(SubjectTypes.Task), await NetForAsync(kernel, task));
    }

    [Fact]
    public async Task On_a_fresh_wallet_the_first_paying_completion_opens_it_and_history_stays_unpaid()
    {
        var database = $"reward_fresh_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync(Ct);
            await admin.ExecuteAsync(new CommandDefinition($"CREATE DATABASE {database};", cancellationToken: Ct));
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }
            .ConnectionString;
        try
        {
            await new MigrationRunner(connectionString).ApplyAsync(Ct);
            var kernel = Build(connectionString);
            await using var _ = kernel.Provider;

            // History from before launch: completed, never reconciled.
            var old = await CreateAsync(kernel, SubjectTypes.Task);
            await RecordStatusWithoutReconcileAsync(kernel, old, "Completed");
            Assert.Empty(await kernel.Rewards.ReconcileAllAsync(Ct));
            Assert.Null((await kernel.Rewards.GetWalletAsync(Ct)).OpenedAt);

            // A change that pays nothing (and an item priced at nothing) opens nothing.
            var task = await CreateAsync(kernel, SubjectTypes.Task);
            Assert.Empty((await kernel.Status.ChangeStatusAsync(task.Urn, "In progress", Ct)).Awards);
            var unpriced = await CreateAsync(kernel, SubjectTypes.Task);
            await kernel.Bounties.SetNothingAsync(unpriced.Urn, null, cancellationToken: Ct);
            Assert.Empty((await kernel.Status.ChangeStatusAsync(unpriced.Urn, "Completed", Ct)).Awards);
            Assert.Null((await kernel.Rewards.GetWalletAsync(Ct)).OpenedAt);

            // The first completion that pays opens the wallet at that completion, and pays.
            var completed = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);
            var earn = Assert.Single(completed.Awards);
            var wallet = await kernel.Rewards.GetWalletAsync(Ct);
            var triggeredAt = Assert.Single(await kernel.Ledger.GetDueAsync(task.Id, Ct)).TriggeredAt;
            Assert.Equal(triggeredAt, wallet.OpenedAt);
            Assert.Equal(Default(SubjectTypes.Task)[Currencies.Xp], wallet.BalanceOf(Currencies.Xp));
            Assert.Equal(LedgerEntries.Earn, earn.Entry);

            // The pre-launch completion still never pays.
            Assert.Empty(await kernel.Rewards.ReconcileAllAsync(Ct));
            Assert.Equal(0, await LedgerEntriesForAsync(old, connectionString));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(postgres.ConnectionString);
            await admin.OpenAsync(Ct);
            await admin.ExecuteAsync(new CommandDefinition(
                $"DROP DATABASE IF EXISTS {database} WITH (FORCE);", cancellationToken: Ct));
        }
    }

    [Fact]
    public async Task A_bounty_shows_the_default_until_priced_and_clears_back_to_it()
    {
        var kernel = Build();
        var goal = await CreateAsync(kernel, SubjectTypes.Goal);

        var unpriced = Assert.Single((await kernel.Bounties.GetAsync(goal.Urn, Ct)).Lines);
        Assert.Equal(BountyTriggers.Completed, unpriced.Trigger);
        Assert.False(unpriced.IsCustom);
        Assert.Equal(Default(SubjectTypes.Goal), unpriced.Amounts);

        var priced = Assert.Single((await kernel.Bounties.SetAsync(
            goal.Urn, "Completed", new Dictionary<string, long> { ["resolve"] = 5 }, cancellationToken: Ct)).Lines);
        Assert.True(priced.IsCustom);
        Assert.Equal(5, priced.Amounts[Currencies.Resolve]);
        Assert.Equal(Default(SubjectTypes.Goal)[Currencies.Gems], priced.Amounts[Currencies.Gems]);

        var nothing = Assert.Single((await kernel.Bounties.SetNothingAsync(goal.Urn, null, cancellationToken: Ct)).Lines);
        Assert.True(nothing.IsCustom);
        Assert.Empty(nothing.Amounts);

        var cleared = Assert.Single((await kernel.Bounties.ClearAsync(goal.Urn, null, Ct)).Lines);
        Assert.False(cleared.IsCustom);
        Assert.Equal(Default(SubjectTypes.Goal), cleared.Amounts);
        Assert.Null(await kernel.Provider.GetRequiredService<ISubjectRepository>()
            .GetAttributeValueAsync(goal.Id, Bounty.AttributeKey, Ct));
    }

    [Theory]
    [InlineData("dollars", 5L, null, "Unknown currency")]
    [InlineData("xp", -1L, null, "from zero up")]
    [InlineData("xp", 5L, "followed", "doesn't pay on 'followed'")]
    public async Task A_bounty_refuses_what_it_cannot_pay(string currency, long amount, string? trigger, string message)
    {
        var kernel = Build();
        var task = await CreateAsync(kernel, SubjectTypes.Task);

        var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Bounties.SetAsync(task.Urn, trigger, new Dictionary<string, long> { [currency] = amount }, cancellationToken: Ct));

        Assert.Contains(message, refused.Message);
        Assert.Null(await kernel.Provider.GetRequiredService<ISubjectRepository>()
            .GetAttributeValueAsync(task.Id, Bounty.AttributeKey, Ct));
    }

    [Fact]
    public async Task The_reader_role_can_read_what_is_due()
    {
        await using var reader = new NpgsqlConnection(BskReader.ConnectionStringFrom(postgres.ConnectionString));
        await reader.OpenAsync(Ct);

        foreach (var sql in new[] { "SELECT count(*) FROM bsk.v_reward_trigger;", "SELECT count(*) FROM bsk.v_reward_due;" })
        {
            Assert.True(await reader.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: Ct)) >= 0);
        }
    }
}

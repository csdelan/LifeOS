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
/// REWARD-2 / D11 (rewards plan R3, migration 0027): the habit lifecycle and habit
/// earning. A followed day earns the habit's bounty and a partial one half of it; a run
/// of 7 / 30 / 100 followed days earns a milestone; establishing earns, graduates the
/// habit out of daily tracking, and moving it back to Building reverses that.
/// Corrections are newer recordings, and reconcile follows them. The database is
/// shared, so each test reasons about its own habits.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RewardHabitTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Kernel(
        ServiceProvider Provider, SubjectService Subjects, AdherenceService Adherence, StatusService Status,
        RewardService Rewards, BountyService Bounties, ILedgerRepository Ledger);

    private Kernel Build()
    {
        var provider = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();
        return new Kernel(
            provider,
            provider.GetRequiredService<SubjectService>(),
            provider.GetRequiredService<AdherenceService>(),
            provider.GetRequiredService<StatusService>(),
            provider.GetRequiredService<RewardService>(),
            provider.GetRequiredService<BountyService>(),
            provider.GetRequiredService<ILedgerRepository>());
    }

    /// <summary>A daily habit that started <paramref name="startOffset"/> days ago.</summary>
    private Task<SubjectRef> HabitAsync(
        Kernel kernel, int startOffset, bool allowsPartial = false, object? establishTarget = null)
    {
        var attributes = new Dictionary<string, object>
        {
            ["start"] = Today.AddDays(-startOffset).ToString("yyyy-MM-dd"),
            ["recurrence"] = new { freq = "daily" }
        };
        if (allowsPartial)
        {
            attributes["allows_partial"] = "true";
        }

        if (establishTarget is not null)
        {
            attributes["establish_target"] = establishTarget;
        }

        return kernel.Subjects.CreateAsync(
            SubjectTypes.Habit, $"earning habit {Guid.NewGuid():N}", JsonSerializer.Serialize(attributes),
            cancellationToken: Ct);
    }

    private Task<AdherenceResult> RecordAsync(Kernel kernel, SubjectRef habit, string result, int daysAgo = 0)
        => kernel.Adherence.RecordAsync(habit.Urn, result, Today.AddDays(-daysAgo), cancellationToken: Ct);

    /// <summary>Records followed days straight to the event store — history behind reconcile's back.</summary>
    private async Task RecordRawAsync(SubjectRef habit, string result, IEnumerable<int> daysAgo)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        foreach (var day in daysAgo)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, payload)
                VALUES ('activity', 'declared', now(), 'test', @payload::jsonb);
                """,
                new
                {
                    payload = JsonSerializer.Serialize(new
                    {
                        subject_id = habit.Id.ToString(),
                        occurrence = Today.AddDays(-day).ToString("yyyy-MM-dd"),
                        result
                    })
                },
                cancellationToken: Ct));
        }
    }

    private async Task<Dictionary<string, Dictionary<string, long>>> PaidAsync(Kernel kernel, SubjectRef habit)
        => (await kernel.Ledger.GetPaidAsync(habit.Id, Ct)).ToDictionary(
            p => p.AwardKey, p => p.Net.ToDictionary(kv => kv.Key, kv => kv.Value));

    private async Task<string?> OccurrenceStateAsync(SubjectRef habit, DateOnly date)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT state FROM bsk.v_habit_occurrence WHERE habit_id = @id AND occurrence_date = @d::date;",
            new { id = habit.Id, d = date.ToString("yyyy-MM-dd") }, cancellationToken: Ct));
    }

    private static IReadOnlyDictionary<string, long> Default(string trigger)
        => BountyDefaults.For(SubjectTypes.Habit, trigger);

    private static Dictionary<string, long> Negated(IReadOnlyDictionary<string, long> amounts)
        => amounts.ToDictionary(kv => kv.Key, kv => -kv.Value);

    [Fact]
    public async Task A_followed_day_earns_the_habit_default_citing_the_recording()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 1);

        var recorded = await RecordAsync(kernel, habit, AdherenceResults.Followed);

        var award = Assert.Single(recorded.Awards);
        Assert.Equal(LedgerEntries.Earn, award.Entry);
        Assert.Equal(BountyTriggers.Followed, award.Trigger);
        Assert.Equal(AwardKeys.Day(habit.Id, Today, AdherenceResults.Followed), award.AwardKey);
        Assert.Equal(Default(BountyTriggers.Followed), award.Amounts);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var derivedFrom = await connection.ExecuteScalarAsync<Guid[]>(new CommandDefinition(
            "SELECT derived_from FROM bsk.event WHERE id = @id;", new { id = award.EventId }, cancellationToken: Ct));
        Assert.Equal(new[] { recorded.EventId }, derivedFrom);
    }

    [Fact]
    public async Task A_partial_day_earns_half_the_followed_bounty_rounded_down()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 1, allowsPartial: true);

        var recorded = await RecordAsync(kernel, habit, AdherenceResults.Partial);

        // Followed pays 1 Coin, 5 Freedom, 5 XP: half is 0 Coins (dropped), 2 and 2.
        var award = Assert.Single(recorded.Awards);
        Assert.Equal(BountyTriggers.Partial, award.Trigger);
        Assert.Equal(new Dictionary<string, long> { [Currencies.Freedom] = 2, [Currencies.Xp] = 2 }, award.Amounts);
    }

    [Fact]
    public async Task A_missed_day_earns_nothing_and_recording_a_day_twice_pays_once()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 2);

        Assert.Empty((await RecordAsync(kernel, habit, AdherenceResults.Missed, daysAgo: 1)).Awards);
        Assert.Single((await RecordAsync(kernel, habit, AdherenceResults.Followed)).Awards);
        Assert.Empty((await RecordAsync(kernel, habit, AdherenceResults.Followed)).Awards);
    }

    [Fact]
    public async Task Correcting_a_day_moves_its_reward_to_match()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 1, allowsPartial: true);
        var followedKey = AwardKeys.Day(habit.Id, Today, AdherenceResults.Followed);
        var partialKey = AwardKeys.Day(habit.Id, Today, AdherenceResults.Partial);
        await RecordAsync(kernel, habit, AdherenceResults.Followed);

        // Followed -> partial: the day's reward is given back and the half paid.
        var toPartial = await RecordAsync(kernel, habit, AdherenceResults.Partial);
        Assert.Equal(2, toPartial.Awards.Count);
        var reversal = Assert.Single(toPartial.Awards, a => a.Entry == LedgerEntries.Reversal);
        Assert.Equal(followedKey, reversal.AwardKey);
        Assert.Equal(Negated(Default(BountyTriggers.Followed)), reversal.Amounts);
        Assert.Equal(partialKey, Assert.Single(toPartial.Awards, a => a.Entry == LedgerEntries.Earn).AwardKey);

        // Partial -> missed: the half is given back too.
        var toMissed = await RecordAsync(kernel, habit, AdherenceResults.Missed);
        Assert.Equal(partialKey, Assert.Single(toMissed.Awards).AwardKey);
        Assert.Empty(await PaidAsync(kernel, habit));

        // Missed -> followed: paid once more.
        Assert.Equal(followedKey, Assert.Single((await RecordAsync(kernel, habit, AdherenceResults.Followed)).Awards).AwardKey);
        Assert.Equal(Default(BountyTriggers.Followed), Assert.Single(await PaidAsync(kernel, habit)).Value);
    }

    [Fact]
    public async Task Seven_followed_days_in_a_row_earn_the_seven_day_milestone_once()
    {
        var kernel = Build();
        // Day -7 is scheduled and unrecorded (missed), so the run starts on day -6.
        var habit = await HabitAsync(kernel, startOffset: 7);
        var milestoneKey = AwardKeys.Streak(7, habit.Id, Today.AddDays(-6));

        for (var day = 6; day >= 1; day--)
        {
            var early = await RecordAsync(kernel, habit, AdherenceResults.Followed, day);
            Assert.DoesNotContain(early.Awards, a => a.Trigger == BountyTriggers.Streak(7));
        }

        var seventh = await RecordAsync(kernel, habit, AdherenceResults.Followed);

        var milestone = Assert.Single(seventh.Awards, a => a.Trigger == BountyTriggers.Streak(7));
        Assert.Equal(milestoneKey, milestone.AwardKey);
        Assert.Equal(Default(BountyTriggers.Streak(7)), milestone.Amounts);
        Assert.Single(seventh.Awards, a => a.Trigger == BountyTriggers.Followed);

        // The milestone cites all seven days.
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            var derivedFrom = await connection.ExecuteScalarAsync<Guid[]>(new CommandDefinition(
                "SELECT derived_from FROM bsk.event WHERE id = @id;", new { id = milestone.EventId },
                cancellationToken: Ct));
            Assert.Equal(7, derivedFrom!.Length);
            Assert.Equal(seventh.EventId, derivedFrom[^1]);
        }

        Assert.Empty(await kernel.Rewards.ReconcileAsync(habit, cancellationToken: Ct));
    }

    [Fact]
    public async Task A_correction_that_splits_a_run_reverses_its_milestone()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 7);
        var milestoneKey = AwardKeys.Streak(7, habit.Id, Today.AddDays(-6));
        await RecordRawAsync(habit, AdherenceResults.Followed, [6, 5, 4, 3, 2, 1]);
        Assert.Contains(
            (await RecordAsync(kernel, habit, AdherenceResults.Followed)).Awards,
            a => a.AwardKey == milestoneKey);

        // Day -3 was actually missed: that day and the milestone are given back.
        var split = await RecordAsync(kernel, habit, AdherenceResults.Missed, daysAgo: 3);

        Assert.All(split.Awards, a => Assert.Equal(LedgerEntries.Reversal, a.Entry));
        Assert.Contains(split.Awards, a => a.AwardKey == milestoneKey);
        Assert.Contains(split.Awards, a => a.AwardKey == AwardKeys.Day(habit.Id, Today.AddDays(-3), AdherenceResults.Followed));
        Assert.DoesNotContain(milestoneKey, (await PaidAsync(kernel, habit)).Keys);

        // Corrected back, the run is whole again and the milestone pays again.
        var healed = await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 3);
        Assert.Contains(healed.Awards, a => a.AwardKey == milestoneKey && a.Entry == LedgerEntries.Earn);
    }

    [Fact]
    public async Task Establishing_pays_and_graduates_the_habit_keeping_its_history_and_days()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 3);
        await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 1);
        Assert.Equal("unrecorded", await OccurrenceStateAsync(habit, Today));

        var established = await kernel.Status.ChangeStatusAsync(habit.Urn, "Established", Ct);

        var award = Assert.Single(established.Awards);
        Assert.Equal(AwardKeys.Established(habit.Id), award.AwardKey);
        Assert.Equal(Default(BountyTriggers.Established), award.Amounts);

        // Graduated: today is no longer scheduled; the recorded day stays, and stays paid.
        Assert.Null(await OccurrenceStateAsync(habit, Today));
        Assert.Equal(AdherenceResults.Followed, await OccurrenceStateAsync(habit, Today.AddDays(-1)));
        Assert.Contains(AwardKeys.Day(habit.Id, Today.AddDays(-1), AdherenceResults.Followed),
            (await PaidAsync(kernel, habit)).Keys);
        Assert.Empty(await kernel.Rewards.ReconcileAsync(habit, cancellationToken: Ct));
    }

    [Fact]
    public async Task Moving_an_established_habit_back_to_building_reverses_it_and_resumes_tracking()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 3);
        await kernel.Status.ChangeStatusAsync(habit.Urn, "Established", Ct);

        var building = await kernel.Status.ChangeStatusAsync(habit.Urn, "Building", Ct);

        var reversal = Assert.Single(building.Awards);
        Assert.Equal(LedgerEntries.Reversal, reversal.Entry);
        Assert.Equal(Negated(Default(BountyTriggers.Established)), reversal.Amounts);
        Assert.Equal("unrecorded", await OccurrenceStateAsync(habit, Today));
    }

    [Fact]
    public async Task Abandoning_a_habit_pays_nothing_and_keeps_what_its_days_earned()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 3);
        await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 1);

        var abandoned = await kernel.Status.ChangeStatusAsync(habit.Urn, "Abandoned", Ct);

        Assert.Empty(abandoned.Awards);
        Assert.Null(await OccurrenceStateAsync(habit, Today));
        Assert.Single(await PaidAsync(kernel, habit));
    }

    [Fact]
    public async Task Archiving_a_habit_never_takes_its_rewards_back()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 1);
        await RecordAsync(kernel, habit, AdherenceResults.Followed);

        await kernel.Provider.GetRequiredService<ArchiveService>().ArchiveAsync(habit.Urn, Ct);

        Assert.Empty(await kernel.Rewards.ReconcileAsync(habit, cancellationToken: Ct));
        Assert.Single(await PaidAsync(kernel, habit));
    }

    [Fact]
    public async Task A_day_recorded_before_the_wallet_opened_never_pays()
    {
        var kernel = Build();
        await kernel.Rewards.OpenAsync(cancellationToken: Ct);
        var habit = await HabitAsync(kernel, startOffset: 1);
        var backdated = new AdherenceService(
            kernel.Subjects,
            kernel.Provider.GetRequiredService<ISubjectRepository>(),
            kernel.Provider.GetRequiredService<IActivityWriter>(),
            kernel.Rewards,
            new TimerTestClock(new DateTimeOffset(2000, 6, 1, 12, 0, 0, TimeSpan.Zero)),
            KernelSources.Cli);

        var recorded = await backdated.RecordAsync(habit.Urn, AdherenceResults.Followed, Today, cancellationToken: Ct);

        Assert.Empty(recorded.Awards);
        Assert.Empty(await PaidAsync(kernel, habit));
    }

    [Fact]
    public async Task A_habit_is_priced_per_trigger_and_partial_follows_followed()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 1, allowsPartial: true);

        var view = await kernel.Bounties.GetAsync(habit.Urn, Ct);
        Assert.Equal(
            new[] { "followed", "streak_7", "streak_30", "streak_100", "established" },
            view.Lines.Select(l => l.Trigger));
        Assert.All(view.Lines, l => Assert.False(l.IsCustom));

        var needsTrigger = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Bounties.SetAsync(habit.Urn, null, new Dictionary<string, long> { ["xp"] = 9 }, cancellationToken: Ct));
        Assert.Contains("Say which trigger", needsTrigger.Message);
        var partial = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Bounties.SetAsync(habit.Urn, "partial", new Dictionary<string, long> { ["xp"] = 9 }, cancellationToken: Ct));
        Assert.Contains("half of followed", partial.Message);

        await kernel.Bounties.SetAsync(
            habit.Urn, "followed", new Dictionary<string, long> { ["xp"] = 9, ["coins"] = 0, ["freedom"] = 0, ["karma"] = 1 }, cancellationToken: Ct);

        var recorded = await RecordAsync(kernel, habit, AdherenceResults.Partial);
        Assert.Equal(new Dictionary<string, long> { [Currencies.Xp] = 4 }, Assert.Single(recorded.Awards).Amounts);
    }

    private async Task<(bool Met, decimal? Adherence, long Concluded)?> EstablishmentAsync(SubjectRef habit)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleOrDefaultAsync<(bool, decimal?, long)?>(new CommandDefinition(
            "SELECT target_met, adherence, concluded FROM bsk.v_habit_establishment WHERE habit_id = @id;",
            new { id = habit.Id }, cancellationToken: Ct));
        return row;
    }

    [Fact]
    public async Task A_habit_meeting_its_target_over_the_window_is_ready_to_establish()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 60);
        // 50 followed of the 59 concluded days in the window (today is still open): 85%.
        await RecordRawAsync(habit, AdherenceResults.Followed, Enumerable.Range(10, 50));

        var ready = await EstablishmentAsync(habit);

        Assert.NotNull(ready);
        Assert.True(ready.Value.Met);
        Assert.Equal(59, ready.Value.Concluded);
        Assert.Equal(Math.Round(50m / 59m, 4), ready.Value.Adherence);

        // Confirming is an ordinary status change; an established habit is no longer listed.
        await kernel.Status.ChangeStatusAsync(habit.Urn, "Established", Ct);
        Assert.Null(await EstablishmentAsync(habit));
    }

    [Fact]
    public async Task A_habit_younger_than_its_window_is_not_ready_however_well_it_goes()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 20);
        await RecordRawAsync(habit, AdherenceResults.Followed, Enumerable.Range(1, 20));

        var young = await EstablishmentAsync(habit);

        Assert.NotNull(young);
        Assert.Equal(1m, young.Value.Adherence);
        Assert.False(young.Value.Met);
    }

    [Fact]
    public async Task A_custom_target_counts_partial_days_as_half()
    {
        var kernel = Build();
        // 90% (written as a percentage) over 10 days.
        var habit = await HabitAsync(kernel, startOffset: 10, allowsPartial: true,
            establishTarget: new { min_adherence = 90, window_days = 10 });
        await RecordRawAsync(habit, AdherenceResults.Followed, [9, 8, 7, 6, 5, 4, 3, 2]);
        await RecordRawAsync(habit, AdherenceResults.Partial, [1]);

        var ready = await EstablishmentAsync(habit);

        Assert.NotNull(ready);
        Assert.Equal(9, ready.Value.Concluded);
        Assert.Equal(Math.Round(8.5m / 9m, 4), ready.Value.Adherence);
        Assert.True(ready.Value.Met);
    }

    [Fact]
    public async Task The_reader_role_can_read_the_habit_lifecycle_views()
    {
        await using var reader = new NpgsqlConnection(BskReader.ConnectionStringFrom(postgres.ConnectionString));
        await reader.OpenAsync(Ct);

        foreach (var sql in new[]
                 {
                     "SELECT count(*) FROM bsk.v_habit_establishment;",
                     "SELECT count(*) FROM bsk.v_habit_occurrence;",
                     "SELECT count(*) FROM bsk.v_reward_due;"
                 })
        {
            Assert.True(await reader.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: Ct)) >= 0);
        }
    }
}

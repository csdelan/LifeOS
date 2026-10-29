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
/// REWARD-6 / REWARD-7, D12 (xp-domains plan X3, migration 0032): each life domain's daily
/// credit — <c>bsk.v_award_credit</c>, <c>ILedgerRepository.GetDomainDaysAsync</c> and
/// <c>DomainService</c>. A day holds what the ledger pays now for the awards credited to it,
/// so a reversal takes its credit away and earning again files it on the new day.
/// <para>
/// The database is shared and no other test pays Environment XP, so these tests price their
/// items in it and compare before and after. The one test that writes a ledger entry by hand
/// rolls it back.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DomainCreditTests(PostgresFixture postgres)
{
    // UTC+14 and UTC−11: at any instant their dates are at least a day apart.
    private const string Kiritimati = "Pacific/Kiritimati";
    private const string PagoPago = "Pacific/Pago_Pago";

    private static readonly DateOnly TodayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Kernel(
        SubjectService Subjects, StatusService Status, AdherenceService Adherence, BountyService Bounties,
        ILedgerRepository Ledger, DomainService Domains);

    private Kernel Build(string homeTimeZone = "UTC")
    {
        var provider = new ServiceCollection()
            .AddLifeOsKernel(postgres.ConnectionString, homeTimeZone: homeTimeZone)
            .BuildServiceProvider();
        return new Kernel(
            provider.GetRequiredService<SubjectService>(),
            provider.GetRequiredService<StatusService>(),
            provider.GetRequiredService<AdherenceService>(),
            provider.GetRequiredService<BountyService>(),
            provider.GetRequiredService<ILedgerRepository>(),
            provider.GetRequiredService<DomainService>());
    }

    private static Dictionary<string, long> Environment(long amount) => new() { [Currencies.EnvironmentXp] = amount };

    private async Task<SubjectRef> PricedTaskAsync(Kernel kernel, long xp, string currency = Currencies.EnvironmentXp)
    {
        var task = await kernel.Subjects.CreateAsync(
            SubjectTypes.Task, $"credit task {Guid.NewGuid():N}", "{}", cancellationToken: Ct);
        await kernel.Bounties.SetAsync(task.Urn, null, new Dictionary<string, long> { [currency] = xp }, cancellationToken: Ct);
        return task;
    }

    /// <summary>Environment XP per credited day, as the repository totals it.</summary>
    private async Task<Dictionary<DateOnly, (long Base, long Paid)>> EnvironmentDaysAsync(Kernel kernel)
        => (await kernel.Ledger.GetDomainDaysAsync([Currencies.EnvironmentXp], Ct)).Days
            .ToDictionary(d => d.Day, d => (d.Base, d.Paid));

    private static long PaidOn(Dictionary<DateOnly, (long Base, long Paid)> days, DateOnly day)
        => days.TryGetValue(day, out var credit) ? credit.Paid : 0;

    private sealed record CreditRow(string Currency, long Base, long Paid, string? CreditedOn, Guid EventId);

    /// <summary>The view's rows for one award key, read on <paramref name="connection"/> (and its transaction).</summary>
    private async Task<List<CreditRow>> CreditRowsAsync(
        string awardKey, NpgsqlConnection? connection = null, NpgsqlTransaction? transaction = null)
    {
        var owned = connection is null;
        connection ??= new NpgsqlConnection(postgres.ConnectionString);
        try
        {
            if (owned)
            {
                await connection.OpenAsync(Ct);
            }

            return (await connection.QueryAsync<CreditRow>(new CommandDefinition(
                """
                SELECT currency AS Currency, base AS Base, paid AS Paid,
                       to_char(credited_on, 'YYYY-MM-DD') AS CreditedOn, event_id AS EventId
                FROM bsk.v_award_credit WHERE award_key = @awardKey ORDER BY currency;
                """,
                new { awardKey }, transaction, cancellationToken: Ct))).ToList();
        }
        finally
        {
            if (owned)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private async Task<DateOnly> CreditedOnAsync(Guid ledgerEventId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var day = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT payload->>'credited_on' FROM bsk.event WHERE id = @id;", new { id = ledgerEventId }, cancellationToken: Ct));
        return DateOnly.ParseExact(day!, "yyyy-MM-dd");
    }

    [Fact]
    public async Task A_reversal_takes_a_days_credit_away_and_completing_again_files_it_on_the_new_day()
    {
        // Completed where it's already tomorrow, reopened, then completed again where it's
        // still yesterday: the same award, earned on two different days.
        var ahead = Build(Kiritimati);
        var behind = Build(PagoPago);
        var task = await PricedTaskAsync(ahead, xp: 7);
        var key = AwardKeys.Completed(task.Id);
        var before = await EnvironmentDaysAsync(ahead);

        var first = Assert.Single(
            (await ahead.Status.ChangeStatusAsync(task.Urn, "Completed", Ct)).Awards);
        var firstDay = await CreditedOnAsync(first.EventId);
        Assert.Equal(PaidOn(before, firstDay) + 7, PaidOn(await EnvironmentDaysAsync(ahead), firstDay));

        Assert.Equal(LedgerEntries.Reversal, Assert.Single(
            (await ahead.Status.ChangeStatusAsync(task.Urn, "In progress", Ct)).Awards).Entry);
        Assert.DoesNotContain(await CreditRowsAsync(key), r => r.Currency == Currencies.EnvironmentXp);
        Assert.Equal(PaidOn(before, firstDay), PaidOn(await EnvironmentDaysAsync(ahead), firstDay));

        var second = Assert.Single(
            (await behind.Status.ChangeStatusAsync(task.Urn, "Completed", Ct)).Awards);
        var secondDay = await CreditedOnAsync(second.EventId);
        Assert.True(secondDay < firstDay, $"{secondDay} should be before {firstDay}");

        var after = await EnvironmentDaysAsync(ahead);
        Assert.Equal(PaidOn(before, firstDay), PaidOn(after, firstDay));
        Assert.Equal(PaidOn(before, secondDay) + 7, PaidOn(after, secondDay));
        var row = Assert.Single(await CreditRowsAsync(key), r => r.Currency == Currencies.EnvironmentXp);
        Assert.Equal((7L, 7L, secondDay.ToString("yyyy-MM-dd"), second.EventId), (row.Base, row.Paid, row.CreditedOn, row.EventId));
    }

    [Fact]
    public async Task A_habit_day_logged_late_is_credited_to_its_own_day_until_its_undone()
    {
        var kernel = Build();
        var habit = await kernel.Subjects.CreateAsync(
            SubjectTypes.Habit, $"credit habit {Guid.NewGuid():N}",
            JsonSerializer.Serialize(new { start = TodayUtc.AddDays(-4).ToString("yyyy-MM-dd"), recurrence = new { freq = "daily" } }),
            cancellationToken: Ct);
        // A new habit is Planned; tracking it lets its days be recorded.
        await kernel.Status.ChangeStatusAsync(habit.Urn, "Building", Ct);
        await kernel.Bounties.SetAsync(habit.Urn, BountyTriggers.Followed, Environment(9), cancellationToken: Ct);
        var (threeAgo, twoAgo) = (TodayUtc.AddDays(-3), TodayUtc.AddDays(-2));
        var before = await EnvironmentDaysAsync(kernel);

        await kernel.Adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, threeAgo, cancellationToken: Ct);
        var logged = await EnvironmentDaysAsync(kernel);
        Assert.Equal(PaidOn(before, threeAgo) + 9, PaidOn(logged, threeAgo));
        Assert.Equal(PaidOn(before, TodayUtc), PaidOn(logged, TodayUtc));

        // Marked missed after all: the day loses its credit. Followed the next day instead.
        await kernel.Adherence.RecordAsync(habit.Urn, AdherenceResults.Missed, threeAgo, cancellationToken: Ct);
        await kernel.Adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, twoAgo, cancellationToken: Ct);
        var after = await EnvironmentDaysAsync(kernel);
        Assert.Equal(PaidOn(before, threeAgo), PaidOn(after, threeAgo));
        Assert.Equal(PaidOn(before, twoAgo) + 9, PaidOn(after, twoAgo));
    }

    [Fact]
    public async Task Base_is_what_an_earn_paid_before_any_multiplier()
    {
        // Phase X5 stamps `base` on an earn a multiplier raised. Written by hand here, and
        // rolled back, since no flow writes one yet.
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        var (multiplied, malformed) = ($"test:{Guid.NewGuid():N}", $"test:{Guid.NewGuid():N}");
        const string insert = """
            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, payload)
            VALUES ('ledger', 'declared', now(), now(), @source, @payload::jsonb);
            """;
        foreach (var payload in new object[]
                 {
                     new
                     {
                         entry = "earn", award_key = multiplied, credited_on = "2003-04-01",
                         amounts = new Dictionary<string, long> { [Currencies.EnvironmentXp] = 13, [Currencies.Xp] = 5 },
                         @base = new Dictionary<string, long> { [Currencies.EnvironmentXp] = 10 }
                     },
                     new
                     {
                         entry = "earn", award_key = malformed, credited_on = "not a date",
                         amounts = new Dictionary<string, long> { [Currencies.EnvironmentXp] = 4 },
                         @base = "ten"
                     }
                 })
        {
            await connection.ExecuteAsync(new CommandDefinition(
                insert, new { source = KernelSources.Cli, payload = JsonSerializer.Serialize(payload) }, transaction,
                cancellationToken: Ct));
        }

        var rows = await CreditRowsAsync(multiplied, connection, transaction);
        var odd = Assert.Single(await CreditRowsAsync(malformed, connection, transaction));
        await transaction.RollbackAsync(Ct);

        // Base where the earn says it; otherwise what it paid.
        Assert.Equal(
            [(Currencies.EnvironmentXp, 10L, 13L, "2003-04-01"), (Currencies.Xp, 5L, 5L, "2003-04-01")],
            rows.Select(r => (r.Currency, r.Base, r.Paid, r.CreditedOn)));
        // A base that isn't an object reads as absent; a bad date as none (the reader then
        // files it on the earn's own date).
        Assert.Equal((4L, 4L, (string?)null), (odd.Base, odd.Paid, odd.CreditedOn));
        Assert.Empty(await CreditRowsAsync(multiplied));
    }

    [Fact]
    public async Task Domains_without_rules_report_their_xp_and_nothing_else()
    {
        var kernel = Build();
        var before = await kernel.Domains.GetAsync(Ct);
        var task = await PricedTaskAsync(kernel, xp: 12, Currencies.SocialXp);

        await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);
        var after = await kernel.Domains.GetAsync(Ct);

        Assert.Equal(TodayUtc, after.Today);
        Assert.Equal(Currencies.LifeDomains, after.Domains.Select(d => d.Currency));
        var social = after.Domains.Single(d => d.Currency.Key == Currencies.SocialXp);
        Assert.False(social.Configured);
        Assert.Null(social.Progress);
        Assert.Equal(before.Domains.Single(d => d.Currency.Key == Currencies.SocialXp).TotalXp + 12, social.TotalXp);
        Assert.All(after.Domains.Where(d => LifeDomains.For(d.Currency.Key) is null), d => Assert.Null(d.Progress));

        var health = after.Domains.Single(d => d.Currency.Key == Currencies.HealthXp);
        Assert.True(health.Configured);
        Assert.Same(LifeDomains.Health, health.Progress!.Rules);
        Assert.Equal(health.Progress.TotalXp, health.TotalXp);

        // Day by day: no success test, so no day is active or not.
        var series = await kernel.Domains.GetDaysAsync("social", 3, Ct);
        Assert.Equal([TodayUtc.AddDays(-2), TodayUtc.AddDays(-1), TodayUtc], series.Days.Select(d => d.Day));
        Assert.All(series.Days, d => Assert.Null(d.Active));
        Assert.True(series.Days[^1].Paid >= 12);
    }

    [Fact]
    public async Task A_configured_domains_series_says_which_days_were_active()
    {
        var kernel = Build();

        var series = await kernel.Domains.GetDaysAsync("Health XP", 5, Ct);

        Assert.Equal(5, series.Days.Count);
        Assert.Equal(TodayUtc, series.Days[^1].Day);
        Assert.All(series.Days, d => Assert.Equal(d.Exception is null && d.Action >= d.Standing!.ActiveMinimum, d.Active));
        await Assert.ThrowsAsync<ArgumentException>(() => kernel.Domains.GetDaysAsync("health", 0, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => kernel.Domains.GetDaysAsync("coins", 3, Ct));
    }

    [Fact]
    public async Task Today_is_the_date_in_the_home_zone()
    {
        var aheadToday = (await Build(Kiritimati).Ledger.GetDomainDaysAsync([Currencies.HealthXp], Ct)).Today;
        var behindToday = (await Build(PagoPago).Ledger.GetDomainDaysAsync([Currencies.HealthXp], Ct)).Today;

        Assert.InRange(aheadToday.DayNumber - behindToday.DayNumber, 1, 2);
    }
}

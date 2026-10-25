using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Cli.Commands;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-8 / D17 (xp-domains plan X9, §7.4.2–§7.4.3, migration 0043): exception days and the
/// fold's other new inputs. A marked day is a <c>note</c> event read through
/// <c>bsk.v_exception_day</c>, newest version per day, voided to unmark; the domain credit
/// carries those days, each day's passive (sleep) base XP, and the last settled day. The fold
/// ignores them until X10. Each test runs in a database of its own
/// (<see cref="DomainTestDatabase"/>), since marked days would reach every domain read in a
/// shared one.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExceptionDayTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_day_is_marked_its_comment_changed_unmarked_and_marked_again()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<ExceptionDayService>();
        var today = await db.TodayAsync();
        var day = today.AddDays(-3);

        var marked = await service.MarkAsync(day, "  Flu, in bed  ", Ct);
        Assert.Equal((day, "Flu, in bed"), (marked.Exception!.Day, marked.Exception.Comment));
        Assert.Empty(marked.Earned.Awards);
        Assert.Empty(marked.Earned.Progress);

        // The same comment again writes nothing; a new one writes a newer version that wins.
        var events = await ExceptionEventCountAsync(db);
        Assert.Same(Earned.Nothing, (await service.MarkAsync(day, "Flu, in bed", Ct)).Earned);
        Assert.Equal(events, await ExceptionEventCountAsync(db));

        var changed = await service.MarkAsync(day, "Flu, then a migraine", Ct);
        Assert.Equal("Flu, then a migraine", changed.Exception!.Comment);
        Assert.Equal(events + 1, await ExceptionEventCountAsync(db));
        Assert.Equal(["Flu, then a migraine"], (await service.ListAsync(cancellationToken: Ct)).Marked.Select(d => d.Comment));

        // Unmarking voids it: the view drops it, and so does the credit the fold reads.
        var unmarked = await service.UnmarkAsync(day, Ct);
        Assert.Null(unmarked.Exception);
        Assert.Empty((await service.ListAsync(cancellationToken: Ct)).Marked);
        Assert.Empty((await Credit(db)).ExceptionDays);

        // Unmarking a day that isn't marked writes nothing.
        events = await ExceptionEventCountAsync(db);
        Assert.Same(Earned.Nothing, (await service.UnmarkAsync(day, Ct)).Earned);
        Assert.Equal(events, await ExceptionEventCountAsync(db));

        // Marking it again brings it back with the new comment.
        await service.MarkAsync(day, "Travel day", Ct);
        var credit = await Credit(db);
        var exception = Assert.Single(credit.ExceptionDays);
        Assert.Equal((day, "Travel day"), (exception.Day, exception.Comment));
    }

    [Fact]
    public async Task No_day_means_today_and_a_blank_comment_or_a_future_day_is_refused_with_nothing_written()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<ExceptionDayService>();
        var today = await db.TodayAsync();

        var marked = await service.MarkAsync(null, "Sick", Ct);
        Assert.Equal(today, marked.Exception!.Day);

        var events = await ExceptionEventCountAsync(db);
        foreach (var blank in new string?[] { null, "", "   " })
        {
            var refused = await Assert.ThrowsAsync<ArgumentException>(() => service.MarkAsync(today.AddDays(-1), blank, Ct));
            Assert.Contains("comment is required", refused.Message);
        }

        var future = await Assert.ThrowsAsync<ArgumentException>(() => service.MarkAsync(today.AddDays(1), "Planned trip", Ct));
        Assert.Contains("today or earlier", future.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => service.MarkAsync(today.AddDays(-1), "two\nlines", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.MarkAsync(today.AddDays(-1), new string('x', 141), Ct));
        Assert.Equal(events, await ExceptionEventCountAsync(db));

        // 140 characters is fine.
        Assert.NotNull((await service.MarkAsync(today.AddDays(-1), new string('x', 140), Ct)).Exception);
    }

    [Fact]
    public async Task The_list_reads_the_last_n_days_newest_first()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<ExceptionDayService>();
        var today = await db.TodayAsync();
        foreach (var ago in new[] { 100, 40, 2, 0 })
        {
            await service.MarkAsync(today.AddDays(-ago), $"{ago} days ago", Ct);
        }

        var list = await service.ListAsync(cancellationToken: Ct);
        Assert.Equal((today, 90), (list.Today, list.Days));
        Assert.Equal([today, today.AddDays(-2), today.AddDays(-40)], list.Marked.Select(d => d.Day));
        Assert.Equal([today.AddDays(-2), today], (await service.ListAsync(3, Ct)).Marked.Select(d => d.Day).Reverse());
        Assert.Equal(4, (await service.ListAsync(731, Ct)).Marked.Count);

        // The credit carries every marked day, oldest first, whatever its age.
        Assert.Equal(
            [today.AddDays(-100), today.AddDays(-40), today.AddDays(-2), today],
            (await Credit(db)).ExceptionDays.Select(d => d.Day));

        foreach (var bad in new[] { 0, 732 })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(bad, Ct));
        }
    }

    [Fact]
    public async Task Sleep_xp_is_split_out_as_passive_and_a_marked_day_stops_counting()
    {
        // Two and three days back, so both have settled whatever the hour.
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        var day = today.AddDays(-2);
        await CreditKeyedAsync(db, ActivityAwards.SleepKey(day), ActivityAwards.SleepTrigger, day, 15);
        await CreditKeyedAsync(db, ActivityAwards.StepsKey(day), ActivityAwards.StepsTrigger, day, 25);
        await db.CreditAsync(day, 30);
        await db.CreditAsync(today.AddDays(-3), 70);

        var before = DomainService.Fold(LifeDomains.Health, await Credit(db));
        var credit = await Credit(db);
        var sick = credit.Days.Single(d => d.Day == day);
        Assert.Equal((70L, 15L), (sick.Base, sick.Passive));
        Assert.Equal(0, credit.Days.Single(d => d.Day == today.AddDays(-3)).Passive);

        var domainDay = new DomainDay(sick.Day, sick.Base, sick.Paid, sick.Passive);
        Assert.Equal(55, domainDay.Action);
        Assert.True(before.StandingOn(day).Counted);
        Assert.True(before.ActiveOn(day));

        // Marked, the day no longer counts toward consistency or the multiplier, and the last
        // 90 days reach back past it. Its XP still counts toward lifetime.
        await db.Get<ExceptionDayService>().MarkAsync(day, "Sick", Ct);
        var after = DomainService.Fold(LifeDomains.Health, await Credit(db));
        var marked = after.StandingOn(day);
        Assert.Equal((true, false, false), (marked.Exception, marked.Counted, marked.Active));
        Assert.Contains(day, after.ExceptionDays);
        Assert.Equal(before.Window.Days - 1, after.Window.Days);
        Assert.Equal(before.TotalXp, after.TotalXp);
    }

    [Fact]
    public async Task The_last_settled_day_turns_over_at_4am_in_the_home_zone()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);

        // Fixed-offset zones from UTC−11 to UTC+14 put the local time on both sides of 4 a.m.
        // (Etc/GMT+5 is UTC−5: the sign is inverted.) Expected is worked out either side of the
        // read, so a turning hour can't make it flaky.
        for (var offset = -11; offset <= 14; offset++)
        {
            var zone = offset == 0 ? "Etc/GMT" : $"Etc/GMT{(offset > 0 ? "-" : "+")}{Math.Abs(offset)}";
            var early = Expected(DateTimeOffset.UtcNow, offset);
            var credit = await new NpgsqlLedgerRepository(db.ConnectionString, zone).GetDomainDaysAsync([Currencies.HealthXp], Ct);
            var late = Expected(DateTimeOffset.UtcNow, offset);
            Assert.True(credit.SettledThrough == early || credit.SettledThrough == late,
                $"{zone}: settled through {credit.SettledThrough}, expected {early}");
            Assert.InRange(credit.Today.DayNumber - credit.SettledThrough.DayNumber, 1, 2);
        }

        static DateOnly Expected(DateTimeOffset now, int offsetHours)
        {
            var local = now.UtcDateTime.AddHours(offsetHours);
            return DateOnly.FromDateTime(local.AddHours(-LifeDomains.SettleHour)).AddDays(-1);
        }
    }

    [Fact]
    public async Task The_endpoints_mark_list_and_unmark_and_refuse_in_words()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var marked = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/domains/exceptions", new { day = Day(today.AddDays(-1)), comment = "Food poisoning" }, Ct));
        Assert.Equal((Day(today.AddDays(-1)), "Food poisoning"),
            (marked.GetProperty("exception").GetProperty("day").GetString(),
                marked.GetProperty("exception").GetProperty("comment").GetString()));
        Assert.Equal(0, marked.GetProperty("awards").GetArrayLength());
        Assert.Equal(0, marked.GetProperty("progress").GetArrayLength());

        var todayMarked = await JsonAsync(await host.Client.PostAsJsonAsync("/api/domains/exceptions", new { comment = "Still ill" }, Ct));
        Assert.Equal(Day(today), todayMarked.GetProperty("exception").GetProperty("day").GetString());

        var list = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains/exceptions", Ct);
        Assert.Equal((Day(today), Day(today.AddDays(-89))), (list.GetProperty("today").GetString(), list.GetProperty("from").GetString()));
        Assert.Equal([Day(today), Day(today.AddDays(-1))],
            list.GetProperty("exceptions").EnumerateArray().Select(e => e.GetProperty("day").GetString()));
        var week = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains/exceptions?days=1", Ct);
        Assert.Single(week.GetProperty("exceptions").EnumerateArray());

        var unmarked = await JsonAsync(await host.Client.PostAsync($"/api/domains/exceptions/{Day(today.AddDays(-1))}/unmark", null, Ct));
        Assert.Equal(JsonValueKind.Null, unmarked.GetProperty("exception").ValueKind);
        var after = await host.Client.GetFromJsonAsync<JsonElement>("/api/domains/exceptions", Ct);
        Assert.Equal([Day(today)], after.GetProperty("exceptions").EnumerateArray().Select(e => e.GetProperty("day").GetString()));

        foreach (var (body, words) in new (object Body, string Words)[]
                 {
                     (new { day = Day(today.AddDays(1)), comment = "Trip" }, "today or earlier"),
                     (new { day = Day(today), comment = "  " }, "comment is required"),
                     (new { day = "25/10/2026", comment = "Trip" }, "yyyy-MM-dd")
                 })
        {
            var response = await host.Client.PostAsJsonAsync("/api/domains/exceptions", body, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(words, await response.Content.ReadAsStringAsync(Ct));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsync("/api/domains/exceptions/yesterday/unmark", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/domains/exceptions?days=0", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/domains/exceptions?days=732", Ct)).StatusCode);
    }

    [Fact]
    public void The_cli_takes_a_date_today_or_yesterday()
    {
        var today = new DateOnly(2026, 10, 25);
        Assert.Equal(today, DomainsExceptionCommand.ParseDay("today", today));
        Assert.Equal(today.AddDays(-1), DomainsExceptionCommand.ParseDay(" Yesterday ", today));
        Assert.Equal(new DateOnly(2026, 10, 1), DomainsExceptionCommand.ParseDay("2026-10-01", today));
        Assert.Throws<ArgumentException>(() => DomainsExceptionCommand.ParseDay("last tuesday", today));
    }

    private Task<DomainCredit> Credit(DomainTestDatabase db)
        => db.Get<ILedgerRepository>().GetDomainDaysAsync([Currencies.HealthXp], Ct);

    private async Task<long> ExceptionEventCountAsync(DomainTestDatabase db)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'note' AND jsonb_exists(payload, 'exception_day');",
            cancellationToken: Ct));
    }

    /// <summary>Health XP credited to <paramref name="day"/> under an award key and trigger of the test's choosing.</summary>
    private async Task CreditKeyedAsync(DomainTestDatabase db, string key, string trigger, DateOnly day, long xp)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            entry = LedgerEntries.Earn,
            amounts = new Dictionary<string, long> { [Currencies.HealthXp] = xp },
            award_key = key,
            trigger,
            credited_on = Day(day)
        });
        await db.Get<ILedgerRepository>().AppendAsync(
            [new LedgerWrite(
                new NewEvent(EventKinds.Ledger, Provenances.Declared, now, now, KernelSources.Cli, PayloadJson: payload),
                [], GuardBalance: false, AwardKey: key, ExpectPaid: false)],
            Ct);
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

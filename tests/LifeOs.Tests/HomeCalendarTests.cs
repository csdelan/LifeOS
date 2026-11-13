using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// A habit check-off without a date lands on today in the home zone
/// (<c>Rewards:HomeTimeZone</c>), never the server's: at 9 pm Pacific the UTC date is already
/// tomorrow. <see cref="IHomeCalendar"/> answers what day it is there; <c>bsk adhere</c> and
/// <c>POST /api/adhere</c> leave an omitted date to it.
/// <para>
/// UTC+14 (Kiritimati) and UTC−11 (Pago Pago) are at least a day apart at any instant, so
/// whichever zone a check-off used shows in its date whenever the test runs.
/// </para>
/// </summary>
public sealed class HomeCalendarTests(PostgresFixture postgres, ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private const string Kiritimati = "Pacific/Kiritimati";
    private const string PagoPago = "Pacific/Pago_Pago";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Kernel(string homeTimeZone)
        => new ServiceCollection()
            .AddLifeOsKernel(classDb.ConnectionString, homeTimeZone: homeTimeZone)
            .BuildServiceProvider();

    /// <summary>A database whose home zone is <paramref name="zone"/>, with a kernel in the same zone.</summary>
    private Task<DomainTestDatabase> DatabaseAsync(string zone) => DomainTestDatabase.CreateAsync(postgres, Ct, zone);

    private Task<SubjectRef> HabitAsync(ServiceProvider kernel)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
        return kernel.CreateTrackedHabitAsync(
            $"calendar habit {Guid.NewGuid():N}",
            JsonSerializer.Serialize(new { start = start.ToString("yyyy-MM-dd"), recurrence = new { freq = "daily" } }),
            Ct);
    }

    /// <summary>Today in <paramref name="kernel"/>'s home zone, read before and after <paramref name="act"/>.</summary>
    private static async Task<(T Result, DateOnly[] Today)> AroundAsync<T>(
        ServiceProvider kernel, Func<Task<T>> act, CancellationToken ct)
    {
        var calendar = kernel.GetRequiredService<IHomeCalendar>();
        var before = await calendar.DateAtAsync(DateTimeOffset.UtcNow, ct);
        var result = await act();
        var after = await calendar.DateAtAsync(DateTimeOffset.UtcNow, ct);
        return (result, [before, after]);
    }

    [Theory]
    [InlineData(HomeZone.Default, "2003-02-14")]
    [InlineData("UTC", "2003-02-15")]
    [InlineData(Kiritimati, "2003-02-15")]
    [InlineData(PagoPago, "2003-02-14")]
    public async Task The_calendar_reads_an_instant_on_the_home_zones_date(string zone, string expected)
    {
        await using var db = await DatabaseAsync(zone);

        // 06:30 UTC on 15 February 2003: 10:30 pm on the 14th in Pacific time.
        var day = await db.Get<IHomeCalendar>()
            .DateAtAsync(new DateTimeOffset(2003, 2, 15, 6, 30, 0, TimeSpan.Zero), Ct);

        Assert.Equal(DateOnly.Parse(expected), day);
    }

    // Code review CR42: every service asks the calendar what day it is, and the calendar reads the
    // kernel's clock, so a test clock moves "today" for the food log as for the rest.
    [Fact]
    public async Task Today_is_the_date_at_the_kernels_clock_for_every_service()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 1, 15, 6, 30, 0, TimeSpan.Zero));
        await using var kernel = new ServiceCollection()
            .AddLifeOsKernel(classDb.ConnectionString, homeTimeZone: PostgresFixture.HabitViewZone)
            .AddSingleton<IClock>(clock)
            .BuildServiceProvider();

        var day = new DateOnly(2026, 1, 15);
        Assert.Equal(day, await kernel.GetRequiredService<IHomeCalendar>().TodayAsync(Ct));
        Assert.Equal(day, await kernel.GetRequiredService<LifeOs.Application.Food.FoodReads>().TodayAsync(Ct));
        Assert.Equal(day, await kernel.GetRequiredService<LifeOs.Application.Food.FoodLogService>().TodayAsync(Ct));
        Assert.Equal(day, await kernel.GetRequiredService<LifeOs.Application.Reviews.ReviewService>().TodayAsync(Ct));
    }

    // Code review CR62: the domain fold's today, its last settled day and the exception-day list
    // read the kernel's clock too, not the database's now(), so a day marked by the service is
    // found by the list it reads back.
    [Fact]
    public async Task The_domain_fold_and_exception_days_read_the_kernels_clock()
    {
        await using var db = await DatabaseAsync(DomainTestDatabase.Zone);
        // 03:30 UTC on Jan 15: before the 4 a.m. settle, so Jan 13 is the last settled day.
        var clock = new TestClock(new DateTimeOffset(2026, 1, 15, 3, 30, 0, TimeSpan.Zero));
        await using var kernel = new ServiceCollection()
            .AddLifeOsKernel(db.ConnectionString, homeTimeZone: DomainTestDatabase.Zone)
            .AddSingleton<IClock>(clock)
            .BuildServiceProvider();

        var credit = await kernel.GetRequiredService<IDomainCreditReader>().GetDomainDaysAsync([Currencies.HealthXp], Ct);
        Assert.Equal((new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 13)), (credit.Today, credit.SettledThrough));

        var exceptions = kernel.GetRequiredService<LifeOs.Application.Rewards.ExceptionDayService>();
        var marked = await exceptions.MarkAsync(null, "Flu", Ct);
        Assert.Equal(new DateOnly(2026, 1, 15), marked.Exception?.Day);
        var listed = await exceptions.ListAsync(cancellationToken: Ct);
        Assert.Equal(new DateOnly(2026, 1, 15), listed.Today);
        Assert.Equal([new DateOnly(2026, 1, 15)], listed.Marked.Select(d => d.Day));
    }

    // Code review CR62: at 8 pm Pacific the UTC date is already tomorrow. Renaming a series then
    // renames today's occurrence too, since "upcoming" counts from today at home.
    [Fact]
    public async Task Renaming_an_appointment_series_in_the_evening_renames_todays_occurrence()
    {
        const string pacific = "America/Los_Angeles";
        await using var db = await DatabaseAsync(pacific);
        // 8 pm on Jan 15 in Los Angeles is 04:00 UTC on Jan 16.
        var clock = new TestClock(new DateTimeOffset(2026, 1, 16, 4, 0, 0, TimeSpan.Zero));
        await using var kernel = new ServiceCollection()
            .AddLifeOsKernel(db.ConnectionString, homeTimeZone: pacific)
            .AddSingleton<IClock>(clock)
            .BuildServiceProvider();
        var subjects = kernel.GetRequiredService<SubjectService>();

        var series = await subjects.CreateAsync(
            SubjectTypes.Appointment, "Evening class",
            attributesJson: """{"start_date":"2026-01-14","start":"21:00","recurrence":{"freq":"daily"}}""",
            cancellationToken: Ct);
        await kernel.GetRequiredService<AppointmentService>().MaterializeAsync(series.Urn, new DateOnly(2026, 1, 16), Ct);

        var renamed = await kernel.GetRequiredService<RenameService>().RenameAsync(series.Urn, "Night class", Ct);

        Assert.Equal(2, renamed.OccurrencesRenamed);
        await using var c = new NpgsqlConnection(db.ConnectionString);
        var titles = (await c.QueryAsync<(string Date, string Title)>(new CommandDefinition(
            "SELECT date, title FROM bsk.v_appointment WHERE series_urn = @s ORDER BY date;",
            new { s = series.Urn }, cancellationToken: Ct))).ToList();
        Assert.Equal(
            [("2026-01-14", "Evening class"), ("2026-01-15", "Night class"), ("2026-01-16", "Night class")],
            titles);
    }

    [Fact]
    public async Task A_zone_the_database_doesnt_know_fails_loudly()
    {
        await using var kernel = Kernel("Mars/Olympus");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => kernel.GetRequiredService<IHomeCalendar>().DateAtAsync(DateTimeOffset.UtcNow, Ct));

        Assert.Contains("Mars/Olympus", refused.Message);
        Assert.Contains(HomeZone.ConfigurationKey, refused.Message);
    }

    [Fact]
    public async Task A_zone_that_isnt_the_databases_own_is_refused()
    {
        // The test database's bsk.home_zone is UTC.
        await using var kernel = Kernel(Kiritimati);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => kernel.GetRequiredService<IHomeCalendar>().DateAtAsync(DateTimeOffset.UtcNow, Ct));

        Assert.Contains($"{HomeZone.ConfigurationKey} is '{Kiritimati}'", refused.Message);
        Assert.Contains($"bsk.home_zone is '{PostgresFixture.HabitViewZone}'", refused.Message);
    }

    [Fact]
    public async Task The_api_wont_start_with_a_zone_that_isnt_the_databases_own()
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiTestHost.StartAsync(
            classDb.ConnectionString, Ct, new Dictionary<string, string?> { [HomeZone.ConfigurationKey] = Kiritimati }));

        Assert.Contains("bsk.home_zone", refused.Message);
    }

    [Fact]
    public async Task A_check_off_without_a_date_lands_on_today_in_the_home_zone()
    {
        await using var aheadDb = await DatabaseAsync(Kiritimati);
        await using var behindDb = await DatabaseAsync(PagoPago);
        var (ahead, behind) = (aheadDb.Kernel, behindDb.Kernel);
        var aheadHabit = await HabitAsync(ahead);
        var behindHabit = await HabitAsync(behind);

        var (aheadDay, aheadToday) = await AroundAsync(ahead, () => ahead.GetRequiredService<AdherenceService>()
            .RecordAsync(aheadHabit.Urn, AdherenceResults.Followed, cancellationToken: Ct), Ct);
        var (behindDay, behindToday) = await AroundAsync(behind, () => behind.GetRequiredService<AdherenceService>()
            .RecordAsync(behindHabit.Urn, AdherenceResults.Followed, cancellationToken: Ct), Ct);

        Assert.Contains(aheadDay.Occurrence, aheadToday);
        Assert.Contains(behindDay.Occurrence, behindToday);
        Assert.InRange(aheadDay.Occurrence.DayNumber - behindDay.Occurrence.DayNumber, 1, 2);
    }

    [Fact]
    public async Task A_check_off_with_a_date_keeps_it()
    {
        await using var kernel = Kernel(Kiritimati);
        var habit = await HabitAsync(kernel);
        var twoDaysAgo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

        var recorded = await kernel.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, twoDaysAgo, cancellationToken: Ct);

        Assert.Equal(twoDaysAgo, recorded.Occurrence);
    }

    [Fact]
    public async Task The_api_leaves_an_omitted_date_to_the_home_zone()
    {
        await using var db = await DatabaseAsync(Kiritimati);
        await using var host = await ApiTestHost.StartAsync(
            db.ConnectionString, Ct, new Dictionary<string, string?> { [HomeZone.ConfigurationKey] = Kiritimati });
        var kernel = db.Kernel;
        var habit = await HabitAsync(kernel);

        var (response, today) = await AroundAsync(kernel, () => host.Client.PostAsJsonAsync(
            "/api/adhere", new { habit = habit.Id.ToString(), state = "followed" }, Ct), Ct);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        var occurrence = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            """
            SELECT payload->>'occurrence' FROM bsk.event
            WHERE kind = 'activity' AND payload->>'subject_id' = @id
            ORDER BY recorded_at DESC LIMIT 1;
            """,
            new { id = habit.Id.ToString() }, cancellationToken: Ct));
        Assert.Contains(DateOnly.Parse(occurrence!), today);
    }
}

using System.Globalization;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Application.Home;
using LifeOs.Application.Measurements;
using LifeOs.Application.Reviews;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Every date LifeOS reads in the home zone, read at UTC+14 (code review CR88): each view column
/// that dates an instant or counts from today in <c>bsk.home_zone()</c>, and each kernel read that
/// dates in <c>Rewards:HomeTimeZone</c>. The tests' databases are in UTC, where a column that read
/// the UTC date passes unseen. At noon UTC it is already 2 am the next day at UTC+14, and still the
/// same day in Pacific time, so each date written then shows which zone it was read in.
/// <para>
/// A column that counts from today (<c>bsk.home_today()</c>) reads the database's own clock,
/// which no test can move, so it is checked against today at UTC+14 as the test runs. A view that
/// starts to read a home date fails <see cref="Every_view_that_dates_in_the_home_zone_is_read_here"/>
/// until the test below reads it.
/// </para>
/// </summary>
public sealed class HomeZoneDateTests(PostgresFixture postgres)
{
    private const string Kiritimati = "Pacific/Kiritimati";

    /// <summary>Every view that reads <c>bsk.home_zone()</c> or <c>bsk.home_today()</c> itself, each read below.</summary>
    private static readonly string[] DatedViews =
    [
        "bsk.v_fast",
        "bsk.v_food_day",
        "bsk.v_habit_establishment",
        "bsk.v_lab_visit",
        "bsk.v_meal",
        "bsk.v_metric",
        "bsk.v_metric_reading",
        "bsk.v_review",
        "bsk.v_space",
        "bsk.v_task_repetition",
        "bsk_derived.habit_occurrence_source",
        "bsk_derived.habit_planned_source",
    ];

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Today at UTC+14, which keeps no summer time.</summary>
    private static DateOnly TodayAtPlusFourteen() => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(14)).DateTime);

    /// <summary>One date a view holds: where it is, the query that reads it as text, and what it may be.</summary>
    private sealed record HomeDate(string View, string Column, string Sql, object Args, params string[] Expected);

    [Fact]
    public async Task Every_home_date_is_the_day_at_utc_plus_14()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct, Kiritimati);
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        // Noon UTC two days ago: 2 am the next day at UTC+14, 4 or 5 am the same day in Pacific time.
        var at = new DateTimeOffset(utcToday.AddDays(-2).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var day = utcToday.AddDays(-1);
        // Ticking, so a habit's making and its tracking, a moment later, are in order.
        var clock = TestClock.Ticking(at);
        await using var kernel = db.Services().AddSingleton<IClock>(clock).BuildServiceProvider();
        T Get<T>() where T : notnull => kernel.GetRequiredService<T>();
        var (subjects, status) = (Get<SubjectService>(), Get<StatusService>());

        // Everything here happens within a minute of that instant.
        var habit = await subjects.CreateAsync(
            SubjectTypes.Habit, $"zone habit {Guid.NewGuid():N}", JsonSerializer.Serialize(new { recurrence = new { freq = "daily" } }),
            cancellationToken: Ct);
        await status.ChangeStatusAsync(habit.Urn, "Building", Ct);
        var task = await subjects.CreateAsync(SubjectTypes.Task, $"zone task {Guid.NewGuid():N}", "{}", cancellationToken: Ct);
        await Get<BountyService>().SetAsync(task.Urn, null, new Dictionary<string, long> { [Currencies.EnvironmentXp] = 5 }, cancellationToken: Ct);
        var earn = Assert.Single((await status.ChangeStatusAsync(task.Urn, "Completed", Ct)).Awards);
        var series = await subjects.CreateAsync(SubjectTypes.Task, $"zone series {Guid.NewGuid():N}", "{}", cancellationToken: Ct);
        await Get<BountyService>().SetNothingAsync(series.Urn, BountyTriggers.Completed, cancellationToken: Ct);
        var repetition = Assert.Single((await Get<RecurrenceService>().SetAsync(series.Urn, Recurrence.Daily(), day, Ct)).Made);
        await status.ChangeStatusAsync(repetition.Urn, "Completed", Ct);
        var reviews = Get<ReviewService>();
        var review = (await reviews.OpenAsync(ReviewKinds.Daily, cancellationToken: Ct)).Review;
        await reviews.CompleteAsync(review.Id, Ct);
        var metric = await Get<MetricService>().AddAsync("Grip strength", "kg", cancellationToken: Ct);
        await Get<MetricService>().ArchiveAsync(metric.Urn, Ct);
        var space = await Get<SpaceService>().AddAsync("Garage", cancellationToken: Ct);
        await Get<SpaceService>().ArchiveAsync(space.Urn, Ct);
        var visit = await Get<LabVisitService>().LogAsync(new LabVisitEntry(at, "Quest", null, [new("glucose", 104m)]), Ct);
        var food = Get<FoodLogService>();
        var meal = (await food.LogAsync(new FoodEntryInput("Oatmeal", 4, 400, at), cancellationToken: Ct)).Meal!;
        var fast = (await food.RecordFastAsync(at.AddMinutes(-90), at, Ct)).Fast!;

        // The kernel's own dates, in its zone, at that instant.
        var credit = Get<IDomainCreditReader>();
        var domainDays = await credit.GetDomainDaysAsync([Currencies.EnvironmentXp], Ct);
        var kernelDates = new (string Read, DateOnly Day)[]
        {
            ("the calendar's date of the instant", await Get<IHomeCalendar>().DateAtAsync(at, Ct)),
            ("the calendar's today", await Get<IHomeCalendar>().TodayAsync(Ct)),
            ("the ledger's day for the completion", Assert.Single(await Get<ILedgerRepository>().GetTriggersAsync(task.Id, Ct)).CreditedOn.GetValueOrDefault()),
            ("the earn's credited_on", DateOnly.Parse((await db.PayloadAsync(earn.EventId)).GetProperty("credited_on").GetString()!, CultureInfo.InvariantCulture)),
            ("the domain credit's day", Assert.Single(domainDays.Days).Day),
            ("the domain fold's today", domainDays.Today),
            ("the exception days' today", (await credit.GetExceptionDaysAsync(1, Ct)).Today),
        };

        // And one entry now, on today at UTC+14.
        var todayBefore = TodayAtPlusFourteen();
        clock.Set(DateTimeOffset.UtcNow);
        var apple = (await food.LogAsync(new FoodEntryInput("Apple", 4, 80), cancellationToken: Ct)).Meal!;

        var (local, today) = ($"{Iso(day)} 02:00", new[] { Iso(todayBefore), Iso(TodayAtPlusFourteen()) });
        var days = today.Select(t => (DateOnly.Parse(t, CultureInfo.InvariantCulture).DayNumber - day.DayNumber).ToString(CultureInfo.InvariantCulture)).ToArray();
        var (habitId, mealKey) = (new { id = habit.Id }, new { key = meal.Id.ToString() });
        var reads = new HomeDate[]
        {
            new("bsk.v_meal", "eaten_on", "SELECT to_char(eaten_on, 'YYYY-MM-DD') FROM bsk.v_meal WHERE meal_key = @key", mealKey, Iso(day)),
            new("bsk.v_meal", "eaten_local", "SELECT to_char(eaten_local, 'YYYY-MM-DD HH24:MI') FROM bsk.v_meal WHERE meal_key = @key", mealKey, local),
            new("bsk.v_meal", "eaten_on, now", "SELECT to_char(eaten_on, 'YYYY-MM-DD') FROM bsk.v_meal WHERE meal_key = @key", new { key = apple.Id.ToString() }, today),
            new("bsk.v_fast", "started_on, started_local", "SELECT to_char(started_on, 'YYYY-MM-DD') || ' ' || to_char(started_local, 'YYYY-MM-DD HH24:MI') FROM bsk.v_fast WHERE fast_key = @key",
                new { key = fast.Id.ToString() }, $"{Iso(day)} {Iso(day)} 00:30"),
            new("bsk.v_fast", "ended_on, ended_local", "SELECT to_char(ended_on, 'YYYY-MM-DD') || ' ' || to_char(ended_local, 'YYYY-MM-DD HH24:MI') FROM bsk.v_fast WHERE fast_key = @key",
                new { key = fast.Id.ToString() }, $"{Iso(day)} {local}"),
            new("bsk.v_food_day", "closed, a past day", "SELECT closed::text FROM bsk.v_food_day WHERE day = @day::date", new { day = Iso(day) }, "true"),
            new("bsk.v_food_day", "closed, today", "SELECT closed::text FROM bsk.v_food_day WHERE day = @day::date", new { day = Iso(apple.EatenOn) }, "false"),
            new("bsk.v_lab_visit", "collected_on, collected_local", "SELECT to_char(collected_on, 'YYYY-MM-DD') || ' ' || to_char(collected_local, 'YYYY-MM-DD HH24:MI') FROM bsk.v_lab_visit WHERE visit_key = @key",
                new { key = visit.Visit.Id.ToString() }, $"{Iso(day)} {local}"),
            new("bsk.v_metric_reading", "taken_on, taken_local", "SELECT to_char(taken_on, 'YYYY-MM-DD') || ' ' || to_char(taken_local, 'YYYY-MM-DD HH24:MI') FROM bsk.v_metric_reading WHERE reading_key = @key",
                new { key = Assert.Single(visit.Results).Reading.Id.ToString() }, $"{Iso(day)} {local}"),
            new("bsk.v_metric", "archived_on", "SELECT to_char(archived_on, 'YYYY-MM-DD') FROM bsk.v_metric WHERE id = @id", new { id = metric.Id }, Iso(day)),
            new("bsk.v_space", "archived_on", "SELECT to_char(archived_on, 'YYYY-MM-DD') FROM bsk.v_space WHERE id = @id", new { id = space.Id }, Iso(day)),
            new("bsk.v_review", "first_completed_on", "SELECT to_char(first_completed_on, 'YYYY-MM-DD') FROM bsk.v_review WHERE id = @id", new { id = review.Id }, Iso(day)),
            new("bsk.v_task_repetition", "closed_on", "SELECT to_char(closed_on, 'YYYY-MM-DD') FROM bsk.v_task_repetition WHERE id = @id", new { id = repetition.Id }, Iso(day)),
            new("bsk_derived.habit_planned_source", "planned_until", "SELECT to_char(planned_until, 'YYYY-MM-DD') FROM bsk_derived.habit_planned_source WHERE habit_id = @id", habitId, Iso(day)),
            new("bsk_derived.habit_occurrence_source", "a start by its making", "SELECT to_char(min(occurrence_date), 'YYYY-MM-DD') FROM bsk_derived.habit_occurrence_source WHERE habit_id = @id", habitId, Iso(day)),
            new("bsk_derived.habit_occurrence_source", "the last day, today", "SELECT to_char(max(occurrence_date), 'YYYY-MM-DD') FROM bsk_derived.habit_occurrence_source WHERE habit_id = @id", habitId, today),
            new("bsk_derived.habit_occurrence_source", "missed, before today", """
                SELECT string_agg(DISTINCT state, ',') FROM bsk_derived.habit_occurrence_source
                WHERE habit_id = @id AND occurrence_date < (SELECT max(occurrence_date) FROM bsk_derived.habit_occurrence_source WHERE habit_id = @id)
                """, habitId, "missed"),
            new("bsk.v_habit_establishment", "window_start", "SELECT to_char(window_start + window_days - 1, 'YYYY-MM-DD') FROM bsk.v_habit_establishment WHERE habit_id = @id", habitId, today),
            new("bsk.v_habit_establishment", "age_days", "SELECT age_days::text FROM bsk.v_habit_establishment WHERE habit_id = @id", habitId, days),
        };

        var wrong = kernelDates.Where(d => d.Day != day).Select(d => $"{d.Read}: {Iso(d.Day)}, not {Iso(day)}").ToList();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        foreach (var read in reads)
        {
            var actual = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(read.Sql, read.Args, cancellationToken: Ct));
            if (!read.Expected.Contains(actual))
            {
                wrong.Add($"{read.View}.{read.Column}: {actual ?? "null"}, not {string.Join(" or ", read.Expected.Distinct())}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        Assert.Equal(DatedViews, reads.Select(r => r.View).Distinct().Order(StringComparer.Ordinal));
    }

    // A view that starts to read a home date belongs in the test above, which reads every one of
    // them at UTC+14; a view that reads them only through another one is covered by that one.
    [Fact]
    public async Task Every_view_that_dates_in_the_home_zone_is_read_here()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        var dated = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT schemaname || '.' || viewname AS view FROM pg_views
            WHERE schemaname IN ('bsk', 'bsk_derived') AND definition ~ 'bsk\.home_(zone|today)\(\)'
            ORDER BY (schemaname || '.' || viewname) COLLATE "C";
            """,
            cancellationToken: Ct));

        Assert.Equal(DatedViews, dated);
    }
}

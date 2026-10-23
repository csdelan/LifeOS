using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Cli.Commands;
using LifeOs.Domain;
using LifeOs.Infrastructure.Diagnostics;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// FOOD-1..4 (food-log plan F1, migration 0034): food entries and fasts as versioned events,
/// read back through <c>v_meal</c>, <c>v_fast</c> and <c>v_food_day</c>. Food events belong to no
/// item, so one test's running fast would refuse another's: each test has a database of its
/// own (UTC unless it's about the home zone).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FoodLogServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Now, to the whole second, as the service keeps food times.</summary>
    private static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    private static FoodEntryInput Entry(
        string name, decimal score, int calories, DateTimeOffset? at = null, string? type = null)
        => new(name, score, calories, at, type);

    private async Task<long> CountAsync(DomainTestDatabase db, string sql)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: Ct));
    }

    private Task<long> FoodEventsAsync(DomainTestDatabase db)
        => CountAsync(db, "SELECT count(*) FROM bsk.event WHERE payload ?| array['meal','fast','food_day'];");

    [Fact]
    public async Task The_newest_version_of_an_entry_wins_even_when_its_time_moves_earlier_and_a_delete_voids_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var now = Now();

        var logged = await food.LogAsync(Entry("Oatmeal", 4, 400, now.AddHours(-1), "breakfast"), cancellationToken: Ct);
        var meal = logged.Meal!;
        Assert.Equal(FoodActions.Logged, logged.Action);
        Assert.Equal(("Oatmeal", 4, 400, "breakfast", 1), (meal.Name, meal.Score, meal.Calories, meal.MealType, meal.Versions));
        Assert.Equal(now.AddHours(-1), meal.EatenAt);
        Assert.Equal([meal.EatenOn], logged.Days);

        // Named by the start of its id, as the CLI lists it.
        Assert.Equal(meal.Id, (await food.ResolveMealAsync(FoodLogService.Short(meal.Id), Ct)).Id);

        // An edit that moves the time earlier is still the newest version.
        var edited = await food.EditAsync(
            meal.Id.ToString(), new FoodEntryInput("Oatmeal and berries", 5, 450, now.AddHours(-30), "breakfast", ProteinG: 12), Ct);
        var after = edited.Meal!;
        Assert.Equal(FoodActions.Edited, edited.Action);
        Assert.Equal(meal.Id, after.Id);
        Assert.Equal(("Oatmeal and berries", 5, 450, 12, 2), (after.Name, after.Score, after.Calories, after.ProteinG, after.Versions));
        Assert.Equal(now.AddHours(-30), after.EatenAt);
        Assert.True(after.LoggedAt > after.FirstLoggedAt);
        Assert.Equal(new[] { meal.EatenOn, after.EatenOn }.Distinct().Order(), edited.Days);

        // The day it left no longer counts it; the day it moved to does.
        if (after.EatenOn != meal.EatenOn)
        {
            Assert.Equal(0, (await food.GetDayAsync(meal.EatenOn, Ct)).Facts.Meals);
        }

        var day = await food.GetDayAsync(after.EatenOn, Ct);
        Assert.Equal((1, 450L, 2250L), (day.Facts.Meals, day.Facts.Calories, day.Facts.ScoreCalories));

        // A delete is a voided version: the entry and its calories are gone, the record isn't.
        var deleted = await food.DeleteAsync(FoodLogService.Short(meal.Id), Ct);
        Assert.Equal(FoodActions.Deleted, deleted.Action);
        Assert.Null(await db.Get<IFoodLogRepository>().GetMealAsync(meal.Id, Ct));
        Assert.Equal(0, (await food.GetDayAsync(after.EatenOn, Ct)).Facts.Meals);
        Assert.Equal(3, await FoodEventsAsync(db));
        Assert.Equal(1, await CountAsync(db, "SELECT count(*) FROM bsk.event WHERE (payload->'meal'->>'voided')::boolean;"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => food.ResolveMealAsync(meal.Id.ToString(), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => food.EditAsync(meal.Id.ToString(), Entry("x", 3, 100), Ct));

        // Every version is declared and dated: the entry when eaten, the delete when made.
        Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM bsk.event WHERE payload ? 'meal' AND (provenance <> 'declared' OR kind <> 'measurement');"));
    }

    [Fact]
    public async Task An_entry_counts_on_the_home_zone_day_it_was_eaten()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct, HomeZone.Default);
        var food = db.Get<FoodLogService>();

        // 01:30 UTC on Oct 20 2025 is 6:30 pm on Oct 19 in Los Angeles (daylight time).
        var evening = new DateTimeOffset(2025, 10, 20, 1, 30, 0, TimeSpan.Zero);
        var meal = (await food.LogAsync(Entry("Salmon salad", 5, 650, evening), cancellationToken: Ct)).Meal!;
        Assert.Equal(new DateOnly(2025, 10, 19), meal.EatenOn);
        Assert.Equal(new DateTime(2025, 10, 19, 18, 30, 0), meal.EatenLocal);
        Assert.Equal(FoodEntries.Dinner, meal.MealType);

        // Moved past midnight there, it re-scores both days.
        var moved = await food.EditAsync(
            meal.Id.ToString(), Entry("Salmon salad", 5, 650, new DateTimeOffset(2025, 10, 20, 8, 0, 0, TimeSpan.Zero)), Ct);
        Assert.Equal([new DateOnly(2025, 10, 19), new DateOnly(2025, 10, 20)], moved.Days);
        Assert.Equal(new DateOnly(2025, 10, 20), moved.Meal!.EatenOn);
        Assert.Equal(FoodEntries.Snack, moved.Meal.MealType);
        var range = await food.GetDaysAsync(new DateOnly(2025, 10, 19), new DateOnly(2025, 10, 20), Ct);
        Assert.Equal([0L, 650L], range.Days.Select(d => d.Facts.Calories));
        Assert.All(range.Days, d => Assert.True(d.Facts.Closed, "a past day is closed by time"));

        // The CLI reads wall-clock times in the home zone.
        Assert.Equal(evening, await FoodCommand.ParseAtAsync("2025-10-19 18:30", food, Ct));
        Assert.Equal(evening, await FoodCommand.ParseAtAsync("2025-10-20T01:30:00Z", food, Ct));
        Assert.Equal(evening, await FoodCommand.ParseAtAsync("2025-10-19T18:30:00-07:00", food, Ct));
        var lunch = await FoodCommand.ParseAtAsync("12:30", food, Ct);
        Assert.InRange(lunch!.Value, DateTimeOffset.UtcNow.AddHours(-36), DateTimeOffset.UtcNow.AddMinutes(1));
        var yesterday = await FoodCommand.ParseAtAsync("yesterday 8:00pm", food, Ct);
        Assert.True(yesterday < DateTimeOffset.UtcNow);
        Assert.Null(await FoodCommand.ParseAtAsync(" ", food, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => FoodCommand.ParseAtAsync("lunchtime", food, Ct));
    }

    [Fact]
    public async Task Entries_are_checked_and_a_refusal_writes_nothing()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var now = Now();

        foreach (var bad in new[]
                 {
                     Entry(" ", 3, 100),
                     Entry(new string('x', 81), 3, 100),
                     Entry("Toast", 0, 100),
                     Entry("Toast", 6, 100),
                     Entry("Toast", 0.5m, 100),
                     Entry("Toast", 3.7m, 100),
                     Entry("Toast", 3.25m, 100),
                     Entry("Toast", 5.5m, 100),
                     Entry("Toast", 3, -1),
                     Entry("Toast", 3, 5001),
                     Entry("Toast", 3, 100, type: "brunch"),
                     Entry("Toast", 3, 100, now.AddMinutes(10)),
                     Entry("Toast", 3, 100, new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero)),
                     new FoodEntryInput("Toast", 3, 100, ProteinG: -1),
                     new FoodEntryInput("Toast", 3, 100, FatG: 1001)
                 })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => food.LogAsync(bad, cancellationToken: Ct));
        }

        await Assert.ThrowsAsync<ArgumentException>(() => food.ResolveMealAsync("ab", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => food.ResolveMealAsync("not-hex!", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => food.ResolveMealAsync("0000000", Ct));
        Assert.Equal(0, await FoodEventsAsync(db));

        // The limits themselves are fine, and the name is trimmed.
        var edge = await food.LogAsync(
            new FoodEntryInput($"  {new string('y', 80)}  ", 1, 5000, now.AddMinutes(-1), "DRINK", 0, 1000, 0), cancellationToken: Ct);
        Assert.Equal((80, FoodEntries.Drink), (edge.Meal!.Name.Length, edge.Meal.MealType));

        // "Log again" offers the newest entry of each name, any case.
        await food.LogAsync(Entry("Oatmeal", 4, 400, now.AddHours(-3)), cancellationToken: Ct);
        await food.LogAsync(Entry("Salad", 5, 500, now.AddHours(-2)), cancellationToken: Ct);
        await food.LogAsync(Entry("oatmeal ", 4, 350, now.AddHours(-1)), cancellationToken: Ct);
        var recent = await food.GetRecentAsync(cancellationToken: Ct);
        Assert.Equal([new string('y', 80), "oatmeal", "Salad"], recent.Select(m => m.Name));
        Assert.Equal(350, recent[1].Calories);
        Assert.Single(await food.GetRecentAsync(1, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => food.GetRecentAsync(0, Ct));
    }

    /// <summary>Food-log plan §8: a score can be a half, and every read keeps it (migration 0040).</summary>
    [Fact]
    public async Task A_half_score_is_stored_as_given_and_every_read_keeps_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var day = (await db.TodayAsync()).AddDays(-2);
        DateTimeOffset At(int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

        var wrap = (await food.LogAsync(Entry("Turkey wrap", 3.5m, 600, At(12)), cancellationToken: Ct)).Meal!;
        Assert.Equal(3.5m, wrap.Score);

        // A whole score sent with a trailing zero is stored as the whole number, as before.
        var pasta = (await food.LogAsync(Entry("Pasta", 3.0m, 333, At(18)), cancellationToken: Ct)).Meal!;
        Assert.Equal((3m, 0), (pasta.Score, pasta.Score.Scale));
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            var stored = (await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT payload->'meal'->>'score' FROM bsk.event WHERE payload ? 'meal' ORDER BY occurred_at;",
                cancellationToken: Ct))).ToList();
            Assert.Equal(["3.5", "3"], stored);
        }

        // The view reads the half (it used to round it to 4), and the day's sum keeps it.
        var view = await food.GetDayAsync(day, Ct);
        Assert.Equal([3.5m, 3m], view.Meals.Select(m => m.Score));
        Assert.Equal((933L, 3099m), (view.Facts.Calories, view.Facts.ScoreCalories));
        Assert.Equal(3.32m, Math.Round(view.Score.AverageScore!.Value, 2));

        // 3.5 x 333 leaves a half in the sum: an edit to a 3.5 pasta reads 600 x 3.5 + 333 x 3.5.
        await food.EditAsync(pasta.Id.ToString(), Entry("Pasta", 3.5m, 333, At(18)), Ct);
        view = await food.GetDayAsync(day, Ct);
        Assert.Equal((3265.5m, 3.5m), (view.Facts.ScoreCalories, view.Score.AverageScore));
        Assert.Equal(17.5m, view.Score.Quality);
        Assert.Equal(3.5m, (await food.GetRecentAsync(cancellationToken: Ct)).Single(m => m.Name == "Pasta").Score);
        Assert.Equal(3.5m, (await food.GetDaysAsync(day, day, Ct)).Days.Single().Score.AverageScore);
    }

    [Fact]
    public async Task One_fast_runs_at_a_time_and_fasts_never_overlap()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var now = Now();

        await Assert.ThrowsAsync<InvalidOperationException>(() => food.EndFastAsync(cancellationToken: Ct));

        var begun = await food.BeginFastAsync(now.AddHours(-20), Ct);
        var running = begun.Fast!;
        Assert.Equal(FoodActions.FastBegun, begun.Action);
        Assert.True(running.Running);
        Assert.Equal(now.AddHours(-20), running.StartedAt);
        Assert.Empty(begun.Days);
        Assert.Equal(running.Id, (await food.GetDayAsync(cancellationToken: Ct)).Running?.Id);

        var twice = await Assert.ThrowsAsync<InvalidOperationException>(() => food.BeginFastAsync(cancellationToken: Ct));
        Assert.Contains("already running", twice.Message);

        // A past fast before it records; one reaching into it is refused, in words.
        var past = (await food.RecordFastAsync(now.AddHours(-48), now.AddHours(-30), Ct)).Fast!;
        Assert.Equal(18 * 3600L, past.Seconds);
        var overlap = await Assert.ThrowsAsync<InvalidOperationException>(
            () => food.RecordFastAsync(now.AddHours(-25), now.AddHours(-19), Ct));
        Assert.Contains("overlaps your fast from", overlap.Message);
        Assert.Contains("still running", overlap.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => food.RecordFastAsync(now.AddHours(-31), now.AddHours(-29), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => food.RecordFastAsync(now.AddHours(-10), now.AddHours(-12), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => food.RecordFastAsync(now.AddHours(-1), now.AddHours(1), Ct));

        // Back to back is fine: one ends as the next begins.
        var adjacent = (await food.RecordFastAsync(now.AddHours(-30), now.AddHours(-21), Ct)).Fast!;

        // A fast ends after it starts.
        await Assert.ThrowsAsync<ArgumentException>(() => food.EndFastAsync(now.AddHours(-21), Ct));
        var ended = await food.EndFastAsync(now.AddHours(-1), Ct);
        Assert.Equal(FoodActions.FastEnded, ended.Action);
        Assert.Equal(19 * 3600L, ended.Fast!.Seconds);
        Assert.Equal([ended.Fast.EndedOn!.Value], ended.Days);
        Assert.Null(await db.Get<IFoodLogRepository>().GetRunningFastAsync(Ct));

        // Edits are checked like new fasts, against every other fast.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => food.EditFastAsync(adjacent.Id.ToString(), now.AddHours(-30), now.AddHours(-19), Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => food.EditFastAsync(adjacent.Id.ToString(), now.AddHours(-30), null, Ct));
        var shorter = await food.EditFastAsync(FoodLogService.Short(adjacent.Id), now.AddHours(-29), now.AddHours(-22), Ct);
        Assert.Equal((FoodActions.FastEdited, 7 * 3600L), (shorter.Action, shorter.Fast!.Seconds));

        // A running fast's start can move and it keeps running.
        var again = (await food.BeginFastAsync(now.AddMinutes(-30), Ct)).Fast!;
        var moved = await food.EditFastAsync(again.Id.ToString(), now.AddMinutes(-50), null, Ct);
        Assert.True(moved.Fast!.Running);
        Assert.Equal(now.AddMinutes(-50), moved.Fast.StartedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => food.EditFastAsync(again.Id.ToString(), now.AddHours(-2), null, Ct));

        // A delete voids it, freeing its time.
        var deleted = await food.DeleteFastAsync(past.Id.ToString(), Ct);
        Assert.Equal(FoodActions.FastDeleted, deleted.Action);
        Assert.Null(await db.Get<IFoodLogRepository>().GetFastAsync(past.Id, Ct));
        await food.RecordFastAsync(now.AddHours(-47), now.AddHours(-31), Ct);

        Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM bsk.event WHERE payload ? 'fast' AND (provenance <> 'declared' OR kind <> 'activity');"));
    }

    [Fact]
    public async Task Logging_food_can_end_the_running_fast_but_a_zero_calorie_entry_never_does()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var now = Now();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => food.LogAsync(Entry("Toast", 3, 200, now.AddHours(-1)), endFast: true, Ct));

        var fast = (await food.BeginFastAsync(now.AddHours(-18), Ct)).Fast!;

        // Black coffee: logged, and the fast runs on. Asked to end it, it's refused.
        await Assert.ThrowsAsync<ArgumentException>(
            () => food.LogAsync(Entry("Black coffee", 5, 0, now.AddHours(-4), "drink"), endFast: true, Ct));
        var coffee = await food.LogAsync(Entry("Black coffee", 5, 0, now.AddHours(-4), "drink"), cancellationToken: Ct);
        Assert.Null(coffee.Fast);
        Assert.True((await db.Get<IFoodLogRepository>().GetFastAsync(fast.Id, Ct))!.Running);

        // Food from before the fast began can't end it.
        await Assert.ThrowsAsync<ArgumentException>(
            () => food.LogAsync(Entry("Late snack", 2, 300, now.AddHours(-19)), endFast: true, Ct));

        // Lunch ends it at lunch's time, in the same write.
        var lunch = await food.LogAsync(Entry("Salmon salad", 5, 1200, now.AddHours(-2)), endFast: true, Ct);
        Assert.Equal(FoodActions.Logged, lunch.Action);
        Assert.Equal(fast.Id, lunch.Fast!.Id);
        Assert.Equal(now.AddHours(-2), lunch.Fast.EndedAt);
        Assert.Equal(16 * 3600L, lunch.Fast.Seconds);
        Assert.Equal(0, lunch.Fast.MealsDuring);
        Assert.Contains(lunch.Fast.EndedOn!.Value, lunch.Days);
        Assert.Contains(lunch.Meal!.EatenOn, lunch.Days);
        Assert.Null(await db.Get<IFoodLogRepository>().GetRunningFastAsync(Ct));

        // The day view offers the newest entry with calories for Begin fast.
        var day = await food.GetDayAsync(lunch.Meal.EatenOn, Ct);
        Assert.Equal(lunch.Meal.Id, day.LastMeal?.Id);
        Assert.Empty(day.EatenDuringFast);
    }

    [Fact]
    public async Task A_day_scores_its_entries_and_its_longest_fast_and_flags_food_eaten_inside_one()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var day = (await db.TodayAsync()).AddDays(-3);
        DateTimeOffset At(int dayOffset, int hour) => new(day.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

        // A 23-hour fast ends on the day, and a 10-hour one later the same day counts for less.
        var longFast = (await food.RecordFastAsync(At(-1, 12), At(0, 11), Ct)).Fast!;
        var shortFast = (await food.RecordFastAsync(At(0, 13), At(0, 23), Ct)).Fast!;
        var salmon = (await food.LogAsync(Entry("Salmon salad", 5, 1000, At(0, 12)), cancellationToken: Ct)).Meal!;
        var snack = (await food.LogAsync(Entry("Almonds", 5, 200, At(0, 15)), cancellationToken: Ct)).Meal!;
        await food.LogAsync(Entry("Water", 5, 0, At(0, 16), "drink"), cancellationToken: Ct);

        var view = await food.GetDayAsync(day, Ct);
        Assert.Equal((3, 1200L, 6000L), (view.Facts.Meals, view.Facts.Calories, view.Facts.ScoreCalories));
        Assert.Equal(23 * 3600L, view.Facts.LongestFastSeconds);
        Assert.Equal(longFast.Id, view.Facts.LongestFastId);
        Assert.True(view.Facts.Closed);
        Assert.False(view.Facts.ClosedByHand);
        Assert.Equal((32L, 7L, 39L), (view.Score.FoodAward, view.Score.FastAward, view.Score.Total));
        Assert.Equal([longFast.Id, shortFast.Id], view.Fasts.Select(f => f.Id));
        Assert.Equal(["Salmon salad", "Almonds", "Water"], view.Meals.Select(m => m.Name)); // oldest first

        // The snack was eaten inside the short fast; the 0-calorie water doesn't count.
        Assert.Equal(shortFast.Id, Assert.Single(view.EatenDuringFast, kv => kv.Key == snack.Id).Value);
        Assert.Single(view.EatenDuringFast);
        Assert.False(view.EatenDuringFast.ContainsKey(salmon.Id));
        Assert.Equal(1, (await db.Get<IFoodLogRepository>().GetFastAsync(shortFast.Id, Ct))!.MealsDuring);

        // The facts behind the score: every entry's version, and the long fast's.
        Assert.Equal(3, view.Facts.FoodEventIds.Count);
        Assert.NotNull(view.Facts.FastEventId);

        // A range fills every day, the empty ones with nothing.
        var range = await food.GetDaysAsync(day.AddDays(-1), day.AddDays(1), Ct);
        Assert.Equal([0L, 39L, 0L], range.Days.Select(d => d.Score.Total));
        Assert.Equal([day.AddDays(-1), day, day.AddDays(1)], range.Days.Select(d => d.Facts.Day));
    }

    [Fact]
    public async Task Closing_a_day_writes_once_and_food_events_stay_out_of_every_other_view()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var today = await db.TodayAsync();
        var now = Now();

        await food.LogAsync(Entry("Oatmeal", 4, 400, now.AddMinutes(-5)), cancellationToken: Ct);
        await food.BeginFastAsync(now, Ct);

        var closed = await food.CloseDayAsync(cancellationToken: Ct);
        Assert.Equal((FoodActions.Closed, today), (closed.Action, closed.Day));
        var facts = (await food.GetDayAsync(today, Ct)).Facts;
        Assert.True(facts.Closed && facts.ClosedByHand);
        Assert.NotNull(facts.ClosedAt);

        // Again, or a day that's over: nothing written.
        Assert.Equal(FoodActions.AlreadyClosed, (await food.CloseDayAsync(today, Ct)).Action);
        Assert.Equal(FoodActions.AlreadyClosed, (await food.CloseDayAsync(today.AddDays(-1), Ct)).Action);
        await Assert.ThrowsAsync<ArgumentException>(() => food.CloseDayAsync(today.AddDays(1), Ct));
        Assert.Equal(1, await CountAsync(db, "SELECT count(*) FROM bsk.event WHERE payload ? 'food_day';"));

        // Ranges: at most 62 days, in order.
        Assert.Equal(FoodLogService.MaxRangeDays, (await food.GetDaysAsync(today.AddDays(-61), today, Ct)).Days.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => food.GetDaysAsync(today.AddDays(-62), today, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => food.GetDaysAsync(today, today.AddDays(-1), Ct));

        // Food events concern no item: no inbox, no diagnostic finding, and the diet award the
        // close paid (FoodRewardsTests) belongs to no item either.
        Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM bsk.v_inbox;"));
        Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM bsk.subject_event;"));
        Assert.Equal(1, await CountAsync(db, "SELECT count(*) FROM bsk.v_ledger WHERE award_key IS NOT NULL;"));
        Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM bsk.v_ledger WHERE subject_id IS NOT NULL;"));
        Assert.Equal(0, (await new DiagnosticRunner(db.ConnectionString).RunAsync(cancellationToken: Ct)).FindingCount);

        // The close is dated now and names its day.
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        var payload = await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT payload::text FROM bsk.event WHERE payload ? 'food_day';", cancellationToken: Ct));
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(today.ToString("yyyy-MM-dd"), document.RootElement.GetProperty("food_day").GetProperty("on").GetString());
    }
}

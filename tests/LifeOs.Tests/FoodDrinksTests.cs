using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// FOOD-6 / FOOD-7 (food-log plan F7, §7, migration 0038): alcoholic drinks in the Food Log. A
/// drink is an entry flagged alcoholic, always scored 1; more than two on a day cut its diet XP,
/// the food award at the close and the fast award once the day is closed; the sobriety streak
/// and the drinks count read them back; a day's entries come oldest first. Each test has a
/// database of its own (UTC), since a food write settles every food day in it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FoodDrinksTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0)
        => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    private static FoodEntryInput Beer(DateTimeOffset at, int score = 1)
        => new("Beer", score, 150, at, Alcoholic: true);

    private static long Health(Award award) => award.Amounts.GetValueOrDefault(Currencies.HealthXp);

    /// <summary>A write's awards on <paramref name="key"/> as (entry, Health XP), in order.</summary>
    private static IEnumerable<(string Entry, long Xp)> On(FoodWriteResult result, string key)
        => result.Earned.Awards.Where(a => a.AwardKey == key).Select(a => (a.Entry, Health(a)));

    /// <summary>How many events an event cites (<c>derived_from</c>).</summary>
    private async Task<int> CitedAsync(DomainTestDatabase db, Guid eventId)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT coalesce(cardinality(derived_from), 0) FROM bsk.event WHERE id = @eventId;", new { eventId }, cancellationToken: Ct));
    }

    private async Task<(long Base, long Paid)> CreditAsync(DomainTestDatabase db, DateOnly day)
    {
        var credit = await db.Get<ILedgerRepository>().GetDomainDaysAsync([Currencies.HealthXp], Ct);
        var row = credit.Days.Where(d => d.Day == day).ToList();
        return (row.Sum(d => d.Base), row.Sum(d => d.Paid));
    }

    [Fact]
    public async Task A_drink_is_scored_1_typed_as_a_drink_and_keeps_its_flag_until_told_otherwise()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var now = Now();

        // Whatever score is sent, a drink is a 1; with no meal type it's a drink, whatever the hour.
        var logged = (await food.LogAsync(new FoodEntryInput("IPA", 5, 200, now.AddHours(-2), Alcoholic: true), cancellationToken: Ct)).Meal!;
        Assert.Equal((true, 1, FoodEntries.Drink), (logged.Alcoholic, logged.Score, logged.MealType));
        var payload = await db.PayloadAsync(logged.EventId);
        Assert.True(payload.GetProperty("meal").GetProperty("alcoholic").GetBoolean());

        // An entry that isn't a drink carries no flag at all.
        var oatmeal = (await food.LogAsync(new FoodEntryInput("Oatmeal", 4, 400, now.AddHours(-3)), cancellationToken: Ct)).Meal!;
        Assert.False(oatmeal.Alcoholic);
        Assert.False((await db.PayloadAsync(oatmeal.EventId)).GetProperty("meal").TryGetProperty("alcoholic", out _));

        // An edit that leaves the flag out keeps it, and the score stays 1.
        var edited = (await food.EditAsync(logged.Id.ToString(), new FoodEntryInput("IPA", 4, 250, logged.EatenAt), Ct)).Meal!;
        Assert.Equal((true, 1, 250, 2), (edited.Alcoholic, edited.Score, edited.Calories, edited.Versions));

        // Told it isn't a drink, it takes the score it's given.
        var cleared = (await food.EditAsync(
            logged.Id.ToString(), new FoodEntryInput("Kombucha", 4, 90, logged.EatenAt, FoodEntries.Drink, Alcoholic: false), Ct)).Meal!;
        Assert.Equal((false, 4, FoodEntries.Drink), (cleared.Alcoholic, cleared.Score, cleared.MealType));

        // A drink still needs a name and sensible calories.
        await Assert.ThrowsAsync<ArgumentException>(() => food.LogAsync(new FoodEntryInput("Wine", 0, -1, Alcoholic: true), cancellationToken: Ct));
    }

    [Fact]
    public async Task A_day_lists_its_entries_oldest_first_and_counts_its_drinks_and_the_streak_reads_them()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var today = await db.TodayAsync();
        var (first, drinking) = (today.AddDays(-7), today.AddDays(-2));

        // The Food Log's first day, five days before the drinking day.
        await food.LogAsync(new FoodEntryInput("Oatmeal", 4, 400, At(first, 8)), cancellationToken: Ct);
        await food.LogAsync(Beer(At(drinking, 20)), cancellationToken: Ct);
        await food.LogAsync(new FoodEntryInput("Salmon salad", 5, 1200, At(drinking, 12)), cancellationToken: Ct);
        await food.LogAsync(Beer(At(drinking, 21)), cancellationToken: Ct);

        var day = await food.GetDayAsync(drinking, Ct);
        Assert.Equal(["Salmon salad", "Beer", "Beer"], day.Meals.Select(m => m.Name));
        Assert.Equal((2, 2), (day.Facts.Drinks, day.Score.Drinks));
        Assert.Equal(2, day.Facts.DrinkVersions.Count);
        Assert.False(day.Score.Cut);

        // The streak: 0 on the drinking day (best 5, the days before it), 2 today.
        Assert.Equal((0, 5, (DateOnly?)drinking, first), (day.Sobriety!.Days, day.Sobriety.BestDays, day.Sobriety.LastDrinkOn, day.Sobriety.FirstDay));
        var now = (await food.GetDayAsync(today, Ct)).Sobriety!;
        Assert.Equal((2, 5, drinking.AddDays(1)), (now.Days, now.BestDays, now.Since));

        // A day before the first has none; a range's streak is as of its last day so far.
        Assert.Null((await food.GetDayAsync(first.AddDays(-1), Ct)).Sobriety);
        var week = await food.GetDaysAsync(first, today.AddDays(3), Ct);
        Assert.Equal((today, 2), (week.Sobriety!.AsOf, week.Sobriety.Days));
        Assert.Equal(2, week.Days.Sum(d => d.Facts.Drinks));
        var past = await food.GetDaysAsync(first, first.AddDays(2), Ct);
        Assert.Equal((first.AddDays(2), 3), (past.Sobriety!.AsOf, past.Sobriety.Days));
        Assert.Null((await food.GetDaysAsync(first.AddDays(-9), first.AddDays(-3), Ct)).Sobriety);
    }

    [Fact]
    public async Task Drinks_are_counted_over_days_or_over_times()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var day = (await db.TodayAsync()).AddDays(-3);

        await food.LogAsync(Beer(At(day, 20)), cancellationToken: Ct);
        await food.LogAsync(Beer(At(day, 21)), cancellationToken: Ct);
        await food.LogAsync(Beer(At(day.AddDays(1), 1)), cancellationToken: Ct);
        await food.LogAsync(new FoodEntryInput("Pizza", 2, 900, At(day, 19)), cancellationToken: Ct);

        var one = await food.GetDrinksAsync(day, day, Ct);
        Assert.Equal((2, 1, 2, (DateOnly?)day), (one.Drinks, one.DrinkDays, one.MostInADay, one.MostOn));
        Assert.Equal((At(day, 0), At(day.AddDays(1), 0)), (one.From, one.To));

        var two = await food.GetDrinksAsync(day, day.AddDays(1), Ct);
        Assert.Equal((3, 2, 2), (two.Drinks, two.DrinkDays, two.MostInADay));

        // By times: from 20:30 up to (not including) 1:00 the next morning holds only the 21:00 beer.
        var span = await food.GetDrinksAsync(At(day, 20, 30), At(day.AddDays(1), 1), Ct);
        Assert.Equal((1, 1), (span.Drinks, span.DrinkDays));

        var none = await food.GetDrinksAsync(day.AddDays(-30), day.AddDays(-1), Ct);
        Assert.Equal((0, 0, (DateOnly?)null), (none.Drinks, none.DrinkDays, none.MostOn));
        await Assert.ThrowsAsync<ArgumentException>(() => food.GetDrinksAsync(day, day.AddDays(-1), Ct));
    }

    [Fact]
    public async Task More_than_two_drinks_cut_both_awards_of_a_closed_day_and_removing_them_restores_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        await db.OpenWalletAsync(daysAgo: 10);
        var day = (await db.TodayAsync()).AddDays(-2);
        var (fastKey, dietKey) = (FoodAwards.FastKey(day), FoodAwards.DietKey(day));

        // The salmon day: a 23-hour fast pays 7, the salmon 32 (the day is over, so closed).
        await food.RecordFastAsync(At(day.AddDays(-1), 13), At(day, 12), Ct);
        await food.LogAsync(new FoodEntryInput("Salmon salad", 5, 1200, At(day, 12, 30)), cancellationToken: Ct);
        Assert.Equal((39L, 39L), await CreditAsync(db, day));

        // Two beers cost a little as 1s in the average (32 → 29 → 26); the fast is untouched.
        var first = await food.LogAsync(Beer(At(day, 19)), cancellationToken: Ct);
        Assert.Equal([("reversal", -32L), ("earn", 29L)], On(first, dietKey));
        var second = await food.LogAsync(Beer(At(day, 19, 30)), cancellationToken: Ct);
        Assert.Equal([("reversal", -29L), ("earn", 26L)], On(second, dietKey));
        Assert.Empty(On(second, fastKey));

        // A third cuts 10%: the food 26 → 22; the fast's 7.33 × 0.9 = 6.6 still rounds to 7.
        var third = await food.LogAsync(Beer(At(day, 20)), cancellationToken: Ct);
        Assert.Equal([("reversal", -26L), ("earn", 22L)], On(third, dietKey));
        Assert.Empty(On(third, fastKey));

        // A fourth cuts 20%: the food 22 → 18 and the fast 7 → 6.
        var fourth = await food.LogAsync(Beer(At(day, 21)), cancellationToken: Ct);
        Assert.Equal([("reversal", -22L), ("earn", 18L)], On(fourth, dietKey));
        Assert.Equal([("reversal", -7L), ("earn", 6L)], On(fourth, fastKey));
        Assert.Equal((24L, 24L), await CreditAsync(db, day));
        Assert.Empty((await food.SettleAsync(Ct)).Awards);

        // The re-scored fast cites what it read: the fast and the four beers (the day closed
        // itself, so there's no close event).
        var fastEarn = fourth.Earned.Awards.Single(a => a.AwardKey == fastKey && a.Entry == LedgerEntries.Earn);
        Assert.Equal(5, await CitedAsync(db, fastEarn.EventId));

        // Taking the fourth back restores the fast (6.6 rounds to 7); the third, the food.
        var drinks = (await food.GetDayAsync(day, Ct)).Meals.Where(m => m.Alcoholic).ToList();
        var fewer = await food.DeleteAsync(drinks[3].Id.ToString(), Ct);
        Assert.Equal([("reversal", -6L), ("earn", 7L)], On(fewer, fastKey));
        Assert.Equal([("reversal", -18L), ("earn", 22L)], On(fewer, dietKey));
        var back = await food.DeleteAsync(drinks[2].Id.ToString(), Ct);
        Assert.Empty(On(back, fastKey));
        Assert.Equal((33L, 33L), await CreditAsync(db, day));

        // Twelve drinks leave the day nothing: both awards are given back.
        FoodWriteResult last = back;
        for (var i = 0; i < 10; i++)
        {
            last = await food.LogAsync(Beer(At(day, 22, i)), cancellationToken: Ct);
        }

        // At eleven, 10% was left of each (1 and 1); the twelfth gives both back.
        Assert.Equal(12, (await food.GetDayAsync(day, Ct)).Facts.Drinks);
        Assert.Equal([("reversal", -1L)], On(last, fastKey));
        Assert.Equal([("reversal", -1L)], On(last, dietKey));
        Assert.Equal((0L, 0L), await CreditAsync(db, day));
    }

    [Fact]
    public async Task Today_a_fast_is_paid_in_full_until_the_close_and_cut_then()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        await db.OpenWalletAsync(daysAgo: 10);
        var today = await db.TodayAsync();
        var now = Now();
        if (now - At(today, 0) < TimeSpan.FromMinutes(30))
        {
            Assert.Skip("Too close to midnight UTC for a test about today.");
        }

        // A 20-hour fast pays 5 when it ends.
        await food.BeginFastAsync(now.AddHours(-20), Ct);
        var ended = await food.EndFastAsync(now.AddMinutes(-20), Ct);
        Assert.Equal(5L, Health(Assert.Single(ended.Earned.Awards)));

        // Four beers while the day is open take nothing back...
        for (var i = 4; i >= 1; i--)
        {
            var beer = await food.LogAsync(Beer(now.AddMinutes(-i)), cancellationToken: Ct);
            Assert.Empty(beer.Earned.Awards);
        }

        // ... though the day shows what the close will pay: the fast cut to 4, the food to 3.
        var open = await food.GetDayAsync(today, Ct);
        Assert.Equal((4L, 5L, 3L, 0.8m), (open.Score.FastAward, open.Score.FastAwardUncut, open.Score.FoodAward, open.Score.DrinkFactor));
        Assert.Equal(5L, open.Paid.Fast!.Paid);

        // Closing pays the food (600 calories at 1: 4 for calories, cut to 3) and re-scores the fast.
        var closed = await food.CloseDayAsync(cancellationToken: Ct);
        Assert.Equal([("earn", 3L)], On(closed, FoodAwards.DietKey(today)));
        Assert.Equal([("reversal", -5L), ("earn", 4L)], On(closed, FoodAwards.FastKey(today)));
        Assert.Equal((7L, 7L), await CreditAsync(db, today));
    }

    [Fact]
    public async Task Drinks_go_over_http_with_no_score_and_read_back_with_the_day_the_streak_and_the_count()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var day = (await db.TodayAsync()).AddDays(-1);
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(19, 0)), TimeSpan.Zero).ToString("O");

        // A drink needs no score; anything else still does.
        var logged = await host.Client.PostAsJsonAsync("/api/food/meals", new { name = "IPA", calories = 200, alcoholic = true, eatenAt = at }, Ct);
        Assert.True(logged.IsSuccessStatusCode, await logged.Content.ReadAsStringAsync(Ct));
        var meal = (await logged.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("meal");
        Assert.Equal((true, 1, "drink"), (meal.GetProperty("alcoholic").GetBoolean(), meal.GetProperty("score").GetInt32(), meal.GetProperty("mealType").GetString()));
        var refused = await host.Client.PostAsJsonAsync("/api/food/meals", new { name = "Toast", calories = 200 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // The day: its drinks, the cut (none yet) and the streak (0, a drink that day).
        var read = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/day?date={day:yyyy-MM-dd}", Ct);
        var summary = read.GetProperty("summary");
        Assert.Equal((1, 1m), (summary.GetProperty("drinks").GetInt32(), summary.GetProperty("drinkFactor").GetDecimal()));
        Assert.True(Assert.Single(read.GetProperty("meals").EnumerateArray()).GetProperty("alcoholic").GetBoolean());
        var streak = read.GetProperty("sobriety");
        Assert.Equal((0, $"{day:yyyy-MM-dd}", $"{day:yyyy-MM-dd}"),
            (streak.GetProperty("days").GetInt32(), streak.GetProperty("lastDrinkOn").GetString(), streak.GetProperty("firstDay").GetString()));
        var range = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/days?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}", Ct);
        Assert.Equal(1, range.GetProperty("sobriety").GetProperty("days").GetInt32());

        // The count, by days (the last 30 by default) and by times.
        var count = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/drinks", Ct);
        Assert.Equal((1, 1, $"{day:yyyy-MM-dd}"),
            (count.GetProperty("drinks").GetInt32(), count.GetProperty("drinkDays").GetInt32(), count.GetProperty("mostOn").GetString()));
        var byDays = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/drinks?from={day.AddDays(-90):yyyy-MM-dd}&to={day.AddDays(-1):yyyy-MM-dd}", Ct);
        Assert.Equal(0, byDays.GetProperty("drinks").GetInt32());
        var from = Uri.EscapeDataString(new DateTimeOffset(day.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero).ToString("O"));
        var byTimes = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/drinks?from={from}&to={day.AddDays(1):yyyy-MM-dd}", Ct);
        Assert.Equal(1, byTimes.GetProperty("drinks").GetInt32());
        var bad = await host.Client.GetAsync("/api/food/drinks?from=last-tuesday", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // An edit that says it isn't a drink after all.
        var id = meal.GetProperty("id").GetGuid();
        var edited = await host.Client.PostAsJsonAsync($"/api/food/meals/{id}", new { name = "Kombucha", score = 4, calories = 90, alcoholic = false }, Ct);
        Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync(Ct));
        var after = (await edited.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("meal");
        Assert.Equal((false, 4), (after.GetProperty("alcoholic").GetBoolean(), after.GetProperty("score").GetInt32()));
    }
}

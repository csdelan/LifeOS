using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// FOOD-1..4 over HTTP (food-log plan F3, §3.6): <c>/api/food</c> reads on the reader
/// connection and writes through <c>FoodLogService</c>, answering what settling paid. Refusals
/// are 400s in words. Each test has a database of its own (UTC), since a food write settles
/// every food day in it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FoodEndpointTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Iso(DateOnly day, int hour, int minute = 0)
        => new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero).ToString("O");

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd");

    private async Task<JsonElement> PostAsync(ApiTestHost host, string path, object? body)
    {
        var response = body is null
            ? await host.Client.PostAsync(path, null, Ct)
            : await host.Client.PostAsJsonAsync(path, body, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<string> RefusedAsync(ApiTestHost host, string path, object? body)
    {
        var response = await host.Client.PostAsJsonAsync(path, body, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()!;
    }

    private async Task<string> RefusedGetAsync(ApiTestHost host, string path)
    {
        var response = await host.Client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()!;
    }

    private static IEnumerable<(string Entry, long Xp)> Awards(JsonElement result)
        => result.GetProperty("awards").EnumerateArray()
            .Select(a => (a.GetProperty("entry").GetString()!, a.GetProperty("amounts").GetProperty(Currencies.HealthXp).GetInt64()));

    [Fact]
    public async Task A_day_logged_over_http_pays_and_reads_back_with_its_score_and_what_it_paid()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await db.OpenWalletAsync(daysAgo: 10);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var day = (await db.TodayAsync()).AddDays(-2);

        // A 23-hour fast ending at noon, then a salmon salad: example 2, 7 + 32.
        var fast = await PostAsync(host, "/api/food/fasts", new { action = "record", from = Iso(day.AddDays(-1), 13), to = Iso(day, 12) });
        Assert.Equal("fast_recorded", fast.GetProperty("action").GetString());
        Assert.Equal(82_800, fast.GetProperty("fast").GetProperty("seconds").GetInt64());
        Assert.Equal([("earn", 7L)], Awards(fast));

        var logged = await PostAsync(host, "/api/food/meals",
            new { name = "Salmon salad", score = 5, calories = 1200, eatenAt = Iso(day, 12, 30), proteinG = 40 });
        Assert.Equal("logged", logged.GetProperty("action").GetString());
        var meal = logged.GetProperty("meal");
        Assert.Equal(("Salmon salad", "lunch", Day(day), 40), (meal.GetProperty("name").GetString(), meal.GetProperty("mealType").GetString(),
            meal.GetProperty("eatenOn").GetString(), meal.GetProperty("proteinG").GetInt32()));
        Assert.Equal([Day(day)], logged.GetProperty("days").EnumerateArray().Select(d => d.GetString()));
        var award = Assert.Single(logged.GetProperty("awards").EnumerateArray());
        Assert.Equal(($"diet:{Day(day)}", "diet", 32L), (award.GetProperty("awardKey").GetString(), award.GetProperty("trigger").GetString(),
            award.GetProperty("amounts").GetProperty(Currencies.HealthXp).GetInt64()));
        Assert.Equal(JsonValueKind.Array, logged.GetProperty("progress").ValueKind);

        // The day reads back, on the reader connection.
        var read = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/day?date={Day(day)}", Ct);
        Assert.Equal(Day(day), read.GetProperty("day").GetString());
        Assert.Equal(Day(await db.TodayAsync()), read.GetProperty("today").GetString());
        var readMeal = Assert.Single(read.GetProperty("meals").EnumerateArray());
        Assert.Equal(meal.GetProperty("id").GetGuid(), readMeal.GetProperty("id").GetGuid());
        Assert.Equal(DateTimeOffset.Parse(Iso(day, 12, 30)), readMeal.GetProperty("eatenAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, readMeal.GetProperty("duringFast").ValueKind);
        Assert.Equal(1, readMeal.GetProperty("versions").GetInt32());
        Assert.Equal(fast.GetProperty("fast").GetProperty("id").GetGuid(), Assert.Single(read.GetProperty("fasts").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("running").ValueKind);
        Assert.Equal(meal.GetProperty("id").GetGuid(), read.GetProperty("lastMeal").GetProperty("id").GetGuid());

        var summary = read.GetProperty("summary");
        Assert.Equal((1, 1200L, 5.0m, true, false), (summary.GetProperty("meals").GetInt32(), summary.GetProperty("calories").GetInt64(),
            summary.GetProperty("averageScore").GetDecimal(), summary.GetProperty("closed").GetBoolean(), summary.GetProperty("closedByHand").GetBoolean()));
        Assert.Equal((28m, 4m, 7.33m), (summary.GetProperty("quality").GetDecimal(), summary.GetProperty("caloriesPart").GetDecimal(), summary.GetProperty("fasting").GetDecimal()));
        Assert.Equal((32L, 7L, 39L, 40), (summary.GetProperty("foodAward").GetInt64(), summary.GetProperty("fastAward").GetInt64(),
            summary.GetProperty("total").GetInt64(), summary.GetProperty("cap").GetInt32()));
        var paid = summary.GetProperty("paid");
        Assert.Equal((39L, 39L), (paid.GetProperty("total").GetInt64(), paid.GetProperty("baseTotal").GetInt64()));
        Assert.Equal((32L, 32L), (paid.GetProperty("diet").GetProperty("base").GetInt64(), paid.GetProperty("diet").GetProperty("paid").GetInt64()));
        Assert.Equal(JsonValueKind.Null, paid.GetProperty("diet").GetProperty("multiplier").ValueKind);
        Assert.Equal(7L, paid.GetProperty("fast").GetProperty("paid").GetInt64());

        // The range: one summary a day, oldest first; an empty day reads as nothing.
        var range = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/days?from={Day(day.AddDays(-1))}&to={Day(day.AddDays(1))}", Ct);
        Assert.Equal([0L, 39L, 0L], range.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("paid").GetProperty("total").GetInt64()));
        Assert.Equal([Day(day.AddDays(-1)), Day(day), Day(day.AddDays(1))], range.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("day").GetString()));

        // "Log again" offers it.
        var recent = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/recent", Ct);
        Assert.Equal("Salmon salad", Assert.Single(recent.EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Edits_and_deletes_re_score_the_day_and_an_edit_keeps_the_time_it_isnt_sent()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await db.OpenWalletAsync(daysAgo: 10);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var day = (await db.TodayAsync()).AddDays(-3);

        var logged = await PostAsync(host, "/api/food/meals",
            new { name = "Salmon salad", score = 5, calories = 1200, eatenAt = Iso(day, 19), mealType = "dinner", fatG = 30 });
        var id = logged.GetProperty("meal").GetProperty("id").GetGuid().ToString("N");

        // By the start of its id; the whole entry is sent, so the fat left out is cleared.
        var edited = await PostAsync(host, $"/api/food/meals/{id[..8]}", new { name = "Salmon salad", score = 3, calories = 1200 });
        Assert.Equal("edited", edited.GetProperty("action").GetString());
        var meal = edited.GetProperty("meal");
        Assert.Equal((DateTimeOffset.Parse(Iso(day, 19)), "dinner", 2), (meal.GetProperty("eatenAt").GetDateTimeOffset(),
            meal.GetProperty("mealType").GetString(), meal.GetProperty("versions").GetInt32()));
        Assert.Equal(JsonValueKind.Null, meal.GetProperty("fatG").ValueKind);
        Assert.Equal([("reversal", -32L), ("earn", 18L)], Awards(edited));

        var deleted = await PostAsync(host, $"/api/food/meals/{id}/delete", null);
        Assert.Equal("deleted", deleted.GetProperty("action").GetString());
        Assert.Equal([("reversal", -18L)], Awards(deleted));
        var read = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/day?date={Day(day)}", Ct);
        Assert.Equal(0, read.GetProperty("meals").GetArrayLength());
        Assert.Equal(0L, read.GetProperty("summary").GetProperty("paid").GetProperty("total").GetInt64());
    }

    /// <summary>Food-log plan §8: a half score goes in and comes back as a half, and pays halfway.</summary>
    [Fact]
    public async Task A_half_score_round_trips_and_pays_halfway_between_its_neighbours()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await db.OpenWalletAsync(daysAgo: 10);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var day = (await db.TodayAsync()).AddDays(-3);

        // 1,200 calories at 3.5: quality 17.5 + calories 4 = 21.5, paid as 22.
        var logged = await PostAsync(host, "/api/food/meals",
            new { name = "Turkey wrap", score = 3.5m, calories = 1200, eatenAt = Iso(day, 12) });
        Assert.Equal(3.5m, logged.GetProperty("meal").GetProperty("score").GetDecimal());
        Assert.Equal([("earn", 22L)], Awards(logged));

        var read = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/day?date={Day(day)}", Ct);
        Assert.Equal(3.5m, Assert.Single(read.GetProperty("meals").EnumerateArray()).GetProperty("score").GetDecimal());
        var summary = read.GetProperty("summary");
        Assert.Equal((3.5m, 17.5m, 22L), (summary.GetProperty("averageScore").GetDecimal(),
            summary.GetProperty("quality").GetDecimal(), summary.GetProperty("foodAward").GetInt64()));

        // A whole score reads as a whole number, never "4.0"; up to 4, the day re-scores to 25.
        var id = logged.GetProperty("meal").GetProperty("id").GetGuid();
        var edited = await PostAsync(host, $"/api/food/meals/{id}", new { name = "Turkey wrap", score = 4.0m, calories = 1200 });
        Assert.Equal("4", edited.GetProperty("meal").GetProperty("score").GetRawText());
        Assert.Equal([("reversal", -22L), ("earn", 25L)], Awards(edited));

        // Anything between the half steps is refused in words.
        var refused = await RefusedAsync(host, "/api/food/meals", new { name = "Toast", score = 3.7m, calories = 200, eatenAt = Iso(day, 15) });
        Assert.Contains("half points", refused);
        Assert.Contains("3.7", refused);
    }

    [Fact]
    public async Task Fasts_begin_end_edit_and_delete_and_food_inside_a_fast_is_flagged()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await db.OpenWalletAsync(daysAgo: 10);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var now = DateTimeOffset.UtcNow;

        var begun = await PostAsync(host, "/api/food/fasts", new { action = "begin", at = now.AddHours(-30).ToString("O") });
        Assert.Equal(("fast_begun", true), (begun.GetProperty("action").GetString(), begun.GetProperty("fast").GetProperty("running").GetBoolean()));
        var id = begun.GetProperty("fast").GetProperty("id").GetGuid();
        var today = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/day", Ct);
        Assert.Equal(id, today.GetProperty("running").GetProperty("id").GetGuid());
        Assert.Contains("already running", await RefusedAsync(host, "/api/food/fasts", new { action = "begin" }));

        // Ended 24 hours in: the full 8.
        var ended = await PostAsync(host, "/api/food/fasts", new { action = "end", at = now.AddHours(-6).ToString("O") });
        Assert.Equal(("fast_ended", 86_400L), (ended.GetProperty("action").GetString(), ended.GetProperty("fast").GetProperty("seconds").GetInt64()));
        Assert.Equal([("earn", 8L)], Awards(ended));

        // A snack logged inside it is flagged with the fast.
        var snack = await PostAsync(host, "/api/food/meals", new { name = "Cookie", score = 1, calories = 200, eatenAt = now.AddHours(-10).ToString("O") });
        var snackDay = snack.GetProperty("meal").GetProperty("eatenOn").GetString();
        var read = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/day?date={snackDay}", Ct);
        Assert.Equal(id, read.GetProperty("meals").EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Cookie").GetProperty("duringFast").GetGuid());

        // Shortened to 13 hours by its id's start: 0.67 → 1.
        var edited = await PostAsync(host, "/api/food/fasts",
            new { action = "edit", fast = id.ToString("N")[..6], from = now.AddHours(-19).ToString("O"), to = now.AddHours(-6).ToString("O") });
        Assert.Equal([("reversal", -8L), ("earn", 1L)], Awards(edited));

        var deleted = await PostAsync(host, "/api/food/fasts", new { action = "delete", fast = id.ToString() });
        Assert.Equal(("fast_deleted", -1L), (deleted.GetProperty("action").GetString(), Awards(deleted).Single().Xp));

        // What each action takes.
        Assert.Contains("from and to", await RefusedAsync(host, "/api/food/fasts", new { action = "record", from = now.AddHours(-20).ToString("O") }));
        Assert.Contains("fast and from", await RefusedAsync(host, "/api/food/fasts", new { action = "edit", fast = id.ToString() }));
        Assert.Contains("takes fast", await RefusedAsync(host, "/api/food/fasts", new { action = "delete" }));
        Assert.Contains("Unknown fast action", await RefusedAsync(host, "/api/food/fasts", new { action = "pause" }));
        Assert.Contains("No fast is running", await RefusedAsync(host, "/api/food/fasts", new { action = "end" }));
    }

    [Fact]
    public async Task Closing_today_pays_its_food_once_and_settle_pays_a_day_that_closed_itself()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await db.OpenWalletAsync(daysAgo: 10);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var today = await db.TodayAsync();
        var now = DateTimeOffset.UtcNow;
        if (now - new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) < TimeSpan.FromMinutes(10))
        {
            Assert.Skip("Too close to midnight UTC for a test about today.");
        }

        // Yesterday's dinner lands with nothing settling it; opening the page settles it.
        var yesterday = today.AddDays(-1);
        await db.Get<IFoodLogRepository>().AppendAsync(
            [new NewEvent(EventKinds.Measurement, Provenances.Declared, DateTimeOffset.Parse(Iso(yesterday, 19)), now, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new { meal = new { id = Guid.NewGuid(), name = "Chicken", score = 4, calories = 900, meal_type = "dinner" } }))],
            FoodGuard.None, Ct);
        var settled = await PostAsync(host, "/api/food/settle", null);
        var award = Assert.Single(settled.GetProperty("awards").EnumerateArray());
        Assert.Equal(($"diet:{Day(yesterday)}", 25L), (award.GetProperty("awardKey").GetString(), award.GetProperty("amounts").GetProperty(Currencies.HealthXp).GetInt64()));
        Assert.Equal(0, (await PostAsync(host, "/api/food/settle", null)).GetProperty("awards").GetArrayLength());

        // Today's oatmeal pays when the day is closed, with no body sent.
        var oatmeal = await PostAsync(host, "/api/food/meals", new { name = "Oatmeal", score = 4, calories = 400, eatenAt = now.AddMinutes(-2).ToString("O") });
        Assert.Equal(0, oatmeal.GetProperty("awards").GetArrayLength());
        var closed = await PostAsync(host, "/api/food/close", null);
        Assert.Equal(("closed", Day(today)), (closed.GetProperty("action").GetString(), closed.GetProperty("day").GetString()));
        Assert.Equal([("earn", 21L)], Awards(closed));

        // Again, or a past day: nothing more.
        var again = await PostAsync(host, "/api/food/close", new { date = Day(today) });
        Assert.Equal(("already_closed", 0), (again.GetProperty("action").GetString(), again.GetProperty("awards").GetArrayLength()));
        Assert.Equal("already_closed", (await PostAsync(host, "/api/food/close", new { date = Day(yesterday) })).GetProperty("action").GetString());
        Assert.Contains("hasn't happened yet", await RefusedAsync(host, "/api/food/close", new { date = Day(today.AddDays(1)) }));

        var read = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/day", Ct);
        Assert.True(read.GetProperty("summary").GetProperty("closedByHand").GetBoolean());
        Assert.Equal(21L, read.GetProperty("summary").GetProperty("paid").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Refusals_are_400s_in_words_and_nothing_is_written()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var today = await db.TodayAsync();

        Assert.Contains("name, score and calories", await RefusedAsync(host, "/api/food/meals", new { name = "Toast", score = 3 }));
        Assert.Contains("score", await RefusedAsync(host, "/api/food/meals", new { name = "Toast", score = 6, calories = 200 }));
        Assert.Contains("5,000", await RefusedAsync(host, "/api/food/meals", new { name = "Toast", score = 3, calories = 9000 }));
        Assert.Contains("future", await RefusedAsync(host, "/api/food/meals",
            new { name = "Toast", score = 3, calories = 200, eatenAt = DateTimeOffset.UtcNow.AddHours(2).ToString("O") }));
        Assert.Contains("meal type", await RefusedAsync(host, "/api/food/meals", new { name = "Toast", score = 3, calories = 200, mealType = "brunch" }));
        Assert.Contains("No food entry", await RefusedAsync(host, "/api/food/meals/abcdef12", new { name = "Toast", score = 3, calories = 200 }));
        Assert.Contains("No food entry", await RefusedAsync(host, "/api/food/meals/abcdef12/delete", null));

        // An overlapping fast.
        var day = today.AddDays(-2);
        await PostAsync(host, "/api/food/fasts", new { action = "record", from = Iso(day, 0), to = Iso(day, 14) });
        Assert.Contains("overlap", await RefusedAsync(host, "/api/food/fasts", new { action = "record", from = Iso(day, 10), to = Iso(day, 20) }));

        // Reads: at most 62 days, in order, and 1 to 100 recent entries.
        Assert.Contains("62", await RefusedGetAsync(host, $"/api/food/days?from={Day(today.AddDays(-62))}&to={Day(today)}"));
        Assert.Contains("on or after", await RefusedGetAsync(host, $"/api/food/days?from={Day(today)}&to={Day(today.AddDays(-1))}"));
        Assert.Contains("1 to 100", await RefusedGetAsync(host, "/api/food/recent?limit=0"));

        // A whole 62 days reads, and with nothing sent, the week so far from Monday.
        var range = await host.Client.GetFromJsonAsync<JsonElement>($"/api/food/days?from={Day(today.AddDays(-61))}&to={Day(today)}", Ct);
        Assert.Equal(62, range.GetProperty("days").GetArrayLength());
        var week = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/days", Ct);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        Assert.Equal((Day(monday), Day(today)), (week.GetProperty("from").GetString(), week.GetProperty("to").GetString()));

        // Only the recorded fast was written.
        var meals = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/recent", Ct);
        Assert.Equal(0, meals.GetArrayLength());
    }
}

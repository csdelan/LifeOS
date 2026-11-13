using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// Reviews over HTTP (REVIEW-1, REVIEW-2, REVIEW-5, reviews plan R2), the twin of
/// <c>bsk review</c>: list, open-or-create, complete, keep focus and the recap. Each test has a
/// database of its own (home zone UTC); the review tests set the clock by hand.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReviewEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Wednesday = new(2026, 11, 4);
    private static readonly DateOnly Sunday = new(2026, 11, 8);

    private static async Task<(DomainTestDatabase Db, ApiTestHost Host, TestClock Clock)> StartAsync(
        PostgresFixture postgres, DateOnly? on)
    {
        var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var clock = TestClock.Ticking();
        clock.On(on ?? DateOnly.FromDateTime(DateTime.UtcNow));
        var host = await ApiTestHost.StartAsync(
            db.ConnectionString, Ct, DomainTestDatabase.ApiZone,
            services =>
            {
                if (on is not null)
                {
                    services.AddSingleton<IClock>(clock);
                }
            });
        return (db, host, clock);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<string> RefusedAsync(HttpResponseMessage response, HttpStatusCode code = HttpStatusCode.BadRequest)
    {
        Assert.Equal(code, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()!;
    }

    private static Task<HttpResponseMessage> OpenAsync(ApiTestHost host, string kind, string? date = null)
        => host.Client.PostAsJsonAsync("/api/reviews/open", new { kind, date }, Ct);

    private static string S(JsonElement row, string name) => row.GetProperty(name).GetString()!;

    [Fact]
    public async Task Opening_a_daily_review_twice_makes_one_and_the_list_shows_its_week_and_status()
    {
        var (db, host, _) = await StartAsync(postgres, Wednesday);
        await using var _db = db;
        await using var _host = host;

        var first = await OkAsync(await OpenAsync(host, "daily"));
        var again = await OkAsync(await OpenAsync(host, "Daily", "2026-11-04"));
        Assert.Equal(S(first, "id"), S(again, "id"));
        Assert.Equal(
            ("daily", "2026-11-04", "2026-11-02", "2026-11-08", "2026-11-05", "2026-11-05", "2026-11-04", "Open"),
            (S(first, "kind"), S(first, "date"), S(first, "weekStart"), S(first, "weekEnd"),
             S(first, "planStart"), S(first, "planEnd"), S(first, "dueOn"), S(first, "status")));
        Assert.Equal("Daily review, Wed Nov 4", S(first, "title"));
        Assert.True(first.GetProperty("keepFocus").GetBoolean());
        Assert.False(first.GetProperty("missed").GetBoolean());

        // Monday's, opened late: listed before Wednesday's, and missed.
        await OkAsync(await OpenAsync(host, "daily", "2026-11-02"));
        var listed = await host.Client.GetFromJsonAsync<JsonElement>("/api/reviews?from=2026-11-02&to=2026-11-08", Ct);
        Assert.Equal(["2026-11-02", "2026-11-04"], listed.EnumerateArray().Select(r => S(r, "date")));
        Assert.Equal("Missed", S(listed[0], "status"));
        Assert.Empty((await host.Client.GetFromJsonAsync<JsonElement>("/api/reviews?kind=weekly&from=2026-11-02&to=2026-11-08", Ct)).EnumerateArray());

        // The default range is the four weeks to today.
        Assert.Equal(2, (await host.Client.GetFromJsonAsync<JsonElement>("/api/reviews", Ct)).GetArrayLength());
    }

    [Fact]
    public async Task A_weekly_review_opens_on_its_Sunday_looking_back_on_its_week_and_planning_the_next()
    {
        var (db, host, clock) = await StartAsync(postgres, Wednesday);
        await using var _db = db;
        await using var _host = host;

        Assert.Contains("opens that Sunday", await RefusedAsync(await OpenAsync(host, "weekly")));

        clock.On(Sunday);
        var weekly = await OkAsync(await OpenAsync(host, "weekly"));
        Assert.Equal(
            ("Weekly review, Sun Nov 8", "2026-11-08", "2026-11-02", "2026-11-08", "2026-11-09", "2026-11-15"),
            (S(weekly, "title"), S(weekly, "date"), S(weekly, "weekStart"), S(weekly, "weekEnd"),
             S(weekly, "planStart"), S(weekly, "planEnd")));
        Assert.Equal(S(weekly, "id"), S(await OkAsync(await OpenAsync(host, "weekly", "2026-11-04")), "id"));
    }

    [Fact]
    public async Task Completing_writes_once_and_a_late_completion_reads_completed_and_missed()
    {
        var (db, host, clock) = await StartAsync(postgres, Wednesday);
        await using var _db = db;
        await using var _host = host;

        var today = await OkAsync(await OpenAsync(host, "daily"));
        var done = await OkAsync(await host.Client.PostAsync($"/api/reviews/{S(today, "id")}/complete", null, Ct));
        Assert.Equal(("Completed", false, false), (S(done, "status"), done.GetProperty("missed").GetBoolean(), done.GetProperty("completedLate").GetBoolean()));
        Assert.Equal(JsonValueKind.String, done.GetProperty("completedAt").ValueKind);
        var again = await OkAsync(await host.Client.PostAsync($"/api/reviews/{S(today, "id")}/complete", null, Ct));
        Assert.Equal(S(done, "completedAt"), S(again, "completedAt"));

        clock.On(Wednesday.AddDays(2));
        var missed = await OkAsync(await OpenAsync(host, "daily", "2026-11-05"));
        Assert.Equal(("Missed", true), (S(missed, "status"), missed.GetProperty("missed").GetBoolean()));
        var late = await OkAsync(await host.Client.PostAsync($"/api/reviews/{S(missed, "id")}/complete", null, Ct));
        Assert.Equal(("Completed", true, true), (S(late, "status"), late.GetProperty("missed").GetBoolean(), late.GetProperty("completedLate").GetBoolean()));

        Assert.Contains("No subject", await RefusedAsync(
            await host.Client.PostAsync($"/api/reviews/{Guid.NewGuid()}/complete", null, Ct), HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Keep_focus_saves_and_bad_requests_are_refused_in_words()
    {
        var (db, host, _) = await StartAsync(postgres, Wednesday);
        await using var _db = db;
        await using var _host = host;
        var review = await OkAsync(await OpenAsync(host, "daily"));

        var saved = await OkAsync(await host.Client.PostAsJsonAsync($"/api/reviews/{S(review, "id")}", new { keepFocus = false }, Ct));
        Assert.False(saved.GetProperty("keepFocus").GetBoolean());
        var untouched = await OkAsync(await host.Client.PostAsJsonAsync($"/api/reviews/{S(review, "id")}", new { }, Ct));
        Assert.False(untouched.GetProperty("keepFocus").GetBoolean());

        Assert.Contains("daily or weekly", await RefusedAsync(await OpenAsync(host, "monthly")));
        Assert.Contains("yyyy-MM-dd", await RefusedAsync(await OpenAsync(host, "daily", "next week")));
        Assert.Contains("opens that day", await RefusedAsync(await OpenAsync(host, "daily", "2026-11-05")));
        Assert.Contains("on or before", await RefusedAsync(await host.Client.GetAsync("/api/reviews?from=2026-11-08&to=2026-11-01", Ct)));
        Assert.Contains("required", await RefusedAsync(await host.Client.GetAsync("/api/reviews/recap?from=2026-11-01", Ct)));
        Assert.Contains("at most", await RefusedAsync(await host.Client.GetAsync("/api/reviews/recap?from=2026-01-01&to=2026-11-01", Ct)));
    }

    [Fact]
    public async Task The_recap_reads_what_was_recorded_over_the_days_and_leaves_reviews_out()
    {
        var (db, host, _) = await StartAsync(postgres, on: null);
        await using var _db = db;
        await using var _host = host;
        var today = await db.TodayAsync();
        var iso = today.ToString("yyyy-MM-dd");

        async Task<string> NewAsync(string type, string title, Dictionary<string, string>? attrs = null)
            => S(await OkAsync(await host.Client.PostAsJsonAsync("/api/subjects", new { type, title, attrs }, Ct)), "id");

        var shipped = await NewAsync(SubjectTypes.Task, "Ship the review page");
        await OkAsync(await host.Client.PostAsJsonAsync("/api/status", new { subject = shipped, status = "Completed" }, Ct));
        await NewAsync(SubjectTypes.Task, "File the receipts", new() { ["due"] = today.AddDays(-1).ToString("yyyy-MM-dd") });
        await NewAsync(SubjectTypes.Task, "Next month's task", new() { ["due"] = today.AddDays(30).ToString("yyyy-MM-dd") });
        await NewAsync(SubjectTypes.Appointment, "Dentist", new() { ["date"] = iso, ["start"] = "09:15" });
        var habit = await db.Kernel.CreateTrackedHabitAsync(
            "Stretch", JsonSerializer.Serialize(new { start = iso, recurrence = new { freq = "daily" } }), Ct);
        await OkAsync(await host.Client.PostAsJsonAsync("/api/adhere", new { habit = habit.Urn, state = "followed", on = iso }, Ct));
        (await host.Client.PostAsJsonAsync("/api/journal", new { subject = shipped, text = "Shipped it.\nFelt good." }, Ct)).EnsureSuccessStatusCode();
        (await host.Client.PostAsJsonAsync("/api/capture", new { text = "Call the vet" }, Ct)).EnsureSuccessStatusCode();
        var review = await OkAsync(await OpenAsync(host, "daily"));
        await OkAsync(await host.Client.PostAsync($"/api/reviews/{S(review, "id")}/complete", null, Ct));

        var recap = await host.Client.GetFromJsonAsync<JsonElement>($"/api/reviews/recap?from={iso}&to={iso}", Ct);
        var sections = recap.EnumerateArray().ToDictionary(s => S(s, "id"), s => s.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(["status", "completed", "overdue", "habits", "appointments", "journal", "inbox"], sections.Keys);

        var status = Assert.Single(sections["status"]);
        Assert.Equal(("Ship the review page", "Completed"), (S(status, "title"), S(status, "detail")));
        Assert.Equal("Ship the review page", S(Assert.Single(sections["completed"]), "title"));
        var overdue = Assert.Single(sections["overdue"]);
        Assert.Equal(("File the receipts", today.AddDays(-1).ToString("yyyy-MM-dd")), (S(overdue, "title"), S(overdue, "at")));
        var stretch = Assert.Single(sections["habits"]);
        Assert.Equal(("Stretch", "followed", iso), (S(stretch, "title"), S(stretch, "detail"), S(stretch, "at")));
        var dentist = Assert.Single(sections["appointments"]);
        Assert.Equal(("Dentist", "09:15 · Scheduled"), (S(dentist, "title"), S(dentist, "detail")));
        var journal = Assert.Single(sections["journal"]);
        Assert.Equal(("Ship the review page", "Shipped it. Felt good."), (S(journal, "title"), S(journal, "detail")));
        Assert.Equal("Call the vet", S(Assert.Single(sections["inbox"]), "title"));

        // Another day's recap is empty; overdue is as of that day.
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd");
        var before = await host.Client.GetFromJsonAsync<JsonElement>($"/api/reviews/recap?from={yesterday}&to={yesterday}", Ct);
        Assert.All(before.EnumerateArray(), s => Assert.Empty(s.GetProperty("items").EnumerateArray()));

        // The habit days for a range (REVIEW-5).
        var days = await host.Client.GetFromJsonAsync<JsonElement>($"/api/habits/occurrences?from={yesterday}&to={iso}", Ct);
        var day = Assert.Single(days.EnumerateArray());
        Assert.Equal(("Stretch", "followed"), (S(day, "habitName"), S(day, "state")));
    }
}

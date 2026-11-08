using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// CAL-1 over HTTP: <c>GET /api/appointments?on=</c> lists a day's appointments for Focus, and
/// <c>POST /api/appointments/{id}/materialize</c> makes a repeating appointment's occurrences,
/// the twin of <c>bsk materialize</c>. Each test has a database of its own.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AppointmentEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> NewAsync(ApiTestHost host, string title, Dictionary<string, string> attrs)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/api/subjects", new { type = SubjectTypes.Appointment, title, attrs }, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<string[]> TitlesOnAsync(ApiTestHost host, string day)
    {
        var rows = await host.Client.GetFromJsonAsync<JsonElement>($"/api/appointments?on={day}", Ct);
        return [.. rows.EnumerateArray().Select(r => r.GetProperty("title").GetString()!)];
    }

    private static async Task<JsonElement> MaterializeAsync(ApiTestHost host, Guid series, string through)
    {
        var response = await host.Client.PostAsJsonAsync($"/api/appointments/{series}/materialize", new { through }, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<string> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()!;
    }

    [Fact]
    public async Task A_day_lists_its_appointments_all_day_first_then_by_start_without_archived_ones()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        await NewAsync(host, "Family dinner", new() { ["date"] = "2026-11-12", ["start"] = "18:30", ["end"] = "20:00" });
        await NewAsync(host, "Dentist", new() { ["date"] = "2026-11-12", ["start"] = "09:15", ["end"] = "10:00" });
        await NewAsync(host, "Farmers market", new() { ["date"] = "2026-11-12", ["all_day"] = "true" });
        await NewAsync(host, "Haircut", new() { ["date"] = "2026-11-13", ["start"] = "12:00" });
        var archived = await NewAsync(host, "Called off", new() { ["date"] = "2026-11-12", ["start"] = "11:00" });
        (await host.Client.PostAsJsonAsync("/api/archive", new { subject = archived.ToString() }, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(["Farmers market", "Dentist", "Family dinner"], await TitlesOnAsync(host, "2026-11-12"));
        var dentist = (await host.Client.GetFromJsonAsync<JsonElement>("/api/appointments?on=2026-11-12", Ct))[1];
        Assert.Equal(("09:15", "10:00", "Scheduled"), (
            dentist.GetProperty("startTime").GetString(), dentist.GetProperty("endTime").GetString(),
            dentist.GetProperty("status").GetString()));

        Assert.Contains("yyyy-MM-dd", await RefusedAsync(await host.Client.GetAsync("/api/appointments?on=next-tuesday", Ct)));
    }

    [Fact]
    public async Task Materialize_makes_a_series_occurrences_once_each_and_they_show_on_their_days_not_the_series()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        // Mondays from 2 November 2026, 07:00-08:00.
        var series = await NewAsync(host, "Team standup", new() { ["start_date"] = "2026-11-02", ["start"] = "07:00", ["end"] = "08:00" });
        (await host.Client.PostAsJsonAsync("/api/recur", new
        {
            subject = series.ToString(),
            recurrence = new { freq = "weekly", on = new[] { "mon" } }
        }, Ct)).EnsureSuccessStatusCode();

        var made = await MaterializeAsync(host, series, "2026-11-23");
        Assert.Equal(series, made.GetProperty("series").GetProperty("id").GetGuid());
        Assert.Equal(4, made.GetProperty("created").GetArrayLength());
        Assert.All(made.GetProperty("created").EnumerateArray(), o => Assert.Equal("Team standup", o.GetProperty("title").GetString()));

        // Each occurrence shows on its day with the series' times; the series itself never does.
        var monday = Assert.Single((await host.Client.GetFromJsonAsync<JsonElement>("/api/appointments?on=2026-11-16", Ct)).EnumerateArray());
        Assert.Equal("07:00", monday.GetProperty("startTime").GetString());
        Assert.False(string.IsNullOrEmpty(monday.GetProperty("seriesUrn").GetString()));
        Assert.Empty(await TitlesOnAsync(host, "2026-11-17"));

        // Again through the same day: nothing new. Further: only the new dates.
        Assert.Equal(0, (await MaterializeAsync(host, series, "2026-11-23")).GetProperty("created").GetArrayLength());
        Assert.Equal(1, (await MaterializeAsync(host, series, "2026-11-30")).GetProperty("created").GetArrayLength());
    }

    [Fact]
    public async Task Materialize_refuses_a_missing_date_and_a_subject_that_is_not_an_appointment()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var series = await NewAsync(host, "Team standup", new() { ["start_date"] = "2026-11-02" });

        Assert.Contains("through is required", await RefusedAsync(
            await host.Client.PostAsJsonAsync($"/api/appointments/{series}/materialize", new { through = "soon" }, Ct)));

        var goal = (await (await host.Client.PostAsJsonAsync("/api/subjects", new { type = SubjectTypes.Goal, title = "Run a 10k" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        Assert.Contains("not an Appointment", await RefusedAsync(
            await host.Client.PostAsJsonAsync($"/api/appointments/{goal}/materialize", new { through = "2026-12-01" }, Ct)));
    }
}

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// The API catches up what fell due while it was stopped as it starts, before it answers anything
/// (code review CR57): the repetitions a repeating Task is owed, then the rewards. Each test has a
/// database of its own (home zone UTC) and sets the clock by hand.
/// </summary>
public sealed class StartupCatchUpTests(PostgresFixture postgres)
{
    private const string Category = "LifeOs.Api.Hosting.StartupCatchUp";

    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Dictionary<string, string?> Settings(string catchUp)
        => new(DomainTestDatabase.ApiZone) { ["CatchUp:OnStart"] = catchUp };

    private static Task<ApiTestHost> StartAsync(DomainTestDatabase db, TestClock clock, string catchUp, LogCapture? logs = null)
        => ApiTestHost.StartAsync(db.ConnectionString, Ct, Settings(catchUp), services =>
        {
            services.AddSingleton<IClock>(clock);
            logs?.AddTo(services);
        });

    private static async Task<List<string?>> OccurrencesAsync(DomainTestDatabase db, Guid series)
        => (await new SubjectReader(db.ConnectionString).GetTaskSeriesHistoryAsync(series, Ct))!
            .Repetitions.Select(r => r.Occurrence).ToList();

    [Fact]
    public async Task The_API_makes_the_repetitions_owed_and_settles_the_rewards_before_it_answers()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var clock = TestClock.Ticking();
        clock.On(Monday);

        Guid series;
        await using (var host = await StartAsync(db, clock, "false"))
        {
            var created = await host.Client.PostAsJsonAsync("/api/subjects", new
            {
                type = "Task",
                title = "Feed the fish",
                attrs = new Dictionary<string, string> { ["recurrence"] = """{"freq":"daily"}""", ["start"] = Iso(Monday) },
            }, Ct);
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync(Ct));
            series = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        }

        Assert.Equal([Iso(Monday)], await OccurrencesAsync(db, series));

        // Two days pass with the machine stopped. Started again, the API has made Tuesday's and
        // Wednesday's before anything asks: no page opened, no request sent.
        clock.On(Monday.AddDays(2));
        var logs = new LogCapture();
        await using (await StartAsync(db, clock, "true", logs))
        {
            Assert.Equal([Iso(Monday.AddDays(2)), Iso(Monday.AddDays(1)), Iso(Monday)], await OccurrencesAsync(db, series));
        }

        var said = logs.Messages(Category);
        Assert.Contains("Caught up repeating Tasks at start: 2 repetitions made for 1 repeating Tasks.", said);
        Assert.Contains(said, m => m.StartsWith("Caught up rewards at start: ", StringComparison.Ordinal));
        Assert.DoesNotContain(said, m => m.StartsWith("Couldn't", StringComparison.Ordinal));
        // Done before the web server listened, so before any request could be answered.
        var all = logs.All().ToList();
        var listening = all.FindIndex(m => m.StartsWith("Now listening on", StringComparison.Ordinal));
        Assert.True(listening > 0, "The host never said it was listening:\n" + string.Join("\n", all));
        Assert.True(
            all.FindIndex(m => m.StartsWith("Caught up rewards at start", StringComparison.Ordinal)) < listening,
            "The catch-up finished after the server listened:\n" + string.Join("\n", all));

        // Turned off, a start makes nothing: Thursday's waits for a page to open.
        clock.On(Monday.AddDays(3));
        await using (await StartAsync(db, clock, "false"))
        {
            Assert.Equal(3, (await OccurrencesAsync(db, series)).Count);
        }
    }

    [Fact]
    public async Task A_CatchUp_setting_that_isnt_true_or_false_stops_the_API_starting()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAsync(db, TestClock.Ticking(), "sometimes"));
        Assert.Equal("CatchUp:OnStart is true or false, not 'sometimes'.", error.Message);
    }
}

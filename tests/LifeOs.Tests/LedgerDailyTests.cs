using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-5 (rewards plan R7): the rewards history by local day —
/// <c>RewardService.GetDailyAsync</c> (<c>bsk ledger --daily</c>) and
/// <c>GET /api/ledger/daily</c>. Each test writes its entries on its own days in 2003, where
/// nothing else in the shared database lands, so it can assert exact totals; everything a
/// test spends it first earns, so the shared wallet never drops.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerDailyTests(PostgresFixture postgres)
{
    private const string Chicago = "America/Chicago";

    // Chicago in January and February: CST, six hours behind UTC.
    private static readonly TimeSpan Cst = TimeSpan.FromHours(-6);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Kernel()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    // Stored as the instant it is (Npgsql writes only UTC offsets).
    private static DateTimeOffset InChicago(DateOnly day, int hour, int minute)
        => new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), Cst).ToUniversalTime();

    /// <summary>
    /// Appends one bare ledger entry dated <paramref name="at"/> — no item, no award key: an
    /// earn or reversal cites a note as its cause; a redemption is declared, with a note.
    /// </summary>
    private static async Task AppendAsync(
        ServiceProvider provider, string entry, IReadOnlyDictionary<string, long> amounts, DateTimeOffset at)
    {
        var declared = entry == LedgerEntries.Redeem;
        IReadOnlyList<Guid>? derivedFrom = null;
        if (!declared)
        {
            var cause = await provider.GetRequiredService<IEventStore>().AppendAsync(
                new NewEvent(EventKinds.Note, Provenances.Declared, at, DateTimeOffset.UtcNow, KernelSources.Cli,
                    PayloadJson: """{"text":"a daily history test"}"""),
                Ct);
            derivedFrom = [cause];
        }

        var payload = declared
            ? JsonSerializer.Serialize(new { entry, amounts, note = "a daily history test" })
            : JsonSerializer.Serialize(new { entry, amounts });
        var ledgerEvent = new NewEvent(
            EventKinds.Ledger, declared ? Provenances.Declared : Provenances.Derived, at, DateTimeOffset.UtcNow,
            KernelSources.Cli, PayloadJson: payload, DerivedFrom: derivedFrom);
        await provider.GetRequiredService<ILedgerRepository>().AppendAsync(
            [new LedgerWrite(ledgerEvent, [], GuardBalance: false)], Ct);
    }

    /// <summary>
    /// Two Chicago days: <paramref name="day"/> earns in the morning and again at 11:30 pm —
    /// after midnight UTC — and the day after gives 3 XP back and spends the 2 Coins.
    /// </summary>
    private static async Task SeedAsync(ServiceProvider provider, DateOnly day)
    {
        await AppendAsync(provider, LedgerEntries.Earn,
            new Dictionary<string, long> { [Currencies.Xp] = 10, [Currencies.Coins] = 2 }, InChicago(day, 8, 0));
        await AppendAsync(provider, LedgerEntries.Earn,
            new Dictionary<string, long> { [Currencies.Xp] = 5 }, InChicago(day, 23, 30));
        await AppendAsync(provider, LedgerEntries.Reversal,
            new Dictionary<string, long> { [Currencies.Xp] = -3 }, InChicago(day.AddDays(1), 9, 0));
        await AppendAsync(provider, LedgerEntries.Redeem,
            new Dictionary<string, long> { [Currencies.Coins] = -2 }, InChicago(day.AddDays(1), 10, 0));
    }

    /// <summary>"coins=2,xp=15": the amounts in the order they came.</summary>
    private static string Text(IReadOnlyDictionary<string, long> amounts)
        => string.Join(",", amounts.Select(kv => $"{kv.Key}={kv.Value}"));

    private static string Text(JsonElement amounts)
        => string.Join(",", amounts.EnumerateObject().Select(p => $"{p.Name}={p.Value.GetInt64()}"));

    [Fact]
    public async Task Each_entry_is_filed_on_the_day_it_happened_where_the_viewer_is()
    {
        await using var provider = Kernel();
        var day = new DateOnly(2003, 1, 15);
        await SeedAsync(provider, day);
        var rewards = provider.GetRequiredService<RewardService>();

        var chicago = await rewards.GetDailyAsync(day, day.AddDays(2), Chicago, Ct);

        Assert.Equal([day, day.AddDays(1)], chicago.Select(d => d.Date));
        Assert.Equal(("coins=2,xp=15", "", "", "coins=2,xp=15"),
            (Text(chicago[0].Earned), Text(chicago[0].Reversed), Text(chicago[0].Spent), Text(chicago[0].Net)));
        Assert.Equal(("", "xp=3", "coins=2", "coins=-2,xp=-3"),
            (Text(chicago[1].Earned), Text(chicago[1].Reversed), Text(chicago[1].Spent), Text(chicago[1].Net)));

        // Read in UTC, the 11:30 pm earn is already the next day.
        var utc = await rewards.GetDailyAsync(day, day.AddDays(2), "UTC", Ct);
        Assert.Equal(("coins=2,xp=10", "xp=5", "coins=-2,xp=2"),
            (Text(utc[0].Earned), Text(utc[1].Earned), Text(utc[1].Net)));

        // The range ends before its last day: [from, to).
        var first = Assert.Single(await rewards.GetDailyAsync(day, day.AddDays(1), Chicago, Ct));
        Assert.Equal(day, first.Date);
    }

    [Fact]
    public async Task A_backwards_or_too_long_range_or_an_unknown_zone_is_refused()
    {
        await using var provider = Kernel();
        var rewards = provider.GetRequiredService<RewardService>();
        var day = new DateOnly(2003, 3, 1);

        var backwards = await Assert.ThrowsAsync<ArgumentException>(() => rewards.GetDailyAsync(day, day, Chicago, Ct));
        var tooLong = await Assert.ThrowsAsync<ArgumentException>(
            () => rewards.GetDailyAsync(day, day.AddDays(LedgerDays.MaxRangeDays + 1), Chicago, Ct));
        var unknown = await Assert.ThrowsAsync<ArgumentException>(
            () => rewards.GetDailyAsync(day, day.AddDays(1), "Mars/Olympus", Ct));
        var spaced = await Assert.ThrowsAsync<ArgumentException>(
            () => rewards.GetDailyAsync(day, day.AddDays(1), "America/ Chicago", Ct));

        Assert.Equal("The history range must end after it starts.", backwards.Message);
        Assert.Equal("The history range can span at most 731 days; this one spans 732.", tooLong.Message);
        Assert.Equal("Unknown time zone 'Mars/Olympus'.", unknown.Message);
        Assert.Equal("Unknown time zone 'America/ Chicago'.", spaced.Message);

        // Two years exactly is allowed.
        await rewards.GetDailyAsync(day, day.AddDays(LedgerDays.MaxRangeDays), Chicago, Ct);
    }

    [Fact]
    public async Task The_daily_endpoint_reads_days_in_the_viewers_zone()
    {
        await using var provider = Kernel();
        var day = new DateOnly(2003, 2, 12);
        await SeedAsync(provider, day);
        await using var host = await ApiTestHost.StartAsync(postgres.ConnectionString, Ct);

        var days = (await host.Client.GetFromJsonAsync<JsonElement>(
            "/api/ledger/daily?from=2003-02-12&to=2003-02-14&tz=America/Chicago", Ct)).EnumerateArray().ToList();

        Assert.Equal(["2003-02-12", "2003-02-13"], days.Select(d => d.GetProperty("date").GetString()));
        Assert.Equal(("coins=2,xp=15", ""), (Text(days[0].GetProperty("earned")), Text(days[0].GetProperty("spent"))));
        Assert.Equal(("xp=3", "coins=2", "coins=-2,xp=-3"),
            (Text(days[1].GetProperty("reversed")), Text(days[1].GetProperty("spent")), Text(days[1].GetProperty("net"))));

        // Without a zone the days are UTC's: the evening earn moves to the next day.
        var utc = (await host.Client.GetFromJsonAsync<JsonElement>(
            "/api/ledger/daily?from=2003-02-12&to=2003-02-14", Ct)).EnumerateArray().ToList();
        Assert.Equal("xp=5", Text(utc[1].GetProperty("earned")));
    }

    [Fact]
    public async Task The_daily_endpoint_refuses_a_missing_or_bad_range_and_an_unknown_zone()
    {
        await using var host = await ApiTestHost.StartAsync(postgres.ConnectionString, Ct);

        async Task<(HttpStatusCode, string)> Refused(string path)
        {
            var response = await host.Client.GetAsync(path, Ct);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            return (response.StatusCode, json.GetProperty("error").GetString() ?? "");
        }

        Assert.Equal((HttpStatusCode.BadRequest, "from and to are required, as yyyy-MM-dd."),
            await Refused("/api/ledger/daily?from=2003-02-12"));
        Assert.Equal((HttpStatusCode.BadRequest, "The history range must end after it starts."),
            await Refused("/api/ledger/daily?from=2003-02-12&to=2003-02-12&tz=America/Chicago"));
        Assert.Equal((HttpStatusCode.BadRequest, "Unknown time zone 'Mars/Olympus'."),
            await Refused("/api/ledger/daily?from=2003-02-12&to=2003-02-13&tz=Mars/Olympus"));
    }
}

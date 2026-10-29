using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// A migrated database of its own for one life-domain test, dropped afterwards (xp-domains plan
/// X4–X5). A wallet opens once and a rank pays once per database, and a domain's multiplier
/// reads every Health day in it, so these tests can't share the fixture's database. Its days
/// are in UTC, the home zone the kernel and the API are given here.
/// </summary>
internal sealed class DomainTestDatabase : IAsyncDisposable
{
    public const string Zone = "UTC";

    /// <summary>The API's settings for this zone (<see cref="ApiTestHost.StartAsync"/>).</summary>
    public static readonly Dictionary<string, string?> ApiZone = new() { [HomeZone.ConfigurationKey] = Zone };

    private readonly string adminConnectionString;
    private readonly string name;
    private readonly CancellationToken ct;

    private DomainTestDatabase(
        string adminConnectionString, string name, string connectionString, string zone, CancellationToken ct)
    {
        this.adminConnectionString = adminConnectionString;
        this.name = name;
        this.ct = ct;
        ConnectionString = connectionString;
        Kernel = new ServiceCollection().AddLifeOsKernel(connectionString, homeTimeZone: zone).BuildServiceProvider();
    }

    public string ConnectionString { get; }

    public ServiceProvider Kernel { get; }

    public T Get<T>() where T : notnull => Kernel.GetRequiredService<T>();

    /// <summary>
    /// A new database. <paramref name="zone"/> is its home zone (UTC unless a test is about
    /// another), set both in the database (<c>bsk.home_zone</c>) and on the kernel.
    /// </summary>
    public static async Task<DomainTestDatabase> CreateAsync(PostgresFixture postgres, CancellationToken ct, string zone = Zone)
    {
        var name = $"domain_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync(ct);
            await admin.ExecuteAsync(new CommandDefinition($"CREATE DATABASE {name};", cancellationToken: ct));
            await admin.ExecuteAsync(new CommandDefinition(
                $"ALTER DATABASE {name} SET bsk.home_zone = '{zone}';", cancellationToken: ct));
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString;
        await new MigrationRunner(connectionString).ApplyAsync(ct);
        return new DomainTestDatabase(postgres.ConnectionString, name, connectionString, zone, ct);
    }

    /// <summary>Today in the home zone.</summary>
    public Task<DateOnly> TodayAsync() => Get<IHomeCalendar>().DateAtAsync(DateTimeOffset.UtcNow, ct);

    /// <summary>Opens the wallet <paramref name="daysAgo"/> days ago.</summary>
    public Task OpenWalletAsync(int daysAgo) => Get<RewardService>().OpenAsync(DateTimeOffset.UtcNow.AddDays(-daysAgo), ct);

    /// <summary>A daily habit each followed day of which pays <paramref name="healthXp"/> Health XP.</summary>
    public async Task<SubjectRef> HabitAsync(DateOnly today, int startOffset, long healthXp)
        => await HabitAsync(today, startOffset, new Dictionary<string, long> { [Currencies.HealthXp] = healthXp });

    /// <summary>
    /// A daily habit started <paramref name="startOffset"/> days before <paramref name="today"/>,
    /// each followed day priced at <paramref name="price"/>.
    /// </summary>
    public async Task<SubjectRef> HabitAsync(DateOnly today, int startOffset, Dictionary<string, long> price)
    {
        // Tracked from its start (TestHabits).
        var habit = await Get<ISubjectRepository>().CreateTrackedHabitAsync(
            $"domain habit {Guid.NewGuid():N}",
            JsonSerializer.Serialize(new
            {
                start = today.AddDays(-startOffset).ToString("yyyy-MM-dd"),
                recurrence = new { freq = "daily" }
            }),
            ct);
        await Get<BountyService>().SetAsync(habit.Urn, BountyTriggers.Followed, price, cancellationToken: ct);
        return habit;
    }

    /// <summary>
    /// Health XP (or another <paramref name="currency"/>) credited to <paramref name="day"/>
    /// straight into the ledger, with no reconcile after it — as if time alone, not a write, had
    /// moved things on since.
    /// </summary>
    public async Task CreditAsync(DateOnly day, long xp, string currency = Currencies.HealthXp)
    {
        var key = $"test:{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            entry = LedgerEntries.Earn,
            amounts = new Dictionary<string, long> { [currency] = xp },
            award_key = key,
            trigger = "test",
            credited_on = day.ToString("yyyy-MM-dd")
        });
        await Get<ILedgerRepository>().AppendAsync(
            [new LedgerWrite(
                new NewEvent(EventKinds.Ledger, Provenances.Declared, now, now, KernelSources.Cli, PayloadJson: payload),
                [], GuardBalance: false, AwardKey: key, ExpectPaid: false)],
            ct);
    }

    /// <summary><paramref name="xp"/> Health XP credited to each day from <paramref name="fromAgo"/> down to <paramref name="toAgo"/> days before <paramref name="today"/>.</summary>
    public async Task CreditDaysAsync(DateOnly today, int fromAgo, int toAgo, long xp)
    {
        for (var ago = fromAgo; ago >= toAgo; ago--)
        {
            await CreditAsync(today.AddDays(-ago), xp);
        }
    }

    /// <summary>The payload of the ledger event <paramref name="eventId"/>.</summary>
    public async Task<JsonElement> PayloadAsync(Guid eventId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        var json = await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT payload::text FROM bsk.event WHERE id = @eventId;", new { eventId }, cancellationToken: ct));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await Kernel.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS {name} WITH (FORCE);");
    }
}

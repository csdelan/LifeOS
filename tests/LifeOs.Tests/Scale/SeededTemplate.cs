using System.Diagnostics;
using Dapper;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests.Scale;

/// <summary>
/// The migrated template with three years of made-up use in it (code review R13): meals and
/// fasts, Oura days sent again and again, habit check-offs, repeating and one-off Tasks with
/// their timers, the ledger and its awards, the Inbox's captures and their triage, journal
/// entries, room ratings, metric readings and reviews (<c>Scale/seed.sql</c>). It's made once a
/// run, the first time a test asks for it, and copied for each test as the migrated template is
/// (<see cref="DomainTestDatabase.CreateSeededAsync"/>). Nothing stays connected to it.
/// </summary>
internal sealed class SeededTemplate
{
    /// <summary>The seeded database every seeded copy is made from.</summary>
    public const string Database = "lifeos_seeded";

    /// <summary>Its home zone, the zone the seed's days are in.</summary>
    public const string Zone = "UTC";

    private SeededTemplate(IReadOnlyDictionary<string, long> paidOnBuild, TimeSpan took)
    {
        PaidOnBuild = paidOnBuild;
        Took = took;
    }

    /// <summary>
    /// What the API's settle (<see cref="SettlementService"/>, as it starts) paid once the seed was
    /// in, by trigger: the food and activity days (<c>diet</c>, <c>fast</c>, <c>steps</c>,
    /// <c>exercise</c>, <c>sleep</c>) and the life domains' rank-ups (<c>rank</c>), and nothing
    /// for an item, whose awards the seed pays.
    /// </summary>
    public IReadOnlyDictionary<string, long> PaidOnBuild { get; }

    /// <summary>How long the build took.</summary>
    public TimeSpan Took { get; }

    /// <summary>The seed, run on a copy of the migrated template.</summary>
    public static string SeedSql { get; } = ReadSeed();

    internal static async Task<SeededTemplate> BuildAsync(string adminConnectionString)
    {
        var clock = Stopwatch.StartNew();
        await PostgresFixture.CopyAsync(adminConnectionString, Database, Zone, CancellationToken.None);

        // Unpooled, so nothing is left connected to it to stop a copy.
        var connectionString = Unpooled(PostgresFixture.On(adminConnectionString, Database));
        await RunAsync(connectionString, SeedSql);
        var seededAt = await ScalarAsync<DateTime>(connectionString, "SELECT clock_timestamp();");

        // The API's settle, as it runs on starting: it pays every food and activity day at the
        // kernel's own prices, and the rank-ups the domains reached. Anything else it pays is an
        // item's award the seed missed. (Every item's own reconcile, `bsk rewards reconcile`,
        // takes over a minute here, so SeedTests checks the items' awards in SQL instead.)
        IReadOnlyList<Award> paid;
        await using (var kernel = new ServiceCollection()
                         .AddLifeOsKernel(connectionString, KernelSources.Api, homeTimeZone: Zone)
                         .BuildServiceProvider())
        {
            paid = (await kernel.GetRequiredService<SettlementService>().SettleAsync()).Awards;
        }

        await RunAsync(connectionString, Redate, new { seededAt });
        await RunAsync(connectionString, "VACUUM (ANALYZE);");
        clock.Stop();
        return new SeededTemplate(
            paid.GroupBy(a => a.Trigger).ToDictionary(g => g.Key, g => (long)g.Count()),
            clock.Elapsed);
    }

    /// <summary><paramref name="connectionString"/> with no pool and no command timeout.</summary>
    internal static string Unpooled(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, CommandTimeout = 0 }.ConnectionString;

    internal static async Task RunAsync(string connectionString, string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, commandTimeout: 0));
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<T>(sql);
    }

    // The day awards the settle just paid are recorded as each was paid as its day closed, not
    // all at once today, as three years of settles leave them; the log can't be changed but by
    // a session that skips its triggers, which only this build does. Then the log is put in the
    // order it was recorded, as years of appends leave it, so a read of a week finds that week's
    // rows together and a fold of one stream finds its rows spread through the rest.
    private const string Redate = """
        SET session_replication_role = replica;
        UPDATE bsk.event SET recorded_at = occurred_at + interval '5 minutes'
        WHERE kind = 'ledger' AND recorded_at >= @seededAt AND occurred_at < @seededAt - interval '5 minutes';
        SET session_replication_role = origin;
        CLUSTER bsk.event USING event_recorded_at_idx;
        """;

    private static string ReadSeed()
    {
        using var stream = typeof(SeededTemplate).Assembly.GetManifestResourceStream("Scale.seed.sql")
                           ?? throw new InvalidOperationException("Scale/seed.sql isn't embedded in the test assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

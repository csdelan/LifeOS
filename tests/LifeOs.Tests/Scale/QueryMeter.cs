using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LifeOs.Tests.Scale;

/// <summary>
/// What a read or a write costs the database, statement by statement (code review R13). A kernel
/// or a reader kernel from <see cref="Kernel"/> or <see cref="Readers"/> reads and writes through
/// a data source whose sessions load <c>auto_explain</c>, which sends the plan of every statement
/// run, nested ones (a function's) among them, with the rows each node read and the buffers it
/// touched, back to the client as a notice. Npgsql logs each notice it receives to this logger,
/// which keeps the plans of the statements run inside <see cref="MeasureAsync{T}"/> and ignores
/// the rest. Only the measured call's own async flow is listened to, so tests running alongside
/// cost nothing and add nothing.
/// </summary>
internal sealed partial class QueryMeter : ILoggerProvider, ILogger
{
    private const string PlanMarker = "plan:\n";

    private static readonly AsyncLocal<ConcurrentQueue<string>?> Current = new();
    private static readonly ILoggerFactory Factory
        = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(new QueryMeter()));

    private QueryMeter()
    {
    }

    /// <summary>A kernel on <paramref name="db"/>, in its zone, that measures.</summary>
    public static ServiceProvider Kernel(DomainTestDatabase db) => Metered(db.Services(KernelSources.Api), db.ConnectionString);

    /// <summary>The API's readers on <paramref name="db"/>, in its zone, that measure.</summary>
    public static ServiceProvider Readers(DomainTestDatabase db)
        => Metered(
            ReaderKernel.Services(
                new KernelSettings(db.ConnectionString) { HomeTimeZone = db.ZoneName }, db.ConnectionString,
                new SystemClock(), objectStorage: null),
            db.ConnectionString);

    /// <summary>
    /// <paramref name="services"/> built with a data source of their own on
    /// <paramref name="connectionString"/>, in place of the kernel's shared one, whose sessions
    /// send their plans to the meter: analysed for rows and buffers but not timed node by node.
    /// The provider disposes it. One connection is opened first, so what Npgsql reads of the
    /// server's types as it first connects isn't measured.
    /// </summary>
    private static ServiceProvider Metered(IServiceCollection services, string connectionString)
    {
        var provider = Build(services, connectionString);
        using (provider.GetRequiredService<NpgsqlDataSource>().OpenConnection())
        {
        }

        return provider;
    }

    private static ServiceProvider Build(IServiceCollection services, string connectionString)
        => services
            .AddSingleton(_ => new NpgsqlDataSourceBuilder(new NpgsqlConnectionStringBuilder(connectionString)
                {
                    Options = string.Join(' ',
                        "-c session_preload_libraries=auto_explain",
                        "-c auto_explain.log_min_duration=0",
                        "-c auto_explain.log_analyze=on",
                        "-c auto_explain.log_buffers=on",
                        "-c auto_explain.log_timing=off",
                        "-c auto_explain.log_nested_statements=on",
                        "-c auto_explain.log_format=json",
                        "-c auto_explain.log_level=notice"),
                }.ConnectionString)
                .UseLoggerFactory(Factory)
                .Build())
            .BuildServiceProvider();

    /// <summary>Runs <paramref name="call"/> and answers what it returned with what it cost.</summary>
    public static async Task<(T Result, Cost Cost)> MeasureAsync<T>(Func<Task<T>> call)
    {
        var plans = new ConcurrentQueue<string>();
        Current.Value = plans;
        var clock = Stopwatch.StartNew();
        try
        {
            var result = await call();
            clock.Stop();
            return (result, Cost.From(plans, clock.Elapsed));
        }
        finally
        {
            Current.Value = null;
        }
    }

    /// <summary>Runs <paramref name="call"/> and answers what it cost.</summary>
    public static async Task<Cost> MeasureAsync(Func<Task> call)
        => (await MeasureAsync(async () =>
        {
            await call();
            return true;
        })).Cost;

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => Current.Value is not null && logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (Current.Value is { } plans && formatter(state, exception) is var text && text.Contains(PlanMarker, StringComparison.Ordinal))
        {
            plans.Enqueue(text);
        }
    }

    public void Dispose()
    {
    }

    [GeneratedRegex(@"duration: ([0-9.]+) ms")]
    private static partial Regex Duration();

    [GeneratedRegex(@"@\w+|\$\d+")]
    private static partial Regex Parameter();

    /// <summary>
    /// One statement as <c>auto_explain</c> planned it: its text, how long the server took, the
    /// rows of <c>bsk.event</c> its scans read, the rows every scan node read (a CTE's, a
    /// function's and an index's too), the buffers it touched, whether it read the log end to
    /// end, and the log's indexes it used.
    /// </summary>
    internal sealed record Statement(
        string Query, double Milliseconds, long EventRowsRead, long RowsScanned, long Buffers, bool ScansEvent,
        IReadOnlyList<string> EventIndexes);

    /// <summary>What one call cost: each statement it ran, and how long it took from the test's side.</summary>
    internal sealed record Cost(IReadOnlyList<Statement> Statements, TimeSpan Elapsed)
    {
        /// <summary>The statements run, nested ones (a function's) among them.</summary>
        public int Count => Statements.Count;

        /// <summary>
        /// The rows of <c>bsk.event</c> every scan of it read: those it returned and those its
        /// filter or recheck threw away, times each scan's loops.
        /// </summary>
        public long EventRowsRead => Statements.Sum(s => s.EventRowsRead);

        /// <summary>
        /// The rows every scan node read, of any table, CTE, function or subquery, times its loops:
        /// a subquery run once per row of another shows here as their product.
        /// </summary>
        public long RowsScanned => Statements.Sum(s => s.RowsScanned);

        /// <summary>The shared buffers the statements touched (a function's counted again in its caller's).</summary>
        public long Buffers => Statements.Sum(s => s.Buffers);

        /// <summary>The statements that read <c>bsk.event</c> end to end.</summary>
        public IReadOnlyList<Statement> FullScans => Statements.Where(s => s.ScansEvent).ToList();

        /// <summary>
        /// How many times <paramref name="sql"/> ran: the statements whose text is its text, white
        /// space aside and each parameter (<c>@name</c> in the kernel's text, <c>$1</c> as Npgsql
        /// sends it) as any other.
        /// </summary>
        public int CountOf(string sql)
        {
            var text = Normalized(sql);
            return Statements.Count(s => Normalized(s.Query) == text);
        }

        private static string Normalized(string sql)
            => Parameter().Replace(OneLine(sql, int.MaxValue), "?").TrimEnd(';', ' ');

        /// <summary>
        /// The totals, then a line per distinct statement (how many times it ran, its server time,
        /// the event rows, scanned rows and buffers it read in all, whether it read the log end to
        /// end, the log's indexes it used), costliest first, for a failure's message.
        /// </summary>
        public string Describe(int top = 12)
            => $"{Count} statements, {EventRowsRead} event rows read, {RowsScanned} rows scanned, {Buffers} buffers, "
               + $"{Elapsed.TotalMilliseconds:F0} ms\n"
               + string.Join('\n', Statements
                   .GroupBy(s => OneLine(s.Query, 140))
                   .Select(g => (Query: g.Key, Runs: g.Count(), Ms: g.Sum(s => s.Milliseconds), Rows: g.Sum(s => s.EventRowsRead),
                                 Scanned: g.Sum(s => s.RowsScanned), Buffers: g.Sum(s => s.Buffers),
                                 Full: g.Any(s => s.ScansEvent), Indexes: g.SelectMany(s => s.EventIndexes).Distinct()))
                   .OrderByDescending(g => g.Scanned).ThenByDescending(g => g.Buffers)
                   .Take(top)
                   .Select(g => $"  {g.Runs,4}x {g.Ms,8:F1} ms {g.Rows,7} event {g.Scanned,8} scanned {g.Buffers,6} buf "
                                + $"{(g.Full ? "FULL " : "")}{string.Join(',', g.Indexes)}  {g.Query}"));

        internal static Cost From(IEnumerable<string> notices, TimeSpan elapsed) => new(notices.Select(Parse).ToList(), elapsed);

        private static string OneLine(string query, int length)
        {
            var line = string.Join(' ', query.Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
            return line.Length > length ? line[..length] : line;
        }

        // "duration: 1.234 ms  plan:", then the plan as JSON.
        private static Statement Parse(string notice)
        {
            var at = notice.IndexOf(PlanMarker, StringComparison.Ordinal);
            var duration = Duration().Match(notice[..at]);
            var milliseconds = duration.Success ? double.Parse(duration.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            using var document = JsonDocument.Parse(notice[(at + PlanMarker.Length)..]);
            var root = document.RootElement;
            var query = root.TryGetProperty("Query Text", out var text) ? text.GetString() ?? "" : "";
            var plan = root.GetProperty("Plan");
            long eventRows = 0, scanned = 0;
            var full = false;
            var indexes = new List<string>();
            Walk(plan);
            return new Statement(
                query, milliseconds, eventRows, scanned, Long(plan, "Shared Hit Blocks") + Long(plan, "Shared Read Blocks"),
                full, indexes.Distinct().ToList());

            void Walk(JsonElement node)
            {
                var type = node.GetProperty("Node Type").GetString() ?? "";
                if (type.EndsWith("Scan", StringComparison.Ordinal) && type != "Bitmap Index Scan")
                {
                    // Each count is the average of the node's loops.
                    var read = (long)Math.Round(
                        (Number(node, "Actual Rows") + Number(node, "Rows Removed by Filter") + Number(node, "Rows Removed by Index Recheck"))
                        * Math.Max(1, Number(node, "Actual Loops")));
                    scanned += read;
                    if (node.TryGetProperty("Relation Name", out var relation) && relation.GetString() == "event"
                        && (!node.TryGetProperty("Schema", out var schema) || schema.GetString() == "bsk"))
                    {
                        eventRows += read;
                        full |= type is "Seq Scan" or "Parallel Seq Scan";
                        if (node.TryGetProperty("Index Name", out var index) && index.GetString() is { } name)
                        {
                            indexes.Add(name);
                        }
                    }
                }

                if (node.TryGetProperty("Plans", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                    {
                        Walk(child);
                    }
                }
            }
        }

        private static double Number(JsonElement node, string name)
            => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

        private static long Long(JsonElement node, string name) => (long)Math.Round(Number(node, name));
    }
}

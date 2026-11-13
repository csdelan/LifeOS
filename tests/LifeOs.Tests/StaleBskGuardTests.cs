using System.CommandLine;
using System.Diagnostics;
using Dapper;
using LifeOs.Cli;
using LifeOs.Cli.Commands;
using LifeOs.Infrastructure.Migrations;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR66: a <c>bsk</c> older than the database it's pointed at won't run against it,
/// so the Pilot's installed copy can't write by stale rules after a push migrates staging.
/// </summary>
public sealed class StaleBskGuardTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Option<string?> Connection = new("--connection", "-c") { Recursive = true };

    private static ParseResult Parse(string connectionString, params string[] args)
    {
        var json = new Option<bool>("--json") { Recursive = true };
        var root = new RootCommand("bsk");
        root.Options.Add(Connection);
        root.Options.Add(json);
        root.Subcommands.Add(MigrateCommand.Create(Connection, json));
        root.Subcommands.Add(CheckCommand.Create(Connection, json));
        root.Subcommands.Add(NewCommand.Create(Connection, json));
        root.Subcommands.Add(JournalCommand.Create(Connection, json));
        return root.Parse([.. args, "--connection", connectionString]);
    }

    /// <summary>
    /// bsk.dll in its own build output, built alongside these tests (same configuration and
    /// framework): the copy beside the test binaries runs with the tests' dependencies, not its own.
    /// </summary>
    private static string BskBuild()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var path = Path.Combine(RepoRoot.Path, "src", "LifeOs.Cli", "bin", framework.Parent!.Name, framework.Name, "bsk.dll");
        Assert.True(File.Exists(path), $"No bsk build at {path}.");
        return path;
    }

    private static async Task FromTheFutureAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO public.schema_migrations (version, name, checksum) VALUES (99999, 'from_a_newer_main', 'x');",
            cancellationToken: Ct));
    }

    [Fact]
    public async Task On_a_database_at_its_own_migrations_every_verb_runs()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);

        Assert.Equal(SchemaVersion.Shipped, (await SchemaVersion.NewestAppliedAsync(db.ConnectionString, Ct))!.Version);
        Assert.Null(await StaleBskGuard.RefusalAsync(Parse(db.ConnectionString, "new", "Task", "Fix the gate"), Connection, Ct));
    }

    [Fact]
    public async Task On_a_newer_database_it_refuses_every_verb_but_migrate_check_and_help()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await FromTheFutureAsync(db.ConnectionString);

        var refusal = await StaleBskGuard.RefusalAsync(Parse(db.ConnectionString, "new", "Task", "Fix the gate"), Connection, Ct);
        Assert.NotNull(refusal);
        Assert.Contains($"through {SchemaVersion.Shipped:D4}", refusal, StringComparison.Ordinal);
        Assert.Contains("99999 (from_a_newer_main)", refusal, StringComparison.Ordinal);
        Assert.Contains("install-bsk.ps1", refusal, StringComparison.Ordinal);
        Assert.NotNull(await StaleBskGuard.RefusalAsync(Parse(db.ConnectionString, "journal", "Gate", "--text", "Oiled it"), Connection, Ct));

        Assert.Null(await StaleBskGuard.RefusalAsync(Parse(db.ConnectionString, "migrate"), Connection, Ct));
        Assert.Null(await StaleBskGuard.RefusalAsync(Parse(db.ConnectionString, "check"), Connection, Ct));
        Assert.Null(await StaleBskGuard.RefusalAsync(Parse(db.ConnectionString, "new", "--help"), Connection, Ct));
    }

    [Fact]
    public async Task A_database_it_cant_reach_or_never_migrated_is_left_to_the_verb()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.ExecuteAsync(new CommandDefinition("DROP TABLE public.schema_migrations;", cancellationToken: Ct));
        }

        Assert.Null(await StaleBskGuard.RefusalAsync(Parse(db.ConnectionString, "new", "Task", "Fix the gate"), Connection, Ct));
        Assert.Null(await StaleBskGuard.RefusalAsync(
            Parse("Host=127.0.0.1;Port=1;Database=nowhere;Username=x;Password=y;Timeout=2", "new", "Task", "Fix the gate"), Connection, Ct));
    }

    [Fact]
    public async Task Bsk_itself_writes_nothing_to_a_newer_database_and_says_why()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await FromTheFutureAsync(db.ConnectionString);
        var title = $"Fix the gate {Guid.NewGuid():N}";

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { BskBuild(), "new", "Task", title, "--connection", db.ConnectionString })
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment.Remove("BSK_CONNECTION_STRING");
        using var process = Process.Start(start)!;
        var stderr = await process.StandardError.ReadToEndAsync(Ct);
        await process.StandardOutput.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("older than the database", stderr, StringComparison.Ordinal);
        await using var check = new NpgsqlConnection(db.ConnectionString);
        Assert.False(await check.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.subject WHERE title = @title);", new { title }, cancellationToken: Ct)));
    }
}

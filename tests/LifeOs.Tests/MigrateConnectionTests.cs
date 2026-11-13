using System.Diagnostics;
using LifeOs.Infrastructure;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR59: which database <c>scripts/migrate-staging.ps1</c> (and
/// <c>migrate-production.ps1</c>, which names <c>.env.prod</c>) migrates. Before the fix a
/// <c>BSK_CONNECTION_STRING</c> left in the shell beat the file the caller named, so a staging
/// string in a terminal sent production's migrations to staging. These run the script's module in
/// <c>pwsh</c>, which Chris's PCs and CI's Ubuntu runners both have.
/// </summary>
public sealed class MigrateConnectionTests : IDisposable
{
    private const string Staging =
        "Host=aws-0-us-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.stagingref;Password=staging-pw;SSL Mode=Require";

    private const string Production =
        "Host=aws-0-us-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.prodref;Password=prod-pw;SSL Mode=Require";

    private readonly string _folder = Directory.CreateTempSubdirectory("lifeos-migrate-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_named_env_file_wins_over_a_connection_string_left_in_the_shell()
    {
        var prod = WriteEnv(".env.prod", $"BSK_CONNECTION_STRING={Production}");

        var run = await ResolveAsync(prod, ".env.prod", named: true, ambient: Staging);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("postgres.prodref@aws-0-us-west-1.pooler.supabase.com:5432/postgres", run.Target);
        Assert.Contains("Ignoring BSK_CONNECTION_STRING in this shell (postgres.stagingref@", run.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shell_and_the_default_env_file_naming_different_databases_is_refused()
    {
        var env = WriteEnv(".env", $"BSK_CONNECTION_STRING={Staging}");

        var run = await ResolveAsync(env, ".env", named: false, ambient: Production);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Pass -EnvFile to choose the file", run.All, StringComparison.Ordinal);
        Assert.DoesNotContain("prod-pw", run.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shell_is_used_when_there_is_no_env_file_or_it_agrees()
    {
        var missing = Path.Combine(_folder, ".env");
        var alone = await ResolveAsync(missing, ".env", named: false, ambient: Staging);
        Assert.Equal("postgres.stagingref@aws-0-us-west-1.pooler.supabase.com:5432/postgres", alone.Target);

        var env = WriteEnv(".env", $"BSK_CONNECTION_STRING={Staging}");
        var agreeing = await ResolveAsync(env, ".env", named: false, ambient: Staging);
        Assert.Equal("postgres.stagingref@aws-0-us-west-1.pooler.supabase.com:5432/postgres", agreeing.Target);
    }

    [Fact]
    public async Task Without_the_shell_the_env_file_is_read_with_its_quotes_dropped()
    {
        var env = WriteEnv(".env", "# staging", $"BSK_CONNECTION_STRING='{Staging}'");

        var run = await ResolveAsync(env, ".env", named: false, ambient: null);

        Assert.Equal("postgres.stagingref@aws-0-us-west-1.pooler.supabase.com:5432/postgres", run.Target);
    }

    [Fact]
    public async Task A_named_env_file_that_is_missing_is_refused_even_with_the_shell_set()
    {
        var run = await ResolveAsync(Path.Combine(_folder, ".env.prod"), ".env.prod", named: true, ambient: Staging);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(".env.prod doesn't exist or has no BSK_CONNECTION_STRING", run.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_script_names_a_database_the_way_bsk_does()
    {
        var run = await RunAsync($"Get-ConnectionTarget {Quote(Production)}", ambient: null);

        Assert.Equal(KernelConnectionString.Describe(Production), run.Target);
    }

    private string WriteEnv(string name, params string[] lines)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static Task<PwshRun> ResolveAsync(string envPath, string envFileName, bool named, string? ambient)
        => RunAsync(
            $"Get-ConnectionTarget (Resolve-MigrationConnection -EnvPath {Quote(envPath)} " +
            $"-EnvFileName {Quote(envFileName)}{(named ? " -EnvFileNamed" : "")})",
            ambient);

    private static async Task<PwshRun> RunAsync(string command, string? ambient)
    {
        var module = Path.Combine(RepoRoot.Path, "scripts", "LifeOsConnection.psm1");
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-Command",
                     $"$ErrorActionPreference = 'Stop'; Import-Module {Quote(module)} -Force; {command}",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        if (ambient is null)
        {
            start.Environment.Remove(KernelConnectionString.EnvironmentVariable);
        }
        else
        {
            start.Environment[KernelConnectionString.EnvironmentVariable] = ambient;
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return new PwshRun(process.ExitCode, await stdout, await stderr);
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private sealed record PwshRun(int ExitCode, string Stdout, string Stderr)
    {
        /// <summary>The last line written to stdout: the target the command printed.</summary>
        public string Target => Stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? "";

        public string All => Stdout + Stderr;
    }
}

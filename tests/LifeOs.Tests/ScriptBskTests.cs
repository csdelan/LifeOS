using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR118 (fifth review R11): the scripts that write to a real database, or check a copy
/// of one, run the bsk <c>Get-MainBsk</c> builds from <c>origin/main</c> (<c>scripts/LifeOsBsk.psm1</c>),
/// never a working tree's, which may be behind staging or hold a branch's migrations; and the
/// scheduled backup runs a copy of its scripts installed outside any checkout. Read from the files,
/// since no test can run them: they fetch, build and reach staging.
/// </summary>
public sealed partial class ScriptBskTests
{
    private static readonly string Scripts = Path.Combine(RepoRoot.Path, "scripts");

    /// <summary>Scripts that build the working tree's bsk, and why that's safe.</summary>
    private static readonly Dictionary<string, string> WorkingTreeBuilds = new()
    {
        ["live-check.ps1"] = "it migrates the throwaway database it makes, lifeos_check_<x>, since it checks the working tree",
        ["seed.ps1"] = "it refuses every database but a throwaway one on localhost",
    };

    private static IEnumerable<string> Files() => Directory
        .EnumerateFiles(Scripts, "*.ps*1", SearchOption.AllDirectories)
        .Where(f => f.EndsWith(".ps1", StringComparison.Ordinal) || f.EndsWith(".psm1", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal);

    private static string Read(string name) => File.ReadAllText(Path.Combine(Scripts, name)).Replace("\r\n", "\n");

    [Fact]
    public void Only_Get_MainBsk_builds_bsk_for_a_real_database()
    {
        var builders = Files()
            .Where(f => File.ReadLines(f).Any(BuildsTheCli))
            .Select(Path.GetFileName)
            .ToHashSet();

        Assert.Equal(
            ["LifeOsBsk.psm1", .. WorkingTreeBuilds.Keys.Order(StringComparer.Ordinal)],
            builders.Order(StringComparer.Ordinal));
        Assert.Contains("merge-base --is-ancestor $commit origin/main", Read("LifeOsBsk.psm1"), StringComparison.Ordinal);
        Assert.Contains("worktree add --quiet --detach $checkout $commit", Read("LifeOsBsk.psm1"), StringComparison.Ordinal);

        // Each script that reaches a real database, or checks a copy of one, takes it from there.
        foreach (var script in new[] { "install-bsk.ps1", "migrate-staging.ps1" })
        {
            Assert.Contains("Get-MainBsk", Read(script), StringComparison.Ordinal);
        }

        foreach (var script in new[] { "backup.ps1", "restore.ps1" })
        {
            Assert.Contains("Initialize-Bsk -Repo", Read(script), StringComparison.Ordinal);
        }

        Assert.Contains("Get-MainBsk -Repo $Repo", Read("LifeOsBackup.psm1"), StringComparison.Ordinal);
        Assert.Contains("dotnet $bsk.Path @argList", Read("migrate-staging.ps1"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_scheduled_backup_runs_an_installed_copy_of_every_file_it_loads()
    {
        var setup = Read("backup-setup.ps1");
        var copied = Regex.Match(setup, @"foreach \(\$file in ((?:'[^']+'(?:, )?)+)\)").Groups[1].Value
            .Split(", ").Select(f => f.Trim('\'')).ToHashSet();

        // backup.ps1 and every module it loads, and they load, by $PSScriptRoot.
        var needed = new HashSet<string> { "backup.ps1" };
        for (var queue = new Queue<string>(needed); queue.Count > 0;)
        {
            foreach (Match load in Regex.Matches(Read(queue.Dequeue()), @"Join-Path \$PSScriptRoot '([^']+\.psm1)'"))
            {
                if (needed.Add(load.Groups[1].Value))
                {
                    queue.Enqueue(load.Groups[1].Value);
                }
            }
        }

        Assert.Equal(needed.Order(StringComparer.Ordinal), copied.Order(StringComparer.Ordinal));
        Assert.Contains("$installed = Join-Path $HOME '.lifeos' 'bin' 'backup'", setup, StringComparison.Ordinal);
        Assert.Contains("$script = Join-Path $installed 'backup.ps1'", setup, StringComparison.Ordinal);
        Assert.Contains("-Repo `\"$mainCheckout`\"", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("Join-Path $PSScriptRoot 'backup.ps1'", setup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_script_parses()
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-Command",
                     "foreach ($f in $args) { $e = $null; [void][Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); " +
                     "foreach ($x in $e) { [Console]::Out.WriteLine(\"$([IO.Path]::GetFileName($f)):$($x.Extent.StartLineNumber): $($x.Message)\") } }",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var file in Files())
        {
            start.ArgumentList.Add(file);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, process.ExitCode);
        Assert.Equal("", (await output).Trim() + (await error).Trim());
    }

    // A dotnet run, build or publish of the CLI, outside a comment and a quoted hint.
    private static bool BuildsTheCli(string line)
    {
        var code = line.TrimStart();
        if (code.StartsWith('#'))
        {
            return false;
        }

        var call = DotnetBuild().Match(code);
        return call.Success
               && code[..call.Index].Count(c => c == '"') % 2 == 0
               && (code.Contains("LifeOs.Cli", StringComparison.Ordinal) || code.Contains("cliProject", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\bdotnet\b[^\n]*?\b(run|build|publish)\b")]
    private static partial Regex DotnetBuild();
}

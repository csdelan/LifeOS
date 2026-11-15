using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LifeOs.Tests;

/// <summary>
/// OPS-1 and the notes plan's "Git on the hub": the nightly backup keeps the notes vault's Git
/// history too, which Obsidian Sync doesn't copy (docs/pilot/backup-restore.md). These run
/// <c>Backup-Vault</c> from <c>scripts/LifeOsBackup.psm1</c> in <c>pwsh</c> on a throwaway
/// repository, never the live vault, with Git's own settings shut out but for
/// <c>core.autocrlf=true</c>, which Git for Windows' install sets. Encryption is a copy here,
/// since CI has no age; <c>backup.ps1</c> passes age.
/// </summary>
public sealed class VaultBackupTests : IDisposable
{
    private static readonly DateTime Night = new(2026, 11, 15, 11, 17, 0, DateTimeKind.Utc);

    private readonly string _root = Directory.CreateTempSubdirectory("lifeos-vault-").FullName;

    public VaultBackupTests()
    {
        Directory.CreateDirectory(Destination);
        Directory.CreateDirectory(Snapshots);
        Directory.CreateDirectory(Temp);
        File.WriteAllText(GitConfig, "[core]\n\tautocrlf = true\n");
        Git(_root, "init", "--quiet", "--initial-branch=master", Vault);
        Git(Vault, "config", "core.autocrlf", "false");
        Commit("First note", ("Home.md", "# Home\n"));
    }

    private string Vault => Path.Combine(_root, "Chris2.0");

    private string Destination => Path.Combine(_root, "backup", "vault");

    private string Snapshots => Path.Combine(_root, "_snapshots");

    // Where the scripts' temporary folders go (TMP, TEMP and TMPDIR), so a test can see they're gone.
    private string Temp => Path.Combine(_root, "tmp");

    private string GitConfig => Path.Combine(_root, "gitconfig");

    private string SnapshotZip => Path.Combine(Snapshots, "Chris2.0 2026-11-12 1353.zip");

    public void Dispose()
    {
        // Git writes its objects read-only, which Directory.Delete refuses on Windows.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_bundle_is_checked_by_a_clone_then_saved_and_the_vault_is_left_as_it_was()
    {
        Commit("A note with Windows line endings", (Path.Combine("Journal", "2026-11-14.md"), "line one\r\nline two\r\n"));
        Git(Vault, "-c", "user.name=LifeOS tests", "-c", "user.email=tests@example.invalid", "tag", "-a", "reorganized", "-m", "After the reorganization");
        Git(Vault, "branch", "side");
        var gitFolder = GitFolderState();
        var refs = VaultRefs();

        var run = await BackupAsync(Night);

        Assert.True(run.ExitCode == 0, run.All);
        Assert.Contains("2 commits, HEAD", run.Stdout, StringComparison.Ordinal);
        Assert.Contains("checked by a clone that checked out its 2 files clean", run.Stdout, StringComparison.Ordinal);
        var daily = Path.Combine(Destination, "daily", "20261115T111700Z");
        var bundle = Path.Combine(daily, "vault-20261115T111700Z.bundle.age");
        using var manifest = ReadManifest(daily, "20261115T111700Z");
        var root = manifest.RootElement;
        Assert.Equal(refs, root.GetProperty("refs").EnumerateArray().Select(r => r.GetString()!));
        Assert.Equal("master", root.GetProperty("branch").GetString());
        Assert.Equal("false", root.GetProperty("coreConfig").GetProperty("core.autocrlf").GetString());
        Assert.Equal(2, root.GetProperty("commits").GetInt32());
        Assert.True(root.GetProperty("restoreTest").GetProperty("checkoutClean").GetBoolean());
        Assert.Equal(Sha256(bundle), root.GetProperty("files")[0].GetProperty("sha256").GetString());
        Assert.True(Directory.Exists(Path.Combine(Destination, "monthly", "2026-11")));

        // Bundling only reads the vault, and no plaintext bundle or clone is left behind.
        Assert.Equal(gitFolder, GitFolderState());
        Assert.Equal(refs, VaultRefs());
        Assert.Empty(Directory.EnumerateFileSystemEntries(Temp));

        // Decrypted (here, already plain), it clones back to the vault as it was, line endings and all.
        var restored = Path.Combine(_root, "restored");
        Git(_root, "clone", "--quiet", "--config", "core.autocrlf=false", "--branch", "master", bundle, restored);
        Assert.Equal(Git(Vault, "rev-parse", "HEAD"), Git(restored, "rev-parse", "HEAD"));
        Assert.Equal("line one\r\nline two\r\n", await File.ReadAllTextAsync(Path.Combine(restored, "Journal", "2026-11-14.md"), TestContext.Current.CancellationToken));
        Assert.Equal(Git(Vault, "rev-parse", "reorganized"), Git(restored, "rev-parse", "reorganized"));
    }

    [Fact]
    public async Task A_night_with_no_new_commit_is_skipped_and_the_next_commit_is_bundled()
    {
        Assert.Equal(0, (await BackupAsync(Night)).ExitCode);

        var quiet = await BackupAsync(Night.AddHours(1));

        Assert.True(quiet.ExitCode == 0, quiet.All);
        Assert.Contains("hasn't changed since the bundle 20261115T111700Z", quiet.Stdout, StringComparison.Ordinal);
        Assert.Equal(["20261115T111700Z"], Names("daily"));

        Commit("Nightly sweep 2026-11-16: one note filed", ("Second.md", "two\n"));
        var next = await BackupAsync(Night.AddDays(1));

        Assert.True(next.ExitCode == 0, next.All);
        Assert.Equal(["20261115T111700Z", "20261116T111700Z"], Names("daily"));
        // The month's first bundle stays its monthly copy.
        Assert.Equal(["2026-11"], Names("monthly"));
        Assert.True(File.Exists(Path.Combine(Destination, "monthly", "2026-11", "vault-manifest-20261115T111700Z.json")));
    }

    [Fact]
    public async Task Daily_bundles_past_the_keeping_days_go_but_the_newest_stays_while_nothing_changes()
    {
        Assert.Equal(0, (await BackupAsync(Night.AddDays(-40))).ExitCode);

        // Unchanged for 40 days: the old bundle is the only copy of the vault as it is now.
        var quiet = await BackupAsync(Night);
        Assert.True(quiet.ExitCode == 0, quiet.All);
        Assert.Equal(["20261006T111700Z"], Names("daily"));

        Commit("Nightly sweep 2026-11-15: one note filed", ("Second.md", "two\n"));
        var next = await BackupAsync(Night.AddMinutes(5));

        Assert.True(next.ExitCode == 0, next.All);
        Assert.Contains("Deleted the daily vault bundle 20261006T111700Z.", next.Stdout, StringComparison.Ordinal);
        Assert.Equal(["20261115T112200Z"], Names("daily"));
        Assert.Equal(["2026-10", "2026-11"], Names("monthly"));
    }

    [Fact]
    public async Task Each_snapshot_is_copied_once_and_again_only_when_it_changes()
    {
        await File.WriteAllBytesAsync(SnapshotZip, [1, 2, 3, 4], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Snapshots, "Chris2.0 2026-11-12 1353.inventory.json"), "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Snapshots, "Test 2026-11-12 0900.zip"), "another vault's", TestContext.Current.CancellationToken);
        var copies = Path.Combine(Destination, "snapshots");
        var copy = Path.Combine(copies, "Chris2.0 2026-11-12 1353.zip.age");

        var first = await BackupAsync(Night, snapshots: true);

        Assert.True(first.ExitCode == 0, first.All);
        Assert.Equal(
            ["Chris2.0 2026-11-12 1353.inventory.json.age", "Chris2.0 2026-11-12 1353.zip.age", "snapshots.json"],
            Directory.GetFiles(copies).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal));
        var copiedAt = File.GetLastWriteTimeUtc(copy);

        var second = await BackupAsync(Night.AddDays(1), snapshots: true);

        Assert.True(second.ExitCode == 0, second.All);
        Assert.Contains("The vault's 2 snapshot file(s) were already backed up.", second.Stdout, StringComparison.Ordinal);
        Assert.Equal(copiedAt, File.GetLastWriteTimeUtc(copy));

        await File.WriteAllBytesAsync(SnapshotZip, [5, 6, 7, 8, 9], TestContext.Current.CancellationToken);
        var third = await BackupAsync(Night.AddDays(2), snapshots: true);

        Assert.True(third.ExitCode == 0, third.All);
        Assert.Contains("changed since it was copied on 2026-11-15; copying it again", third.Stdout, StringComparison.Ordinal);
        Assert.Equal([5, 6, 7, 8, 9], await File.ReadAllBytesAsync(copy, TestContext.Current.CancellationToken));
        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(copies, "snapshots.json"), TestContext.Current.CancellationToken));
        var zip = index.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == Path.GetFileName(SnapshotZip));
        Assert.Equal(Sha256(SnapshotZip), zip.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task A_folder_that_is_not_the_top_of_a_repository_is_refused_and_the_snapshots_are_still_copied()
    {
        var plain = Path.Combine(_root, "plain", "Chris2.0");
        Directory.CreateDirectory(plain);
        await File.WriteAllBytesAsync(SnapshotZip, [1, 2, 3], TestContext.Current.CancellationToken);

        var notARepository = await BackupAsync(Night, vault: plain, snapshots: true);

        Assert.Equal(1, notARepository.ExitCode);
        Assert.Contains($"The vault folder {plain} isn't a Git repository.", notARepository.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Destination, "daily")));
        Assert.True(File.Exists(Path.Combine(Destination, "snapshots", "Chris2.0 2026-11-12 1353.zip.age")));

        var journal = Path.Combine(Vault, "Journal");
        Directory.CreateDirectory(journal);
        var inside = await BackupAsync(Night, vault: journal);

        Assert.Equal(1, inside.ExitCode);
        Assert.Contains("is inside the Git repository", inside.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Destination, "daily")));
    }

    [Fact]
    public async Task A_bundle_that_could_not_be_encrypted_is_not_kept_and_leaves_no_plaintext()
    {
        var run = await BackupAsync(Night, encrypt: "{ param($In, $Out) throw 'age stopped' }");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The vault's Git history wasn't backed up: age stopped.", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(["20261115T111700Z.partial"], Names("daily"));
        Assert.False(Directory.Exists(Path.Combine(Destination, "monthly")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Temp));

        // The next night doesn't take the leftover for a kept bundle.
        var next = await BackupAsync(Night.AddDays(1));
        Assert.True(next.ExitCode == 0, next.All);
        Assert.Contains("20261116T111700Z", Names("daily"));
    }

    [Fact]
    public async Task With_no_vault_set_up_the_night_says_so_and_carries_on()
    {
        var run = await PwshAsync(
            $"Invoke-VaultBackup -Settings @{{ Target = 'staging'; Destination = {Quote(Destination)} }} -Encrypt {{ throw 'not called' }}");

        Assert.True(run.ExitCode == 0, run.All);
        Assert.Contains("No vault is set up, so the notes vault's Git history isn't backed up", run.Stdout, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Destination));
    }

    private Task<PwshRun> BackupAsync(DateTime now, string? vault = null, bool snapshots = false, string? encrypt = null)
        => PwshAsync(
            $"Backup-Vault -VaultPath {Quote(vault ?? Vault)} -Destination {Quote(Destination)}" +
            (snapshots ? $" -SnapshotFolder {Quote(Snapshots)}" : "") +
            $" -Encrypt {encrypt ?? "{ param($In, $Out) Copy-Item -LiteralPath $In -Destination $Out }"}" +
            $" -Now ([datetime]::new({now.Ticks}, [DateTimeKind]::Utc))");

    private async Task<PwshRun> PwshAsync(string command)
    {
        var module = Path.Combine(RepoRoot.Path, "scripts", "LifeOsBackup.psm1");
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-Command",
                     "$ErrorActionPreference = 'Stop'; $PSStyle.OutputRendering = 'PlainText'; " +
                     $"try {{ Import-Module {Quote(module)} -Force; {command} }} " +
                     "catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        Isolate(start.Environment);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        return new PwshRun(process.ExitCode, await stdout, await stderr);
    }

    private string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Isolate(start.Environment);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {stderr.Result}");
        return stdout.Result.Trim();
    }

    // Chris's Git settings stay out; Git for Windows' autocrlf=true stays in, from GitConfig.
    private void Isolate(IDictionary<string, string?> environment)
    {
        environment["GIT_CONFIG_GLOBAL"] = GitConfig;
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var name in new[] { "TMP", "TEMP", "TMPDIR" })
        {
            environment[name] = Temp;
        }
    }

    private void Commit(string message, params (string Path, string Text)[] files)
    {
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(Vault, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
            Git(Vault, "add", "--", path);
        }

        Git(Vault, "-c", "user.name=LifeOS tests", "-c", "user.email=tests@example.invalid", "commit", "--quiet", "-m", message);
    }

    private string[] VaultRefs()
        => [.. $"{Git(Vault, "for-each-ref", "--format=%(objectname) %(refname)")}\n{Git(Vault, "rev-parse", "HEAD")} HEAD"
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)];

    // Every file under the vault's .git, with its size and time, to show bundling wrote nothing there.
    private string[] GitFolderState()
    {
        var git = Path.Combine(Vault, ".git");
        return
        [
            .. Directory.EnumerateFiles(git, "*", SearchOption.AllDirectories)
                .Select(f => $"{Path.GetRelativePath(git, f)} {new FileInfo(f).Length} {File.GetLastWriteTimeUtc(f).Ticks}")
                .Order(StringComparer.Ordinal),
        ];
    }

    private string[] Names(string kind)
        => [.. Directory.GetDirectories(Path.Combine(Destination, kind)).Select(d => Path.GetFileName(d)).Order(StringComparer.Ordinal)];

    private static JsonDocument ReadManifest(string folder, string stamp)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, $"vault-manifest-{stamp}.json")));

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private sealed record PwshRun(int ExitCode, string Stdout, string Stderr)
    {
        public string All => Stdout + Stderr;
    }
}

using System.Diagnostics;
using System.Text.Json;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Cli;

/// <summary>A bsk command failed; carries the CLI's own error message.</summary>
public sealed class BskException(string message) : Exception(message);

/// <summary>
/// The pilot's single write path: shells out to the <c>bsk</c> CLI (invariant 9 —
/// bsk is the only writer). Returns stdout on success, and throws
/// <see cref="BskException"/> with the CLI's (plain-text) message on a non-zero exit.
/// </summary>
public sealed class BskCli(string executablePath, bool fromWorkingTree = false)
{
    /// <summary>
    /// Where <c>scripts/install-bsk.ps1</c> publishes <c>bsk</c>, built from a clean checkout
    /// of <c>origin/main</c>.
    /// </summary>
    public static string InstalledPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lifeos", "bin", "bsk", "bsk.exe");

    /// <summary>
    /// True when this is the repo's own build output. Agents build unpushed code in the same
    /// tree, so that copy is never used for STAGING and never migrates DEV on launch.
    /// </summary>
    public bool FromWorkingTree { get; } = fromWorkingTree;

    /// <summary>
    /// Locates <c>bsk.exe</c>: the <c>BSK_EXE</c> environment variable if set, else the copy
    /// <c>scripts/install-bsk.ps1</c> installed, else (DEV only) the build output under the repo
    /// that contains this pilot. Throws if none can be used for <paramref name="environment"/>.
    /// </summary>
    internal static BskCli Locate(PilotEnvironment environment)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BSK_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return new BskCli(fromEnvironment);
        }

        if (File.Exists(InstalledPath))
        {
            return new BskCli(InstalledPath);
        }

        if (environment == PilotEnvironment.Staging)
        {
            throw new BskException(
                "Writes to STAGING use only the bsk built from main. Run ./scripts/install-bsk.ps1 "
                + $"(it installs {InstalledPath}), or set BSK_EXE, then restart the pilot.");
        }

        var root = FindRepoRoot(AppContext.BaseDirectory);
        if (root is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(root, "src", "LifeOs.Cli", "bin", configuration, "net10.0", "bsk.exe");
                if (File.Exists(candidate))
                {
                    return new BskCli(candidate, fromWorkingTree: true);
                }
            }
        }

        throw new BskException(
            "Could not find bsk.exe. Run ./scripts/install-bsk.ps1, build the solution (./run.ps1), "
            + "or set the BSK_EXE environment variable to its full path.");
    }

    private static string? FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LifeOs.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs <c>bsk &lt;args&gt;</c>; returns stdout, or throws on a non-zero exit.</summary>
    public string Run(params string[] args) => Run(extraEnv: null, args);

    /// <summary>Like <see cref="Run(string[])"/>, with extra environment variables for the child process.</summary>
    public string Run(IReadOnlyDictionary<string, string>? extraEnv, params string[] args)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new BskException($"Could not start {executablePath}.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new BskException(
                string.IsNullOrWhiteSpace(message) ? $"bsk exited with code {process.ExitCode}." : message.Trim());
        }

        return stdout;
    }

    /// <summary>Runs <c>bsk … --json</c> and deserializes stdout.</summary>
    public T RunJson<T>(params string[] args)
    {
        var withJson = new string[args.Length + 1];
        args.CopyTo(withJson, 0);
        withJson[^1] = "--json";
        var stdout = Run(withJson);
        try
        {
            return JsonSerializer.Deserialize<T>(stdout, JsonOptions)
                ?? throw new BskException("bsk --json returned empty output.");
        }
        catch (JsonException ex)
        {
            throw new BskException($"Could not parse bsk JSON output: {ex.Message}\n{stdout}");
        }
    }

    public CreatedSubject NewSubject(params string[] args)
        => RunJson<CreatedSubject>(args);

    /// <summary>
    /// Adds a journal entry to <paramref name="subjectUrn"/> in one <c>bsk journal &lt;item&gt;</c>
    /// call, which writes the entry and its link to the item in one transaction (code review
    /// CR63). Before, an editor shim captured the entry and a second <c>bsk relate</c> linked it,
    /// so a failure between them left an entry about nothing. The text goes through a temp file,
    /// not the command line, so a long entry fits.
    /// </summary>
    public void AppendJournal(string subjectUrn, string text)
    {
        var draft = Path.Combine(Path.GetTempPath(), $"lifeos-journal-draft-{Guid.NewGuid():N}.txt");
        File.WriteAllText(draft, text);
        try
        {
            var created = RunJson<JournalCapture>("journal", subjectUrn, "--file", draft);
            if (created.Id == Guid.Empty)
            {
                throw new BskException("bsk journal did not return an event id.");
            }
        }
        finally
        {
            TryDelete(draft);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Temp leftovers are harmless.
        }
    }
}

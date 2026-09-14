using System.Diagnostics;
using System.Text.Json;

namespace LifeOs.Pilot.Cli;

/// <summary>A bsk command failed; carries the CLI's own error message.</summary>
public sealed class BskException(string message) : Exception(message);

/// <summary>
/// The pilot's single write path: shells out to the <c>bsk</c> CLI (invariant 9 —
/// bsk is the only writer). Returns stdout on success, and throws
/// <see cref="BskException"/> with the CLI's (plain-text) message on a non-zero exit.
/// </summary>
public sealed class BskCli(string executablePath)
{
    /// <summary>
    /// Locates <c>bsk.exe</c>: the <c>BSK_EXE</c> environment variable if set, else the
    /// build output under the repo that contains this pilot. Throws if it cannot be found.
    /// </summary>
    public static BskCli Locate()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BSK_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return new BskCli(fromEnvironment);
        }

        var root = FindRepoRoot(AppContext.BaseDirectory);
        if (root is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(root, "src", "LifeOs.Cli", "bin", configuration, "net10.0", "bsk.exe");
                if (File.Exists(candidate))
                {
                    return new BskCli(candidate);
                }
            }
        }

        throw new BskException(
            "Could not find bsk.exe. Build the solution (./run.ps1) or set the BSK_EXE environment variable to its full path.");
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
    /// Append a journal entry and relate it to <paramref name="subjectUrn"/> without
    /// opening $EDITOR: a tiny helper copies the drafted text into the file the
    /// existing <c>bsk journal</c> verb already expects, then <c>bsk relate</c> files it.
    /// </summary>
    public void AppendJournal(string subjectUrn, string text)
    {
        var draft = Path.Combine(Path.GetTempPath(), $"lifeos-journal-draft-{Guid.NewGuid():N}.txt");
        var helper = Path.Combine(Path.GetTempPath(), $"lifeos-journal-write-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(draft, text);
        File.WriteAllText(helper, $"@echo off{Environment.NewLine}copy /Y \"{draft}\" %1 >nul{Environment.NewLine}exit 0{Environment.NewLine}");
        try
        {
            var created = Run(
                new Dictionary<string, string> { ["VISUAL"] = helper, ["EDITOR"] = helper },
                "journal", "--json");
            var parsed = JsonSerializer.Deserialize<JournalCapture>(created, JsonOptions)
                ?? throw new BskException("bsk journal --json returned empty output.");
            if (parsed.Id == Guid.Empty)
            {
                throw new BskException("Journal capture did not return an event id.");
            }

            Run("relate", parsed.Id.ToString(), subjectUrn);
        }
        finally
        {
            TryDelete(draft);
            TryDelete(helper);
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

    private sealed class JournalCapture
    {
        public Guid Id { get; set; }
    }
}

/// <summary>The structured result of <c>bsk new --json</c>.</summary>
public sealed class CreatedSubject
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
}

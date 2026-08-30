using System.Diagnostics;

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

    /// <summary>Runs <c>bsk &lt;args&gt;</c>; returns stdout, or throws on a non-zero exit.</summary>
    public string Run(params string[] args)
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
}

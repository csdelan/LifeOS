using System.Diagnostics;
using System.Text;

namespace LifeOs.Pilot.Cli;

/// <summary>What a child process printed, and how it exited.</summary>
internal sealed record ChildProcessResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Runs a child process to its end and gives back what it printed (WinForms-free, so the tests
/// compile it). Both pipes are drained together: <c>BskCli</c> read all of stdout before
/// any of stderr, so a <c>bsk</c> that filled the stderr pipe first (a long refusal, a stack trace)
/// waited on the Pilot while the Pilot waited on it, and the window hung for good (code review
/// CR151).
/// </summary>
internal static class ChildProcess
{
    /// <summary>Runs <paramref name="path"/> with <paramref name="args"/>; null when it couldn't start.</summary>
    public static ChildProcessResult? Run(
        string path, IEnumerable<string> args, IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var startInfo = new ProcessStartInfo(path)
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

        using var process = new Process { StartInfo = startInfo };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderr)
                {
                    stderr.AppendLine(e.Data);
                }
            }
        };

        if (!process.Start())
        {
            return null;
        }

        process.BeginErrorReadLine();
        var stdout = process.StandardOutput.ReadToEnd();

        // With no timeout this also waits for the stderr reader to reach the end of its stream.
        process.WaitForExit();
        lock (stderr)
        {
            return new ChildProcessResult(process.ExitCode, stdout, stderr.ToString());
        }
    }
}

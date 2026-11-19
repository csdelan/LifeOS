using LifeOs.Pilot.Cli;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR151: the Pilot's <c>bsk</c> runner read all of stdout before any of stderr, so a
/// child that filled the stderr pipe first waited on the Pilot while the Pilot waited on it, and
/// the window hung for good. The child here writes far more to stderr than a pipe holds before
/// it prints its one line of stdout; a deadlock fails the test at its timeout rather than hanging.
/// </summary>
public sealed class PilotChildProcessTests
{
    private const int StderrLength = 200_000;

    [Fact]
    public async Task A_child_that_fills_stderr_before_stdout_runs_to_its_end()
    {
        var (path, args) = OperatingSystem.IsWindows()
            ? ("powershell.exe", new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                $"[Console]::Error.Write('x' * {StderrLength}); [Console]::Out.Write('done'); exit 3"
            })
            : ("/bin/sh", new[] { "-c", $"head -c {StderrLength} /dev/zero | tr '\\0' x >&2; printf done; exit 3" });

        var run = Task.Run(() => ChildProcess.Run(path, args), TestContext.Current.CancellationToken);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("done", result.Stdout.Trim());
        Assert.Equal(StderrLength, result.Stderr.Trim().Length);
    }
}

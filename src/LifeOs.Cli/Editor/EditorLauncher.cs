using System.Diagnostics;

namespace LifeOs.Cli.Editor;

/// <summary>
/// Launches the user's editor on a file and waits for it to close. The editor is
/// taken from <c>$VISUAL</c>, then <c>$EDITOR</c>, falling back to a platform
/// default. A command may include arguments (e.g. <c>code --wait</c>); the file
/// path is appended last.
/// </summary>
internal static class EditorLauncher
{
    public static async Task LaunchAsync(string filePath, CancellationToken cancellationToken)
    {
        var command = ResolveEditorCommand();
        var tokens = SplitCommand(command);

        var startInfo = new ProcessStartInfo
        {
            FileName = tokens[0],
            UseShellExecute = false
        };
        for (var i = 1; i < tokens.Count; i++)
        {
            startInfo.ArgumentList.Add(tokens[i]);
        }
        startInfo.ArgumentList.Add(filePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start editor '{command}'.");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Editor '{command}' exited with code {process.ExitCode}; nothing was captured.");
        }
    }

    private static string ResolveEditorCommand()
    {
        var editor = Environment.GetEnvironmentVariable("VISUAL");
        if (string.IsNullOrWhiteSpace(editor))
        {
            editor = Environment.GetEnvironmentVariable("EDITOR");
        }
        if (string.IsNullOrWhiteSpace(editor))
        {
            editor = OperatingSystem.IsWindows() ? "notepad" : "vi";
        }
        return editor;
    }

    // Minimal command splitter: whitespace-separated, honouring double quotes so
    // an editor path containing spaces can be quoted.
    private static List<string> SplitCommand(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in command)
        {
            switch (ch)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ' ' or '\t' when !inQuotes:
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}

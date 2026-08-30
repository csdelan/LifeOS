namespace LifeOs.Cli.Editor;

/// <summary>
/// Runs a text buffer through an external editor: writes an optional seed to a
/// temp file, hands it to the supplied launcher, reads the buffer back, and
/// cleans up. The launcher is injected so the round-trip is testable without a
/// real editor process.
/// </summary>
internal static class EditorBuffer
{
    public static async Task<string> EditAsync(
        Func<string, CancellationToken, Task> launchEditor,
        string seed = "",
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bsk-journal-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(path, seed, cancellationToken);
            await launchEditor(path, cancellationToken);
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless; don't mask the real outcome.
            }
        }
    }
}

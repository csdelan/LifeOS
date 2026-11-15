namespace LifeOs.Tests.Scale;

/// <summary>
/// With <c>LIFEOS_SCALE_REPORT</c> set to a file, the scale tests add a line to it for each thing
/// they measure (what each hot path cost, each stream's rows, how long the template took), so a
/// change's cost before and after can be compared: run them once each side and diff the files.
/// </summary>
internal static class ScaleReport
{
    private static readonly Lock Gate = new();

    public static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("LIFEOS_SCALE_REPORT") is not { Length: > 0 } path)
        {
            return;
        }

        lock (Gate)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}

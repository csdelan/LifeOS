namespace LifeOs.Tests;

/// <summary>The repository's root: the folder holding <c>LifeOs.slnx</c>, above the test binaries.</summary>
internal static class RepoRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "LifeOs.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No LifeOs.slnx above {AppContext.BaseDirectory}.");
    }
}

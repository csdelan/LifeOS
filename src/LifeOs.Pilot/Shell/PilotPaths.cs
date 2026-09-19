namespace LifeOs.Pilot.Shell;

/// <summary>Where the pilot keeps its per-user state, and how it finds the repo .env.</summary>
internal static class PilotPaths
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueSkies", "Pilot");

    public static string Placement => Path.Combine(DataDir, "window-placement.json");

    public static string ViewState => Path.Combine(DataDir, "view-state.json");

    /// <summary>Remembered environment choice + .env location (see EnvironmentSettings).</summary>
    public static string Settings => Path.Combine(DataDir, "environment.json");

    /// <summary>
    /// The repo's own .env, used as the default staging file on first run: walk up
    /// from the running binary for LifeOs.slnx, then take &lt;root&gt;/.env. Returns
    /// null when the pilot runs detached from its repo (the path is a default only;
    /// whether the file exists is checked when Staging is activated).
    /// </summary>
    public static string? DefaultEnvFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LifeOs.slnx")))
            {
                return Path.Combine(directory.FullName, ".env");
            }

            directory = directory.Parent;
        }

        return null;
    }
}

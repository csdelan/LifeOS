namespace LifeOs.Pilot.Shell;

/// <summary>Which database the pilot is pointed at.</summary>
internal enum PilotEnvironment
{
    /// <summary>Local Docker Postgres (docker-compose.yml). The safe default.</summary>
    Dev,

    /// <summary>The shared Supabase cloud database, credentials from a .env file.</summary>
    Staging,
}

/// <summary>Display text and highlight colours for each environment.</summary>
internal static class PilotEnvironmentInfo
{
    /// <summary>Caption / badge text on the title bar and the top-right selector.</summary>
    public static readonly Color Foreground = Color.White;

    public static string Label(PilotEnvironment environment) => environment switch
    {
        PilotEnvironment.Staging => "STAGING",
        _ => "DEV",
    };

    /// <summary>
    /// Title-bar and badge background. DEV is a calm green (local, safe); STAGING
    /// is a caution amber (a shared cloud database — take more care).
    /// </summary>
    public static Color Background(PilotEnvironment environment) => environment switch
    {
        PilotEnvironment.Staging => Color.FromArgb(0xB5, 0x4A, 0x00),
        _ => Color.FromArgb(0x1E, 0x6F, 0x3C),
    };
}

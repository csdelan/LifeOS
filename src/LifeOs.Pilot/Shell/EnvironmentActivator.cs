namespace LifeOs.Pilot.Shell;

/// <summary>
/// Points the current process at the chosen environment by setting (or clearing)
/// the connection-string environment variables the reader and the bsk CLI read.
/// Because bsk runs as a child process, it inherits these — so setting them here
/// switches the read path and the write path together, with the credentials
/// living only in the .env file (never duplicated into Windows env vars).
/// </summary>
internal static class EnvironmentActivator
{
    public const string OwnerVar = "BSK_CONNECTION_STRING";
    public const string ReaderVar = "BSK_READER_CONNECTION_STRING";

    /// <summary>The outcome of pointing the process at an environment.</summary>
    /// <param name="Effective">What we actually ran as (falls back to Dev on a problem).</param>
    /// <param name="WritesEnabled">False when Staging has no owner credentials in the .env.</param>
    /// <param name="Warning">A message to show the user, or null.</param>
    public readonly record struct Result(PilotEnvironment Effective, bool WritesEnabled, string? Warning);

    public static Result Apply(PilotEnvironment requested, string? envFilePath)
    {
        if (requested == PilotEnvironment.Dev)
        {
            UseLocalDefaults();
            return new Result(PilotEnvironment.Dev, WritesEnabled: true, Warning: null);
        }

        if (string.IsNullOrWhiteSpace(envFilePath) || !File.Exists(envFilePath))
        {
            UseLocalDefaults();
            return new Result(PilotEnvironment.Dev, true,
                $"STAGING is selected, but no .env file was found at:\n{envFilePath}\n\n"
                + "Running DEV instead. Choose a .env file with the “…” button.");
        }

        IReadOnlyDictionary<string, string> env;
        try
        {
            env = DotEnv.Read(envFilePath);
        }
        catch (IOException ex)
        {
            UseLocalDefaults();
            return new Result(PilotEnvironment.Dev, true,
                $"STAGING is selected, but the .env file could not be read:\n{ex.Message}\n\nRunning DEV instead.");
        }

        if (!env.TryGetValue(ReaderVar, out var reader) || string.IsNullOrWhiteSpace(reader))
        {
            UseLocalDefaults();
            return new Result(PilotEnvironment.Dev, true,
                $"STAGING is selected, but {ReaderVar} is missing from:\n{envFilePath}\n\nRunning DEV instead.");
        }

        Environment.SetEnvironmentVariable(ReaderVar, reader);

        if (env.TryGetValue(OwnerVar, out var owner) && !string.IsNullOrWhiteSpace(owner))
        {
            Environment.SetEnvironmentVariable(OwnerVar, owner);
            return new Result(PilotEnvironment.Staging, WritesEnabled: true, Warning: null);
        }

        // Reads work, but there are no write credentials — run read-only rather
        // than let writes silently fall back to the local database.
        Environment.SetEnvironmentVariable(OwnerVar, null);
        return new Result(PilotEnvironment.Staging, WritesEnabled: false,
            $"Connected to STAGING for reads, but {OwnerVar} is missing from the .env, "
            + "so writes are disabled this session.");
    }

    private static void UseLocalDefaults()
    {
        // Clearing the process overrides makes the reader fall back to its
        // local-development default and bsk fall back to its own — both localhost.
        Environment.SetEnvironmentVariable(OwnerVar, null);
        Environment.SetEnvironmentVariable(ReaderVar, null);
    }
}

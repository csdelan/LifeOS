using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Postgres column names are snake_case; let Dapper map them to PascalCase
        // properties (expected_cadence -> ExpectedCadence, etc.).
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        ApplicationConfiguration.Initialize();

        // Point this process (and, by inheritance, the bsk child processes) at the
        // remembered environment before anything reads a connection string.
        var settings = EnvironmentSettings.Load(PilotPaths.Settings, PilotPaths.DefaultEnvFile());
        var activation = EnvironmentActivator.Apply(settings.Environment, settings.EnvFilePath);

        // Failover: if we ended up on the local DEV database but it isn't reachable
        // (e.g. a fresh machine with no local Postgres, or Docker not running), fall
        // back to the STAGING .env when one is available, so the pilot can still open
        // against Supabase instead of stalling on a dead local DB. The choice is not
        // persisted — next launch tries DEV again in case the local DB is back up.
        if (activation.Effective == PilotEnvironment.Dev
            && !LocalDatabase.CanConnect(ReaderConnectionString.Resolve()))
        {
            var failover = EnvironmentActivator.Apply(PilotEnvironment.Staging, settings.EnvFilePath);
            if (failover.Effective == PilotEnvironment.Staging)
            {
                activation = failover with
                {
                    Warning =
                        "The local DEV database isn't reachable, so the pilot connected to STAGING "
                        + $"(Supabase) using:\n{settings.EnvFilePath}\n\n"
                        + (failover.Warning
                            ?? "Switch back to DEV from the selector once your local database is running."),
                };
            }
        }

        var reader = new SubjectReader(ReaderConnectionString.Resolve());

        // Writes shell out to bsk (which inherits our environment). Disabled when
        // Staging has no owner credentials, or when bsk.exe can't be found — the
        // write buttons then explain the read-only state.
        BskCli? bsk = null;
        if (activation.WritesEnabled)
        {
            try
            {
                bsk = BskCli.Locate();
            }
            catch (BskException)
            {
                // Left null on purpose — read-only mode until bsk is available.
            }
        }

        // Keep the LOCAL dev database in step with the build by applying pending
        // migrations before the window opens (a schema-touching migration that
        // hasn't been applied makes reads referencing new columns fail). The
        // STAGING schema is owned by CI and scripts/migrate-staging.ps1, so the
        // pilot never migrates it on launch. Best-effort: a failure warns but the
        // app still opens (it already surfaces read errors).
        if (bsk is not null && activation.Effective == PilotEnvironment.Dev)
        {
            try
            {
                bsk.Run("migrate");
            }
            catch (BskException ex)
            {
                MessageBox.Show(
                    "Database migration failed on startup. The app will still open, but reads may fail "
                    + $"until this is resolved.\n\n{ex.Message}",
                    "Migration failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        if (activation.Warning is not null)
        {
            MessageBox.Show(activation.Warning, "Environment", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Application.Run(new MainForm(reader, bsk, activation.Effective, settings));
    }
}

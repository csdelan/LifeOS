using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;

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

        var reader = new SubjectReader(ReaderConnectionString.Resolve());

        // Writes shell out to bsk. If it can't be found the reader still works;
        // the write buttons explain what to do.
        BskCli? bsk = null;
        try
        {
            bsk = BskCli.Locate();
        }
        catch (BskException)
        {
            // Left null on purpose — read-only mode until bsk is available.
        }

        // Apply any pending migrations before the window opens. The pilot reads
        // Postgres directly but never writes schema itself (bsk is the only writer),
        // so a schema-touching migration that hasn't been applied makes reads that
        // reference new columns fail. Running migrate here keeps the DB in step with
        // the build. Best-effort: a failure warns but still opens the app (which
        // already surfaces read errors), and read-only mode skips it entirely.
        if (bsk is not null)
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

        Application.Run(new BrowseForm(reader, bsk));
    }
}

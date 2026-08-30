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

        Application.Run(new BrowseForm(reader, bsk));
    }
}

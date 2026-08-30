using System.CommandLine;
using System.Text.Json;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Migrations;
using LifeOs.Infrastructure.Rebuild;

var connectionOption = new Option<string?>("--connection", "-c")
{
    Description = $"PostgreSQL connection string. Falls back to the {KernelConnectionString.EnvironmentVariable} " +
                  "environment variable, then the local development default.",
    Recursive = true
};

var jsonOption = new Option<bool>("--json")
{
    Description = "Emit machine-readable JSON instead of human-readable text.",
    Recursive = true
};

var rootCommand = new RootCommand("bsk — the Life Kernel command-line interface.");
rootCommand.Options.Add(connectionOption);
rootCommand.Options.Add(jsonOption);

var migrateCommand = new Command("migrate", "Apply pending database migrations, in order and idempotently.");
migrateCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
    var asJson = parseResult.GetValue(jsonOption);

    try
    {
        var runner = new MigrationRunner(connectionString);
        var applied = await runner.ApplyAsync(cancellationToken);

        if (asJson)
        {
            var payload = new
            {
                applied = applied.Select(m => new { version = m.Version, name = m.Name }).ToArray(),
                count = applied.Count
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
        }
        else if (applied.Count == 0)
        {
            Console.WriteLine("Database is up to date; no migrations to apply.");
        }
        else
        {
            Console.WriteLine($"Applied {applied.Count} migration(s):");
            foreach (var migration in applied)
            {
                Console.WriteLine($"  {migration.Version:D4}  {migration.Name}");
            }
        }

        return 0;
    }
    catch (Exception ex)
    {
        if (asJson)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }));
        }
        else
        {
            Console.Error.WriteLine($"Migration failed: {ex.Message}");
        }

        return 1;
    }
});

rootCommand.Subcommands.Add(migrateCommand);

var verifyOption = new Option<bool>("--verify")
{
    Description = "Rebuild into a shadow and diff against the current derived state; report drift without changing anything."
};

var rebuildCommand = new Command("rebuild",
    "Regenerate all derived tables from source, deterministically and in a transaction.");
rebuildCommand.Options.Add(verifyOption);
rebuildCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
    var asJson = parseResult.GetValue(jsonOption);
    var verifyOnly = parseResult.GetValue(verifyOption);

    try
    {
        var rebuilder = new DerivedRebuilder(connectionString);

        if (verifyOnly)
        {
            var result = await rebuilder.VerifyAsync(cancellationToken);

            if (asJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    drift = result.HasDrift,
                    materializedChecksum = result.MaterializedChecksum,
                    freshChecksum = result.FreshChecksum
                }));
            }
            else if (result.HasDrift)
            {
                Console.Error.WriteLine(
                    "Derived state has drifted from source. " +
                    $"materialized={result.MaterializedChecksum[..12]} fresh={result.FreshChecksum[..12]}. " +
                    "Run `bsk rebuild` to regenerate.");
            }
            else
            {
                Console.WriteLine($"Derived state matches source (checksum {result.FreshChecksum[..12]}).");
            }

            return result.HasDrift ? 1 : 0;
        }

        var rows = await rebuilder.RebuildAsync(cancellationToken);
        var checksum = await rebuilder.ChecksumAsync(cancellationToken);

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { rebuilt = true, rows, checksum }));
        }
        else
        {
            Console.WriteLine($"Rebuilt derived state: {rows} row(s) (checksum {checksum[..12]}).");
        }

        return 0;
    }
    catch (Exception ex)
    {
        if (asJson)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }));
        }
        else
        {
            Console.Error.WriteLine($"Rebuild failed: {ex.Message}");
        }

        return 1;
    }
});

rootCommand.Subcommands.Add(rebuildCommand);

return await rootCommand.Parse(args).InvokeAsync();

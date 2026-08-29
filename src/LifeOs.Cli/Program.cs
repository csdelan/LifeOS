using System.CommandLine;
using System.Text.Json;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Migrations;

var connectionOption = new Option<string?>("--connection", "-c")
{
    Description = $"PostgreSQL connection string. Falls back to the {KernelConnectionString.EnvironmentVariable} " +
                  "environment variable, then the local development default."
};

var jsonOption = new Option<bool>("--json")
{
    Description = "Emit machine-readable JSON instead of human-readable text."
};

var rootCommand = new RootCommand("bsk — the Life Kernel command-line interface.");

var migrateCommand = new Command("migrate", "Apply pending database migrations, in order and idempotently.");
migrateCommand.Options.Add(connectionOption);
migrateCommand.Options.Add(jsonOption);
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

return await rootCommand.Parse(args).InvokeAsync();

using System.CommandLine;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

internal static class MigrateCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var outOfOrderOption = new Option<bool>("--allow-out-of-order")
        {
            Description = "Apply a pending migration numbered below the highest one already applied. "
                + "Without it, migrate refuses, because that usually means a migration was renumbered after it ran."
        };

        var command = new Command("migrate", "Apply pending database migrations, in order and idempotently.")
        {
            outOfOrderOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var allowOutOfOrder = parseResult.GetValue(outOfOrderOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var applied = await provider.GetRequiredService<MigrationRunner>().ApplyAsync(allowOutOfOrder, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        applied = applied.Select(m => new { version = m.Version, name = m.Name }).ToArray(),
                        count = applied.Count
                    });
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
            });
        });

        return command;
    }
}

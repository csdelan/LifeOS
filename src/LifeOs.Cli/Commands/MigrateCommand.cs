using System.CommandLine;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

internal static class MigrateCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("migrate", "Apply pending database migrations, in order and idempotently.");
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var applied = await provider.GetRequiredService<MigrationRunner>().ApplyAsync(cancellationToken);

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

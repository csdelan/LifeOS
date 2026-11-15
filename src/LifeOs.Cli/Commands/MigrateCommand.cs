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

        var pendingOption = new Option<bool>("--pending")
        {
            Description = "List the migrations that would be applied, after the same checks, and apply none."
        };

        var command = new Command("migrate", "Apply pending database migrations, in order and idempotently.")
        {
            outOfOrderOption,
            pendingOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var allowOutOfOrder = parseResult.GetValue(outOfOrderOption);
            var onlyList = parseResult.GetValue(pendingOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var runner = provider.GetRequiredService<MigrationRunner>();
                if (onlyList)
                {
                    // Code review CR117: a release run by hand says what it would apply unless asked to.
                    var pending = await runner.PendingAsync(allowOutOfOrder, cancellationToken);
                    if (asJson)
                    {
                        Cli.WriteJson(new
                        {
                            pending = pending.Select(m => new { version = m.Version, name = m.Name }).ToArray(),
                            count = pending.Count
                        });
                    }
                    else if (pending.Count == 0)
                    {
                        Console.WriteLine("Database is up to date; no migrations would be applied.");
                    }
                    else
                    {
                        Console.WriteLine($"{pending.Count} migration(s) would be applied:");
                        foreach (var migration in pending)
                        {
                            Console.WriteLine($"  {migration.Version:D4}  {migration.Name}");
                        }
                    }

                    return 0;
                }

                var applied = await runner.ApplyAsync(allowOutOfOrder, cancellationToken);

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

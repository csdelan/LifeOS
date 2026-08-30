using System.CommandLine;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Rebuild;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

internal static class RebuildCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var verifyOption = new Option<bool>("--verify")
        {
            Description = "Rebuild into a shadow and diff against the current derived state; " +
                          "report drift without changing anything."
        };

        var command = new Command("rebuild",
            "Regenerate all derived tables from source, deterministically and in a transaction.");
        command.Options.Add(verifyOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var verifyOnly = parseResult.GetValue(verifyOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var rebuilder = provider.GetRequiredService<DerivedRebuilder>();

                if (verifyOnly)
                {
                    var result = await rebuilder.VerifyAsync(cancellationToken);

                    if (asJson)
                    {
                        Cli.WriteJson(new
                        {
                            drift = result.HasDrift,
                            materializedChecksum = result.MaterializedChecksum,
                            freshChecksum = result.FreshChecksum
                        });
                    }
                    else if (result.HasDrift)
                    {
                        Cli.WriteError(false,
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
                    Cli.WriteJson(new { rebuilt = true, rows, checksum });
                }
                else
                {
                    Console.WriteLine($"Rebuilt derived state: {rows} row(s) (checksum {checksum[..12]}).");
                }

                return 0;
            });
        });

        return command;
    }
}

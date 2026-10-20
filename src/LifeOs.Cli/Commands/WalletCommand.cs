using System.CommandLine;
using LifeOs.Application.Rewards;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk wallet</c> — show every currency's balance (REWARD-1 / REWARD-3), and
/// <c>bsk wallet open</c> — write the wallet's start line. Nothing that happened before
/// the opening ever pays; opening again is a no-op that reports the existing opening.
/// </summary>
internal static class WalletCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("wallet", "Show your rewards balances; 'wallet open' starts the wallet.");
        command.Subcommands.Add(CreateOpen(connectionOption, jsonOption));

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var wallet = await provider.GetRequiredService<RewardService>().GetWalletAsync(cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(RewardOutput.WalletJson(wallet));
                    return 0;
                }

                Console.WriteLine(wallet.OpenedAt is { } openedAt
                    ? $"Wallet — opened {RewardOutput.Stamp(openedAt)}"
                    : "Wallet — not opened yet (bsk wallet open)");
                foreach (var line in wallet.Lines)
                {
                    var tracked = line.Currency.Spendable ? "" : "   tracked";
                    Console.WriteLine($"  {line.Currency.Label,-10}{line.Balance,8}{tracked}");
                }

                return 0;
            });
        });

        return command;
    }

    private static Command CreateOpen(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("open", "Open the wallet: nothing that happened before now earns rewards.");

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var opening = await provider.GetRequiredService<RewardService>().OpenAsync(cancellationToken: cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new { openedAt = opening.OpenedAt, created = opening.Created });
                }
                else
                {
                    Console.WriteLine(opening.Created
                        ? $"Opened the wallet at {RewardOutput.Stamp(opening.OpenedAt)}."
                        : $"The wallet is already open (since {RewardOutput.Stamp(opening.OpenedAt)}).");
                }

                return 0;
            });
        });

        return command;
    }
}

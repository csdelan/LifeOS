using System.CommandLine;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk redeem &lt;currency=amount&gt;… --note "…"</c> — spend currencies on a reward
/// (REWARD-3), e.g. <c>bsk redeem freedom=45 --note "Gaming"</c>. The note says what it
/// was for and is required. Only Coins, Freedom, Gems and Karma can be spent, and a
/// redemption the balance can't cover is refused — there is no overdraft.
/// </summary>
internal static class RedeemCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var amountsArgument = new Argument<string[]>("amounts")
        {
            Description = "One or more currency=amount pairs, e.g. freedom=45 coins=100.",
            Arity = ArgumentArity.OneOrMore
        };
        var noteOption = new Option<string?>("--note", "-n")
        {
            Description = "What the redemption is for (required)."
        };

        var command = new Command("redeem", "Spend Coins, Freedom, Gems or Karma on a reward.");
        command.Arguments.Add(amountsArgument);
        command.Options.Add(noteOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var amountTexts = parseResult.GetValue(amountsArgument)!;
            var note = parseResult.GetValue(noteOption);

            return Cli.RunAsync(asJson, async () =>
            {
                var amounts = RewardOutput.ParseAmounts(amountTexts, "redeem expects currency=amount, e.g. freedom=45");

                await using var provider = Cli.BuildServices(connectionString);
                var redemption = await provider.GetRequiredService<RewardService>()
                    .RedeemAsync(amounts, note, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        id = redemption.EventId,
                        amounts = redemption.Amounts,
                        note = redemption.Note,
                        wallet = RewardOutput.WalletJson(redemption.Wallet)
                    });
                    return 0;
                }

                Console.WriteLine(
                    $"Redeemed {RewardOutput.Amounts(redemption.Amounts, signed: false)}: {redemption.Note}.");
                var left = redemption.Amounts.Keys
                    .Select(key => $"{redemption.Wallet.BalanceOf(key)} {Currencies.LabelFor(key)}");
                Console.WriteLine($"Balance: {string.Join(", ", left)}.");
                return 0;
            });
        });

        return command;
    }
}

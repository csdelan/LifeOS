using System.CommandLine;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk redeem [&lt;reward&gt;] [currency=amount…] [--note "…"]</c> — spend currencies
/// (REWARD-3). Against a catalog Reward: <c>bsk redeem "Movie night"</c> pays its price
/// (amounts given override it), and <c>bsk redeem "Bike fund" coins=90</c> deposits Coins
/// into a savings goal; the note is optional. Without one it is a custom redemption, e.g.
/// <c>bsk redeem freedom=45 --note "Gaming"</c>, and the note is required. Only Coins,
/// Freedom, Gems and Karma can be spent; spending Coins records the dollars moved at the
/// configured rate; and a redemption the balance can't cover is refused — there is no
/// overdraft.
/// </summary>
internal static class RedeemCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var argumentsArgument = new Argument<string[]>("reward-and-amounts")
        {
            Description = "An optional Reward (urn, short id, or title — quote one with spaces), then currency=amount pairs, e.g. freedom=45 coins=100.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var noteOption = new Option<string?>("--note", "-n")
        {
            Description = "What the redemption is for (required without a Reward)."
        };

        var command = new Command("redeem", "Spend Coins, Freedom, Gems or Karma on a reward.");
        command.Arguments.Add(argumentsArgument);
        command.Options.Add(noteOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var tokens = parseResult.GetValue(argumentsArgument) ?? [];
            var note = parseResult.GetValue(noteOption);

            return Cli.RunAsync(asJson, async () =>
            {
                // A token with '=' is an amount; the one without is the Reward.
                var names = tokens.Where(t => !t.Contains('=')).ToList();
                if (names.Count > 1)
                {
                    throw new ArgumentException(
                        $"Name one Reward (quote a title with spaces); got {string.Join(", ", names.Select(n => $"'{n}'"))}.");
                }

                var amounts = RewardOutput.ParseAmounts(
                    tokens.Where(t => t.Contains('=')), "redeem expects currency=amount, e.g. freedom=45");

                await using var provider = Cli.BuildServices(connectionString);
                var redemption = await provider.GetRequiredService<RewardService>().RedeemAsync(
                    names.SingleOrDefault(), amounts.Count == 0 ? null : amounts, note, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        id = redemption.EventId,
                        reward = redemption.Reward is { } reward ? RewardOutput.RewardJson(reward) : null,
                        amounts = redemption.Amounts,
                        note = redemption.Note,
                        dollars = redemption.Dollars,
                        coinRate = redemption.CoinRate,
                        wallet = RewardOutput.WalletJson(redemption.Wallet)
                    });
                    return 0;
                }

                var spent = RewardOutput.Amounts(redemption.Amounts, signed: false);
                if (redemption.Dollars is { } dollars)
                {
                    spent += $" ({RewardOutput.Usd(dollars)})";
                }

                var about = redemption.Note is { } text ? $": {text}" : "";
                Console.WriteLine(redemption.Reward switch
                {
                    { Kind: RewardKinds.Savings } goal =>
                        $"Deposited {spent} into {goal.Reward.Title}{about}. {RewardOutput.Describe(goal)}.",
                    { } reward => $"Redeemed {spent} on {reward.Reward.Title}{about}.",
                    null => $"Redeemed {spent}{about}."
                });

                var left = redemption.Amounts.Keys
                    .Select(key => $"{redemption.Wallet.BalanceOf(key)} {Currencies.LabelFor(key)}");
                Console.WriteLine($"Balance: {string.Join(", ", left)}.");
                return 0;
            });
        });

        return command;
    }
}

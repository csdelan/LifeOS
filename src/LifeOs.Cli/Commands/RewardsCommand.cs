using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Activity;
using LifeOs.Application.Food;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk rewards</c> — the reward catalog (REWARD-3) and rewards maintenance:
/// <list type="bullet">
/// <item><c>rewards list [--archived]</c> — the catalog: what each Reward costs, or how far
/// each savings goal has got.</item>
/// <item><c>rewards add &lt;title&gt; [currency=amount…] [--savings [--target &lt;usd&gt;]]</c> —
/// add an instant reward with its price, or a savings goal.</item>
/// <item><c>rewards price &lt;reward&gt; currency=amount…</c> — set an instant reward's price.</item>
/// <item><c>rewards target &lt;reward&gt; &lt;usd&gt; | --clear</c> — set a savings goal's target.</item>
/// <item><c>rewards reconcile [--subject &lt;ref&gt;]</c> — bring the ledger in line with the
/// record (rewards plan §3.3): earn every award that is due and unpaid, and reverse every
/// paid award that is no longer due. Status changes already do this for their own item;
/// this heals one whose reconcile never ran. The full sweep also pays any life-domain
/// rank-up still due (xp-domains plan §3.7) and settles the food days' diet XP (food-log
/// plan §3.4) and the Oura days' activity XP (activity plan §3.8). Safe to run any time — it
/// never pays twice and never opens the wallet.</item>
/// </list>
/// Rename or archive a Reward like any subject (<c>bsk rename</c>, <c>bsk archive</c>);
/// redeem against one with <c>bsk redeem</c>.
/// </summary>
internal static class RewardsCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command(
            "rewards", "The reward catalog (list, add, price, target) and 'rewards reconcile', which heals missed earnings.");
        command.Subcommands.Add(CreateList(connectionOption, jsonOption));
        command.Subcommands.Add(CreateAdd(connectionOption, jsonOption));
        command.Subcommands.Add(CreatePrice(connectionOption, jsonOption));
        command.Subcommands.Add(CreateTarget(connectionOption, jsonOption));
        command.Subcommands.Add(CreateReconcile(connectionOption, jsonOption));
        return command;
    }

    private static Command CreateList(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var archivedOption = new Option<bool>("--archived")
        {
            Description = "Include archived Rewards."
        };

        var command = new Command("list", "The reward catalog: prices, and savings progress.");
        command.Options.Add(archivedOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var includeArchived = parseResult.GetValue(archivedOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var catalog = await provider.GetRequiredService<RewardCatalogService>()
                    .ListAsync(includeArchived, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(catalog.Select(RewardOutput.RewardJson));
                    return 0;
                }

                if (catalog.Count == 0)
                {
                    Console.WriteLine("The catalog is empty; add a reward with bsk rewards add.");
                    return 0;
                }

                foreach (var reward in catalog)
                {
                    var redeemed = reward.Redemptions == 0
                        ? ""
                        : $" — redeemed {reward.Redemptions}x, last {RewardOutput.Stamp(reward.LastRedeemedAt!.Value)}";
                    var archived = reward.Archived ? " (archived)" : "";
                    Console.WriteLine(
                        $"{reward.Reward.Title} ({reward.Kind}, {reward.Reward.Urn}){archived}: {RewardOutput.Describe(reward)}{redeemed}");
                }

                return 0;
            });
        });

        return command;
    }

    private static Command CreateAdd(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var titleArgument = new Argument<string>("title")
        {
            Description = "What the reward is, e.g. \"Movie night\" or \"Bike fund\"."
        };
        var priceArgument = new Argument<string[]>("price")
        {
            Description = "An instant reward's price as currency=amount pairs, e.g. freedom=120.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var savingsOption = new Option<bool>("--savings")
        {
            Description = "Make it a savings goal that Coins are deposited into."
        };
        var targetOption = new Option<string?>("--target")
        {
            Description = "A savings goal's target in dollars, e.g. 500."
        };
        var descriptionOption = new Option<string?>("--description")
        {
            Description = "A line about the reward."
        };

        var command = new Command("add", "Add a reward to the catalog: an instant reward with a price, or a savings goal.");
        command.Arguments.Add(titleArgument);
        command.Arguments.Add(priceArgument);
        command.Options.Add(savingsOption);
        command.Options.Add(targetOption);
        command.Options.Add(descriptionOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var title = parseResult.GetValue(titleArgument)!;
            var priceTexts = parseResult.GetValue(priceArgument) ?? [];
            var savings = parseResult.GetValue(savingsOption);
            var target = parseResult.GetValue(targetOption);
            var description = parseResult.GetValue(descriptionOption);

            return Cli.RunAsync(asJson, async () =>
            {
                var price = RewardOutput.ParseAmounts(priceTexts, "a price is currency=amount, e.g. freedom=120");
                var targetUsd = target is null ? (decimal?)null : ParseUsd(target);

                await using var provider = Cli.BuildServices(connectionString);
                var reward = await provider.GetRequiredService<RewardCatalogService>().AddAsync(
                    title, savings ? RewardKinds.Savings : RewardKinds.Instant, price, targetUsd, description,
                    cancellationToken);

                return Write(reward, asJson, "Added");
            });
        });

        return command;
    }

    private static Command CreatePrice(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var rewardArgument = new Argument<string>("reward")
        {
            Description = "The instant reward to price (urn, short id, or title)."
        };
        var priceArgument = new Argument<string[]>("price")
        {
            Description = "What it costs as currency=amount pairs, e.g. freedom=120 (replaces the old price).",
            Arity = ArgumentArity.OneOrMore
        };

        var command = new Command("price", "Set what an instant reward costs.");
        command.Arguments.Add(rewardArgument);
        command.Arguments.Add(priceArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var reference = parseResult.GetValue(rewardArgument)!;
            var priceTexts = parseResult.GetValue(priceArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                var price = RewardOutput.ParseAmounts(priceTexts, "a price is currency=amount, e.g. freedom=120");

                await using var provider = Cli.BuildServices(connectionString);
                var reward = await provider.GetRequiredService<RewardCatalogService>()
                    .SetPriceAsync(reference, price, cancellationToken);

                return Write(reward, asJson, "Priced");
            });
        });

        return command;
    }

    private static Command CreateTarget(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var rewardArgument = new Argument<string>("reward")
        {
            Description = "The savings goal (urn, short id, or title)."
        };
        var amountArgument = new Argument<string?>("usd")
        {
            Description = "The target in dollars, e.g. 500.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var clearOption = new Option<bool>("--clear")
        {
            Description = "Remove the target."
        };

        var command = new Command("target", "Set or clear a savings goal's target in dollars.");
        command.Arguments.Add(rewardArgument);
        command.Arguments.Add(amountArgument);
        command.Options.Add(clearOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var reference = parseResult.GetValue(rewardArgument)!;
            var amount = parseResult.GetValue(amountArgument);
            var clear = parseResult.GetValue(clearOption);

            return Cli.RunAsync(asJson, async () =>
            {
                if (clear == (amount is not null))
                {
                    throw new ArgumentException("Give a target in dollars, e.g. 500, or --clear — one of them.");
                }

                var targetUsd = amount is null ? (decimal?)null : ParseUsd(amount);

                await using var provider = Cli.BuildServices(connectionString);
                var reward = await provider.GetRequiredService<RewardCatalogService>()
                    .SetTargetAsync(reference, targetUsd, cancellationToken);

                return Write(reward, asJson, clear ? "Cleared the target of" : "Set the target of");
            });
        });

        return command;
    }

    private static Command CreateReconcile(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectOption = new Option<string?>("--subject")
        {
            Description = "Reconcile only this item (urn, short id, or title)."
        };

        var command = new Command("reconcile", "Earn what is due and reverse what no longer is.");
        command.Options.Add(subjectOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var reference = parseResult.GetValue(subjectOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var rewards = provider.GetRequiredService<RewardService>();
                var awards = reference is null
                    ? [
                        .. await rewards.ReconcileAllAsync(cancellationToken),
                        .. (await provider.GetRequiredService<FoodRewards>().SettleAsync(cancellationToken: cancellationToken)).Awards,
                        .. (await provider.GetRequiredService<ActivityRewards>().SettleAsync(cancellationToken)).Awards
                    ]
                    : await rewards.ReconcileAsync(
                        await provider.GetRequiredService<SubjectService>().ResolveAsync(reference, cancellationToken),
                        causeEventId: null,
                        cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new { awards = RewardOutput.AwardsJson(awards) });
                    return 0;
                }

                if (awards.Count == 0)
                {
                    Console.WriteLine("The ledger already matches the record.");
                    return 0;
                }

                RewardOutput.WriteAwards(awards);
                return 0;
            });
        });

        return command;
    }

    private static int Write(CatalogReward reward, bool asJson, string verb)
    {
        if (asJson)
        {
            Cli.WriteJson(RewardOutput.RewardJson(reward));
            return 0;
        }

        Console.WriteLine(
            $"{verb} {reward.Reward.Title} ({reward.Kind}, {reward.Reward.Urn}): {RewardOutput.Describe(reward)}.");
        return 0;
    }

    private static decimal ParseUsd(string text)
        => decimal.TryParse(text.Trim().TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"A target is a dollar amount, e.g. 500 or 1250.50; got '{text}'.");
}

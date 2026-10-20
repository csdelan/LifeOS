using System.CommandLine;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk rewards reconcile [--subject &lt;ref&gt;]</c> — bring the ledger in line with the
/// record (rewards plan §3.3): earn every award that is due and unpaid, and reverse every
/// paid award that is no longer due. Status changes already do this for their own item;
/// this heals one whose reconcile never ran. Safe to run any time — it never pays twice
/// and never opens the wallet.
/// </summary>
internal static class RewardsCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("rewards", "Rewards maintenance: 'rewards reconcile' heals missed earnings.");
        command.Subcommands.Add(CreateReconcile(connectionOption, jsonOption));
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
                    ? await rewards.ReconcileAllAsync(cancellationToken)
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
}

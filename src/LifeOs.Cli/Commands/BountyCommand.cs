using System.CommandLine;
using LifeOs.Application.Rewards;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk bounty &lt;subject&gt; [currency=amount…] [--on &lt;trigger&gt;] [--every &lt;minutes&gt;] [--nothing | --clear]</c>
/// — show or set what an item pays (REWARD-2). With no amounts it shows each trigger's
/// price; <c>karma=3</c> adds Karma to what the trigger pays now (its type default when
/// never priced) and <c>xp=0</c> drops XP; <c>--nothing</c> makes it pay nothing;
/// <c>--clear</c> puts it back on the type default. Already-paid rewards never change.
/// A Habit has several triggers (followed, streak_7, streak_30, streak_100, established),
/// so pricing one needs <c>--on</c>; a partial day always pays half of followed. A Task
/// also has <c>timed</c>: <c>--on timed xp=1 --every 15</c> pays 1 XP for every 15 minutes
/// its timer recorded before it was completed (every minute without <c>--every</c>); a
/// Task left without <c>--on</c> prices <c>completed</c>.
/// </summary>
internal static class BountyCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = new Argument<string>("subject")
        {
            Description = "The Task, Project, Goal or Habit to price (urn, short id, or title)."
        };
        var amountsArgument = new Argument<string[]>("amounts")
        {
            Description = "currency=amount pairs to set, e.g. xp=20 karma=3 (0 drops a currency).",
            Arity = ArgumentArity.ZeroOrMore
        };
        var onOption = new Option<string?>("--on")
        {
            Description = "The trigger to price, e.g. completed, timed, followed, streak_7 (optional for completed, or when the type has one)."
        };
        var everyOption = new Option<int?>("--every")
        {
            Description = "With --on timed: pay once for every this many minutes on the timer (default 1)."
        };
        var nothingOption = new Option<bool>("--nothing")
        {
            Description = "Make the trigger pay nothing."
        };
        var clearOption = new Option<bool>("--clear")
        {
            Description = "Put the trigger (or, without --on, every trigger) back on the type default."
        };

        var command = new Command("bounty", "Show or set what an item earns.");
        command.Arguments.Add(subjectArgument);
        command.Arguments.Add(amountsArgument);
        command.Options.Add(onOption);
        command.Options.Add(everyOption);
        command.Options.Add(nothingOption);
        command.Options.Add(clearOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var subject = parseResult.GetValue(subjectArgument)!;
            var amountTexts = parseResult.GetValue(amountsArgument) ?? [];
            var on = parseResult.GetValue(onOption);
            var every = parseResult.GetValue(everyOption);
            var nothing = parseResult.GetValue(nothingOption);
            var clear = parseResult.GetValue(clearOption);

            return Cli.RunAsync(asJson, async () =>
            {
                var changes = (amountTexts.Length > 0 ? 1 : 0) + (nothing ? 1 : 0) + (clear ? 1 : 0);
                if (changes > 1)
                {
                    throw new ArgumentException("Give amounts, --nothing or --clear — one at a time.");
                }

                if (clear && every is not null)
                {
                    throw new ArgumentException("--clear puts the trigger back on its default, minutes included; leave out --every.");
                }

                await using var provider = Cli.BuildServices(connectionString);
                var bounties = provider.GetRequiredService<BountyService>();
                var view = amountTexts.Length > 0
                    ? await bounties.SetAsync(
                        subject, on,
                        RewardOutput.ParseAmounts(amountTexts, "bounty expects currency=amount, e.g. xp=20"),
                        every, cancellationToken)
                    : nothing
                        ? await bounties.SetNothingAsync(subject, on, every, cancellationToken)
                        : clear
                            ? await bounties.ClearAsync(subject, on, cancellationToken)
                            : every is not null
                                ? await bounties.SetAsync(subject, on, null, every, cancellationToken)
                                : await bounties.GetAsync(subject, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        subject = new { urn = view.Subject.Urn, type = view.Subject.Type, title = view.Subject.Title },
                        bounty = view.Lines.Select(l => new
                        {
                            trigger = l.Trigger,
                            amounts = l.Amounts,
                            everyMinutes = l.EveryMinutes,
                            custom = l.IsCustom
                        })
                    });
                    return 0;
                }

                Console.WriteLine($"{view.Subject.Title} ({view.Subject.Type}, {view.Subject.Urn}) pays:");
                foreach (var line in view.Lines)
                {
                    var amounts = line.Amounts.Count == 0 ? "nothing" : RewardOutput.Amounts(line.Amounts);
                    var per = line.EveryMinutes is { } minutes && line.Amounts.Count > 0
                        ? minutes == 1 ? " per minute on the timer" : $" per {minutes} minutes on the timer"
                        : "";
                    Console.WriteLine($"  {line.Trigger}: {amounts}{per}{(line.IsCustom ? "" : " (default)")}");
                }

                return 0;
            });
        });

        return command;
    }
}

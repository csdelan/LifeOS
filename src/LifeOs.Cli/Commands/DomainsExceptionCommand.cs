using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Exception days (REWARD-8, xp-domains plan §7.4.3): a day of sickness, injury or travel that
/// neither helps nor hurts the life domains.
/// <list type="bullet">
/// <item><c>bsk domains exception [&lt;date&gt;] "&lt;comment&gt;"</c> marks a day — today unless
/// a date is given (<c>yyyy-MM-dd</c>, <c>today</c> or <c>yesterday</c>) — with a required
/// one-line comment. Marking it again with another comment changes the comment.</item>
/// <item><c>bsk domains exception --unmark &lt;date&gt;</c> unmarks it.</item>
/// <item><c>bsk domains exceptions [--days N]</c> lists the marked days among the last N (90
/// by default), newest first.</item>
/// </list>
/// A write prints any rank-up it paid and each domain whose level or rank it moved, as
/// <c>bsk adhere</c> does. Dates are home-zone days (<c>Rewards:HomeTimeZone</c>).
/// </summary>
internal static class DomainsExceptionCommand
{
    public static Command CreateMark(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var wordsArgument = new Argument<string[]>("words")
        {
            Description = "[<date>] \"<comment>\": the day (yyyy-MM-dd, today or yesterday; today when left out) and why it's an exception.",
            Arity = new ArgumentArity(0, 2)
        };
        var unmarkOption = new Option<string?>("--unmark")
        {
            Description = "Unmark this day (yyyy-MM-dd, today or yesterday) instead."
        };

        var command = new Command("exception", "Mark a day as an exception (sick, injured, travelling), or unmark it.");
        command.Arguments.Add(wordsArgument);
        command.Options.Add(unmarkOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var words = parseResult.GetValue(wordsArgument) ?? [];
            var unmark = parseResult.GetValue(unmarkOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<ExceptionDayService>();
                var today = await provider.GetRequiredService<IHomeCalendar>()
                    .DateAtAsync(provider.GetRequiredService<IClock>().UtcNow, cancellationToken);

                ExceptionDayChange change;
                if (unmark is not null)
                {
                    if (words.Length > 0)
                    {
                        throw new ArgumentException("--unmark takes only the day: leave out the comment.");
                    }

                    var day = ParseDay(unmark, today);
                    change = await service.UnmarkAsync(day, cancellationToken);
                    if (!asJson)
                    {
                        Console.WriteLine(change.Earned.Progress.Count == 0 && change.Earned.Awards.Count == 0
                            ? $"{Day(day)} counts like any other day."
                            : $"Unmarked {Day(day)}.");
                    }
                }
                else
                {
                    var (day, comment) = words.Length switch
                    {
                        0 => throw new ArgumentException("Say why the day is an exception: bsk domains exception [<date>] \"<comment>\"."),
                        1 => ((DateOnly?)null, words[0]),
                        _ => (ParseDay(words[0], today), words[1])
                    };
                    change = await service.MarkAsync(day, comment, cancellationToken);
                    if (!asJson && change.Exception is { } marked)
                    {
                        Console.WriteLine($"{Day(marked.Day)} is an exception: {marked.Comment}");
                    }
                }

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        exception = change.Exception is { } x ? Json(x) : null,
                        awards = RewardOutput.AwardsJson(change.Earned.Awards),
                        progress = RewardOutput.ProgressJson(change.Earned.Progress)
                    });
                    return 0;
                }

                RewardOutput.WriteAwards(change.Earned.Awards);
                RewardOutput.WriteProgress(change.Earned.Progress);
                return 0;
            });
        });

        return command;
    }

    public static Command CreateList(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var daysOption = new Option<int>("--days")
        {
            Description = $"How many days back to list, today included (default {ExceptionDayService.DefaultDays}).",
            DefaultValueFactory = _ => ExceptionDayService.DefaultDays
        };

        var command = new Command("exceptions", "List the days marked as exceptions, newest first.");
        command.Options.Add(daysOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var days = parseResult.GetValue(daysOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var list = await provider.GetRequiredService<ExceptionDayService>().ListAsync(days, cancellationToken);
                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        today = list.Today,
                        from = list.Today.AddDays(1 - list.Days),
                        exceptions = list.Marked.Select(Json)
                    });
                    return 0;
                }

                var from = list.Today.AddDays(1 - list.Days);
                if (list.Marked.Count == 0)
                {
                    Console.WriteLine($"No exception days from {Day(from)} to {Day(list.Today)}.");
                    return 0;
                }

                Console.WriteLine($"Exception days from {Day(from)} to {Day(list.Today)}:");
                foreach (var d in list.Marked)
                {
                    Console.WriteLine($"  {Day(d.Day)}  {d.Comment}");
                }

                return 0;
            });
        });

        return command;
    }

    /// <summary>A day as the verbs take it: <c>yyyy-MM-dd</c>, <c>today</c> or <c>yesterday</c>.</summary>
    internal static DateOnly ParseDay(string text, DateOnly today)
    {
        var day = text.Trim();
        if (string.Equals(day, "today", StringComparison.OrdinalIgnoreCase))
        {
            return today;
        }

        if (string.Equals(day, "yesterday", StringComparison.OrdinalIgnoreCase))
        {
            return today.AddDays(-1);
        }

        return DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new ArgumentException($"'{text}' isn't a day: give yyyy-MM-dd, today or yesterday.");
    }

    private static object Json(ExceptionDay day) => new { day = day.Day, comment = day.Comment, markedAt = day.MarkedAt };

    private static string Day(DateOnly day) => day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture);
}

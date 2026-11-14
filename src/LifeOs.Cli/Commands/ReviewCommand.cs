using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Reviews;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Daily and weekly reviews (REVIEW-1, REVIEW-2, reviews plan §3):
/// <list type="bullet">
/// <item><c>bsk review open daily|weekly [--date D]</c> opens the review holding the day (today by
/// default), making it the first time. A weekly review is the Monday-to-Sunday week's, named by
/// its Sunday, and opens on that Sunday.</item>
/// <item><c>bsk review complete daily|weekly [--date D]</c> completes it (opening it first if need
/// be). Late is fine: it stays missed.</item>
/// <item><c>bsk review list [--kind K] [--from D] [--to D]</c> lists them with what each reads as
/// today: Open, Completed (late, when it was) or Missed.</item>
/// </list>
/// <c>D</c> is yyyy-MM-dd, <c>today</c> or <c>yesterday</c>, a home-zone day.
/// </summary>
internal static class ReviewCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("review", "Daily and weekly reviews: open, complete and list.");
        command.Subcommands.Add(CreateOpen(connectionOption, jsonOption, complete: false));
        command.Subcommands.Add(CreateOpen(connectionOption, jsonOption, complete: true));
        command.Subcommands.Add(CreateList(connectionOption, jsonOption));
        return command;
    }

    private static Command CreateOpen(Option<string?> connectionOption, Option<bool> jsonOption, bool complete)
    {
        var kindArgument = new Argument<string>("kind") { Description = "daily or weekly." };
        var dateOption = new Option<string?>("--date")
        {
            Description = "A day in the review's period (yyyy-MM-dd, today or yesterday). Today by default."
        };

        var command = complete
            ? new Command("complete", "Complete a review, opening it first if it isn't open yet. Late is fine: it stays missed.")
            : new Command("open", "Open the review for a day or a week, making it the first time.");
        command.Arguments.Add(kindArgument);
        command.Options.Add(dateOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<ReviewService>();
                var today = await service.TodayAsync(cancellationToken);
                var day = ParseDay(parseResult.GetValue(dateOption), today);
                var kind = parseResult.GetValue(kindArgument)!;

                if (complete)
                {
                    var review = await service.CompleteAsync(kind, day, cancellationToken);
                    Write(review, today, asJson, "Completed");
                }
                else
                {
                    var opened = await service.OpenAsync(kind, day, cancellationToken);
                    Write(opened.Review, today, asJson, opened.Created ? "Opened" : "Already open");
                }

                return 0;
            });
        });
        return command;
    }

    private static Command CreateList(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var kindOption = new Option<string?>("--kind") { Description = "daily or weekly; both by default." };
        var fromOption = new Option<string?>("--from") { Description = "The first period to list (yyyy-MM-dd). 28 days ago by default." };
        var toOption = new Option<string?>("--to") { Description = "The last period to list (yyyy-MM-dd). Today by default." };

        var command = new Command("list", "The reviews in a range of days, with what each reads as today.");
        command.Options.Add(kindOption);
        command.Options.Add(fromOption);
        command.Options.Add(toOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var today = await provider.GetRequiredService<ReviewService>().TodayAsync(cancellationToken);
                var kindText = parseResult.GetValue(kindOption);
                var kind = kindText is null ? null : ReviewService.KindOf(kindText);
                var from = ParseDay(parseResult.GetValue(fromOption), today) ?? today.AddDays(-28);
                var to = ParseDay(parseResult.GetValue(toOption), today) ?? today;

                var reviews = await provider.GetRequiredService<IReviewRepository>()
                    .ListAsync(kind, from, to, cancellationToken: cancellationToken);
                if (asJson)
                {
                    Cli.WriteJson(reviews.Select(r => ReviewRow.From(r, today)).ToArray());
                    return 0;
                }

                if (reviews.Count == 0)
                {
                    Console.WriteLine($"No reviews from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}.");
                    return 0;
                }

                foreach (var review in reviews)
                {
                    Console.WriteLine($"{review.Period:yyyy-MM-dd}  {review.Kind,-6}  {Reads(review, today),-16}  {review.Urn}");
                }

                return 0;
            });
        });
        return command;
    }

    private static void Write(Review review, DateOnly today, bool asJson, string verb)
    {
        if (asJson)
        {
            Cli.WriteJson(ReviewRow.From(review, today));
            return;
        }

        var (from, to) = review.LooksBack;
        var span = from == to ? ReviewPeriods.Day(from) : $"{ReviewPeriods.Day(from)} – {ReviewPeriods.Day(to)}";
        Console.WriteLine($"{verb}: {review.Title} ({span}), {Reads(review, today)}. {review.Urn}");
    }

    /// <summary>What a review reads as today: "Open", "Missed", "Completed" or "Completed late".</summary>
    private static string Reads(Review review, DateOnly today)
        => review.CompletedLate ? "Completed late" : review.StatusAsOf(today);

    private static DateOnly? ParseDay(string? text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (string.Equals(trimmed, "today", StringComparison.OrdinalIgnoreCase))
        {
            return today;
        }

        if (string.Equals(trimmed, "yesterday", StringComparison.OrdinalIgnoreCase))
        {
            return today.AddDays(-1);
        }

        return DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new ArgumentException($"'{trimmed}' isn't a day. Use yyyy-MM-dd, today or yesterday.");
    }
}

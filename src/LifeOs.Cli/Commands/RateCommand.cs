using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Home;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Rates a space (HOME-2, environment plan §4.7), 1–5 in half points:
/// <list type="bullet">
/// <item><c>bsk rate &lt;space&gt; &lt;tidiness&gt; &lt;cleanliness&gt; &lt;aesthetics&gt;</c> rates all three;</item>
/// <item><c>bsk rate &lt;space&gt; --tidiness 4 --cleanliness 4.5</c> rates some (a quick check);</item>
/// <item><c>bsk rate &lt;space&gt; --same</c> confirms the space's last rating;</item>
/// <item><c>bsk rate --edit &lt;id&gt; [--tidiness …] [--day …] [--note …]</c> changes a rating, and
/// <c>bsk rate --delete &lt;id&gt;</c> deletes one;</item>
/// <item><c>bsk rate --list [&lt;space&gt;] [--days N]</c> lists the ratings, newest first.</item>
/// </list>
/// Each takes <c>--day yesterday|&lt;date&gt;</c> (today by default) and <c>--note "…"</c>. A write
/// prints any rank-up it paid and each domain whose level or rank it moved.
/// </summary>
internal static class RateCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var wordsArgument = new Argument<string[]>("words")
        {
            Description = "<space> [<tidiness> <cleanliness> <aesthetics>]: the space by name, then its ratings, 1–5 in halves.",
            Arity = new ArgumentArity(0, 4)
        };
        var tidinessOption = new Option<decimal?>("--tidiness") { Description = "Tidiness, 1–5 in halves: how de-cluttered it is." };
        var cleanlinessOption = new Option<decimal?>("--cleanliness") { Description = "Cleanliness, 1–5 in halves: how washed it is." };
        var aestheticsOption = new Option<decimal?>("--aesthetics") { Description = "Aesthetics, 1–5 in halves: decor, colour, light." };
        var sameOption = new Option<bool>("--same") { Description = "Confirm the space's last rating, as it was rated." };
        var dayOption = new Option<string?>("--day") { Description = "The day rated: yyyy-MM-dd, today or yesterday (today by default)." };
        var noteOption = new Option<string?>("--note") { Description = "A one-line note, up to 140 characters." };
        var editOption = new Option<string?>("--edit") { Description = "Change the rating with this id (or the start of it)." };
        var deleteOption = new Option<string?>("--delete") { Description = "Delete the rating with this id (or the start of it)." };
        var listOption = new Option<bool>("--list") { Description = "List the ratings, newest first (of one space when named)." };
        var daysOption = new Option<int>("--days")
        {
            Description = $"With --list, how many days back (default {SpaceRatingService.DefaultDays}).",
            DefaultValueFactory = _ => SpaceRatingService.DefaultDays
        };

        var command = new Command("rate", "Rate a space on Tidiness, Cleanliness and Aesthetics, 1–5 in half points.");
        command.Arguments.Add(wordsArgument);
        foreach (var option in new Option[]
                 {
                     tidinessOption, cleanlinessOption, aestheticsOption, sameOption, dayOption, noteOption,
                     editOption, deleteOption, listOption, daysOption
                 })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var words = parseResult.GetValue(wordsArgument) ?? [];
            var edit = parseResult.GetValue(editOption);
            var delete = parseResult.GetValue(deleteOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<SpaceRatingService>();
                var spaces = await provider.GetRequiredService<SpaceService>().ListAsync(true, cancellationToken);
                var today = await provider.GetRequiredService<IHomeCalendar>()
                    .DateAtAsync(provider.GetRequiredService<IClock>().UtcNow, cancellationToken);
                var dayText = parseResult.GetValue(dayOption);
                DateOnly? day = dayText is null ? null : DomainsExceptionCommand.ParseDay(dayText, today);

                if (parseResult.GetValue(listOption))
                {
                    if (words.Length > 1)
                    {
                        throw new ArgumentException("--list takes at most a space's name.");
                    }

                    var list = await service.ListAsync(
                        words.Length == 1 ? words[0] : null, parseResult.GetValue(daysOption), cancellationToken);
                    if (asJson)
                    {
                        Cli.WriteJson(new { today = list.Today, ratings = list.Ratings.Select(r => Json(r, spaces)) });
                        return 0;
                    }

                    if (list.Ratings.Count == 0)
                    {
                        Console.WriteLine($"No ratings in the last {list.Days} days.");
                        return 0;
                    }

                    foreach (var r in list.Ratings)
                    {
                        Console.WriteLine($"  {Short(r.Id)}  {Describe(r, spaces)}");
                    }

                    return 0;
                }

                RatingWriteResult result;
                if (delete is not null)
                {
                    result = await service.DeleteAsync(delete, cancellationToken);
                    if (!asJson)
                    {
                        Console.WriteLine("Deleted the rating.");
                    }
                }
                else if (edit is not null)
                {
                    if (words.Length > 0)
                    {
                        throw new ArgumentException("--edit takes its changes as options (--tidiness 4, --day yesterday, --note …).");
                    }

                    result = await service.EditAsync(
                        edit,
                        new RatingEdit(
                            day,
                            parseResult.GetValue(tidinessOption),
                            parseResult.GetValue(cleanlinessOption),
                            parseResult.GetValue(aestheticsOption),
                            parseResult.GetValue(noteOption)),
                        cancellationToken);
                }
                else
                {
                    if (words.Length == 0)
                    {
                        throw new ArgumentException("Say which space to rate: bsk rate \"Office\" 4.5 4 3.5");
                    }

                    var numbers = words.Skip(1).Select(Number).ToList();
                    if (numbers.Count > 0 && (parseResult.GetValue(tidinessOption) is not null
                                              || parseResult.GetValue(cleanlinessOption) is not null
                                              || parseResult.GetValue(aestheticsOption) is not null))
                    {
                        throw new ArgumentException("Give the ratings in order or as --tidiness/--cleanliness/--aesthetics, not both.");
                    }

                    var input = new RatingInput(
                        words[0],
                        numbers.ElementAtOrDefault(0) ?? parseResult.GetValue(tidinessOption),
                        numbers.ElementAtOrDefault(1) ?? parseResult.GetValue(cleanlinessOption),
                        numbers.ElementAtOrDefault(2) ?? parseResult.GetValue(aestheticsOption),
                        parseResult.GetValue(noteOption),
                        parseResult.GetValue(sameOption));
                    result = await service.RateAsync(day, [input], cancellationToken);
                }

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        ratings = result.Ratings.Select(r => Json(r, spaces)),
                        awards = RewardOutput.AwardsJson(result.Earned.Awards),
                        progress = RewardOutput.ProgressJson(result.Earned.Progress)
                    });
                    return 0;
                }

                foreach (var r in result.Ratings)
                {
                    Console.WriteLine($"Rated {Describe(r, spaces)}  ({Short(r.Id)})");
                }

                RewardOutput.WriteAwards(result.Earned.Awards);
                RewardOutput.WriteProgress(result.Earned.Progress);
                return 0;
            });
        });

        return command;
    }

    /// <summary>A rating in words: "Office on Tue 2026-10-27: tidiness 4.5, cleanliness 4 — note".</summary>
    internal static string Describe(SpaceRating rating, IReadOnlyList<Space> spaces)
    {
        var name = spaces.FirstOrDefault(s => s.Id == rating.SpaceId)?.Name ?? "a space";
        var parts = HomeParts.All
            .Where(p => rating.Of(p) is not null)
            .Select(p => $"{p} {Rating(rating.Of(p)!.Value)}");
        var note = rating.Note is { } n ? $" — {n}" : "";
        return $"{name} on {rating.Day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture)}: {string.Join(", ", parts)}{note}";
    }

    internal static object Json(SpaceRating rating, IReadOnlyList<Space> spaces) => new
    {
        id = rating.Id,
        spaceId = rating.SpaceId,
        space = spaces.FirstOrDefault(s => s.Id == rating.SpaceId)?.Name,
        day = rating.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        tidiness = rating.Tidiness,
        cleanliness = rating.Cleanliness,
        aesthetics = rating.Aesthetics,
        note = rating.Note,
        ratedAt = rating.RatedAt
    };

    internal static string Rating(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static decimal? Number(string text)
        => decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"'{text}' isn't a rating: give 1 to 5 in half points (1, 1.5 … 5).");

    private static string Short(Guid id) => id.ToString("N")[..8];
}

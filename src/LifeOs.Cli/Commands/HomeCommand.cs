using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Home;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// The Home Score (HOME-3, environment plan §4.7): <c>bsk home</c> prints today's score, each
/// space with its parts as rated and as they've faded, and Fix next — the three parts that cost
/// the score most. <c>--days N</c> adds the daily scores. Scores show floored to a tenth, as the
/// web shows them; <c>--json</c> gives them exact, in points.
/// </summary>
internal static class HomeCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var daysOption = new Option<int?>("--days") { Description = "Also list the Home Score on each of the last N days (1–731)." };

        var command = new Command("home", "The Home Score: each space's parts as they've faded, and what to fix next.");
        command.Options.Add(daysOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var days = parseResult.GetValue(daysOption);
            return Cli.RunAsync(asJson, async () =>
            {
                if (days is { } count)
                {
                    SpaceRatingService.Check(count);
                }

                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<HomeService>();
                var home = await service.GetAsync(cancellationToken);
                var series = days is { } n ? await service.GetDaysAsync(n, cancellationToken) : null;
                var names = (series?.Spaces ?? home.Spaces).ToDictionary(s => s.Id, s => s.Name);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        today = Day(home.Today),
                        settledThrough = Day(home.SettledThrough),
                        homeScore = Points(home.Score),
                        spaces = home.Spaces.Select(space =>
                        {
                            var day = home.Of(space.Id);
                            return new
                            {
                                id = space.Id,
                                name = space.Name,
                                weight = space.Weight,
                                pace = space.Pace,
                                score = Points(day?.Score),
                                lastRatedOn = day?.LastRatedOn is { } last ? Day(last) : null,
                                parts = HomeParts.All.ToDictionary(p => p, p => PartJson(day?.Part(p)))
                            };
                        }),
                        fixNext = home.FixNext.Select(x => new
                        {
                            spaceId = x.SpaceId,
                            space = names.GetValueOrDefault(x.SpaceId),
                            part = x.Part.Part,
                            drag = Points(x.Part.Drag)
                        }),
                        days = series?.Days.Select(d => new
                        {
                            day = Day(d.Day),
                            homeScore = Points(d.Score),
                            exception = series.Exceptions.GetValueOrDefault(d.Day)
                        })
                    });
                    return 0;
                }

                if (home.Spaces.Count == 0)
                {
                    Console.WriteLine("No spaces yet: add one with bsk space add \"Office\" --weight 5 --pace 5.");
                    return 0;
                }

                var counted = home.Day.Spaces.Count(s => s.Counted);
                Console.WriteLine(home.Score is { } score
                    ? $"Home Score {Shown(score)} on {home.Today.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture)} ({counted} of {home.Spaces.Count} spaces counted)"
                    : $"No Home Score yet: rate all three parts of a space (bsk rate \"{home.Spaces[0].Name}\" 4 4 3.5).");
                Console.WriteLine();

                var width = home.Spaces.Max(s => s.Name.Length);
                foreach (var space in home.Spaces)
                {
                    var day = home.Of(space.Id);
                    var spaceScore = day?.Score is { } s ? Shown(s).PadLeft(5) : "  —  ";
                    var parts = HomeParts.All.Select(p => PartText(day?.Part(p)));
                    Console.WriteLine($"  {space.Name.PadRight(width)}  {spaceScore}   {string.Join("   ", parts)}");
                }

                if (home.FixNext.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Fix next:");
                    foreach (var (spaceId, part) in home.FixNext)
                    {
                        Console.WriteLine($"  {names.GetValueOrDefault(spaceId)} {part.Part}  −{Shown(part.Drag)}");
                    }
                }

                if (series is not null)
                {
                    Console.WriteLine();
                    foreach (var d in series.Days.AsEnumerable().Reverse())
                    {
                        var shown = d.Score is { } s ? Shown(s).PadLeft(5) : "    —";
                        var exception = series.Exceptions.TryGetValue(d.Day, out var comment) ? $"  exception: {comment}" : "";
                        Console.WriteLine($"  {d.Day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture)}  {shown}{exception}");
                    }
                }

                return 0;
            });
        });
        return command;
    }

    /// <summary>A part in words: "tidiness 4.5 → 75.0" (as rated, then its points now), or "tidiness —" when never rated.</summary>
    private static string PartText(PartValue? part)
        => part?.Rated is { } rated && part.Value is { } value
            ? $"{part.Part} {RateCommand.Rating(rated)} → {Shown(value)}"
            : $"{part?.Part ?? ""} —";

    private static object? PartJson(PartValue? part) => part is null
        ? null
        : new
        {
            rated = part.Rated,
            ratedOn = part.RatedOn is { } on ? Day(on) : null,
            now = Points(part.Value),
            drag = Points(part.Drag)
        };

    /// <summary>Points floored to a tenth, as the web shows them: 636,805 → "63.6".</summary>
    internal static string Shown(long value)
        => (Math.Floor(value / (HomeRules.Unit / 10m)) / 10m).ToString("0.0", CultureInfo.InvariantCulture);

    private static decimal? Points(long? value) => value is { } v ? v / (decimal)HomeRules.Unit : null;

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

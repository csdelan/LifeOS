using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Food;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk food</c> — the Food Log (FOOD-1..4, food-log plan §3.5):
/// <code>
/// bsk food log "Salmon salad" --score 5 --cal 650 [--at 12:30] [--type lunch]
///              [--protein 40 --carbs 20 --fat 30] [--end-fast]
/// bsk food log "Turkey wrap" --score 3.5 --cal 550  (a score is 1 to 5 in whole or half points)
/// bsk food log "IPA" --alcohol --cal 200            (a drink: its score is always 1)
/// bsk food edit &lt;entry&gt; [--name …] [--score …] [--cal …] [--at …] [--type …] [nutrients]
///              [--alcohol | --no-alcohol]
/// bsk food drinks [--days 30 | --from … --to …]
/// bsk food delete &lt;entry&gt;
/// bsk food photo &lt;entry&gt; --file &lt;path&gt; | --remove
/// bsk food fast begin [--at …] | end [--at …] | record --from … --to …
/// bsk food fast edit &lt;fast&gt; [--from …] [--to …] | delete &lt;fast&gt;
/// bsk food close [&lt;date&gt;]
/// bsk food [&lt;date&gt;] [--week | --month]
/// </code>
/// Times are home-zone wall-clock times: <c>12:30</c> or <c>7:30pm</c> (today, or yesterday
/// when that's still ahead), <c>yesterday 20:00</c>, <c>2026-10-19 19:00</c>, or an ISO instant
/// with an offset. An entry or a fast is named by its id or its first 8 characters, as the
/// day's listing prints them.
/// </summary>
internal static class FoodCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var dateArgument = new Argument<string?>("date")
        {
            Description = "The day to show (YYYY-MM-DD, 'today' or 'yesterday'). Defaults to today in the home zone.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var weekOption = new Option<bool>("--week") { Description = "Show the Monday-to-Sunday week holding the day." };
        var monthOption = new Option<bool>("--month") { Description = "Show the month holding the day." };

        var command = new Command("food", "Log food and fasts, close a food day, and show a day, week or month.");
        command.Arguments.Add(dateArgument);
        command.Options.Add(weekOption);
        command.Options.Add(monthOption);
        command.Subcommands.Add(CreateLog(connectionOption, jsonOption));
        command.Subcommands.Add(CreateEdit(connectionOption, jsonOption));
        command.Subcommands.Add(CreateDelete(connectionOption, jsonOption));
        command.Subcommands.Add(CreatePhoto(connectionOption, jsonOption));
        command.Subcommands.Add(CreateFast(connectionOption, jsonOption));
        command.Subcommands.Add(CreateClose(connectionOption, jsonOption));
        command.Subcommands.Add(CreateDrinks(connectionOption, jsonOption));

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var dateText = parseResult.GetValue(dateArgument);
            var week = parseResult.GetValue(weekOption);
            var month = parseResult.GetValue(monthOption);

            return Cli.RunAsync(asJson, async () =>
            {
                if (week && month)
                {
                    throw new ArgumentException("Use --week or --month, not both.");
                }

                await using var provider = Cli.BuildServices(connectionString);
                var food = provider.GetRequiredService<FoodLogService>();
                var day = await ParseDayAsync(dateText, food, cancellationToken);

                if (!week && !month)
                {
                    var view = await food.GetDayAsync(day, cancellationToken);
                    if (asJson)
                    {
                        Cli.WriteJson(FoodOutput.DayJson(view));
                    }
                    else
                    {
                        FoodOutput.WriteDay(view, food.Rules);
                    }

                    return 0;
                }

                var target = day ?? await food.TodayAsync(cancellationToken);
                var (from, to) = week
                    ? (target.AddDays(-(((int)target.DayOfWeek + 6) % 7)), target.AddDays(6 - (((int)target.DayOfWeek + 6) % 7)))
                    : (new DateOnly(target.Year, target.Month, 1),
                       new DateOnly(target.Year, target.Month, DateTime.DaysInMonth(target.Year, target.Month)));
                var range = await food.GetDaysAsync(from, to, cancellationToken);
                if (asJson)
                {
                    Cli.WriteJson(FoodOutput.RangeJson(range));
                }
                else
                {
                    FoodOutput.WriteRange(range, food.Rules);
                }

                return 0;
            });
        });

        return command;
    }

    // ---------------------------------------------------------------------------------
    // log / edit / delete
    // ---------------------------------------------------------------------------------

    private sealed record EntryOptions(
        Option<decimal?> Score, Option<int?> Calories, Option<string?> At, Option<string?> Type,
        Option<int?> Protein, Option<int?> Carbs, Option<int?> Fat)
    {
        public static EntryOptions Create() => new(
            new Option<decimal?>("--score") { Description = "How healthy it was, 1 (junk) to 5 (very healthy), in whole or half points (3.5)." },
            new Option<int?>("--cal", "--calories") { Description = "Rough calories, 0 to 5,000." },
            new Option<string?>("--at") { Description = "When it was eaten (home-zone time, e.g. 12:30 or \"2026-10-19 19:00\"). Defaults to now." },
            new Option<string?>("--type") { Description = "breakfast | lunch | dinner | snack | drink. Guessed from the time when left out." },
            new Option<int?>("--protein") { Description = "Protein, whole grams (optional)." },
            new Option<int?>("--carbs") { Description = "Carbs, whole grams (optional)." },
            new Option<int?>("--fat") { Description = "Fat, whole grams (optional)." });

        public void AddTo(Command command)
        {
            command.Options.Add(Score);
            command.Options.Add(Calories);
            command.Options.Add(At);
            command.Options.Add(Type);
            command.Options.Add(Protein);
            command.Options.Add(Carbs);
            command.Options.Add(Fat);
        }
    }

    private static Command CreateLog(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var nameArgument = new Argument<string>("name") { Description = "What it was, e.g. \"Salmon salad\"." };
        var options = EntryOptions.Create();
        var endFastOption = new Option<bool>("--end-fast") { Description = "End the running fast at this entry's time." };
        var alcoholOption = new Option<bool>("--alcohol")
        {
            Description = "One alcoholic drink (a beer, a glass of wine, a shot): its score is always 1 and it counts toward the day's drinks."
        };
        var photoFromOption = new Option<string?>("--photo-from")
        {
            Description = "Show this entry's photo too (its id or first 8 characters), as Log again does. Nothing is uploaded again."
        };

        var command = new Command("log", "Log a food entry: a name, a 1–5 health score (halves allowed) and rough calories.");
        command.Arguments.Add(nameArgument);
        options.AddTo(command);
        command.Options.Add(endFastOption);
        command.Options.Add(alcoholOption);
        command.Options.Add(photoFromOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                var alcoholic = parseResult.GetValue(alcoholOption);
                var score = parseResult.GetValue(options.Score)
                            ?? (alcoholic
                                ? FoodEntries.DrinkScore
                                : throw new ArgumentException("Give it a health score: --score 1 (junk) to 5 (very healthy), halves allowed (3.5)."));
                if (alcoholic && score != FoodEntries.DrinkScore)
                {
                    throw new ArgumentException("A drink's score is always 1: leave --score out.");
                }
                var calories = parseResult.GetValue(options.Calories)
                               ?? throw new ArgumentException("Give it rough calories: --cal 650 (0 for a black coffee).");

                await using var provider = Cli.BuildServices(connectionString);
                var food = provider.GetRequiredService<FoodLogService>();
                var input = new FoodEntryInput(
                    parseResult.GetValue(nameArgument)!, score, calories,
                    await ParseAtAsync(parseResult.GetValue(options.At), food, cancellationToken),
                    parseResult.GetValue(options.Type),
                    parseResult.GetValue(options.Protein), parseResult.GetValue(options.Carbs), parseResult.GetValue(options.Fat),
                    alcoholic);
                var photo = parseResult.GetValue(photoFromOption) is { } source
                    ? await food.PhotoOfAsync(source, cancellationToken)
                    : (Guid?)null;
                var result = await food.LogAsync(input, parseResult.GetValue(endFastOption), photo, cancellationToken);
                FoodOutput.WriteResult(result, asJson);
                return 0;
            });
        });

        return command;
    }

    private static Command CreateEdit(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var entryArgument = new Argument<string>("entry") { Description = "The entry (its id or first 8 characters)." };
        var nameOption = new Option<string?>("--name") { Description = "A new name." };
        var options = EntryOptions.Create();
        var alcoholOption = new Option<bool>("--alcohol") { Description = "Make it an alcoholic drink (its score becomes 1)." };
        var noAlcoholOption = new Option<bool>("--no-alcohol") { Description = "It isn't an alcoholic drink after all." };

        var command = new Command("edit", "Change a food entry. What you leave out stays as it was.");
        command.Arguments.Add(entryArgument);
        command.Options.Add(nameOption);
        options.AddTo(command);
        command.Options.Add(alcoholOption);
        command.Options.Add(noAlcoholOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var food = provider.GetRequiredService<FoodLogService>();
                var reference = parseResult.GetValue(entryArgument)!;
                var meal = await food.ResolveMealAsync(reference, cancellationToken);
                var (on, off) = (parseResult.GetValue(alcoholOption), parseResult.GetValue(noAlcoholOption));
                if (on && off)
                {
                    throw new ArgumentException("Use --alcohol or --no-alcohol, not both.");
                }

                var input = new FoodEntryInput(
                    parseResult.GetValue(nameOption) ?? meal.Name,
                    parseResult.GetValue(options.Score) ?? meal.Score,
                    parseResult.GetValue(options.Calories) ?? meal.Calories,
                    await ParseAtAsync(parseResult.GetValue(options.At), food, cancellationToken) ?? meal.EatenAt,
                    parseResult.GetValue(options.Type) ?? meal.MealType,
                    parseResult.GetValue(options.Protein) ?? meal.ProteinG,
                    parseResult.GetValue(options.Carbs) ?? meal.CarbsG,
                    parseResult.GetValue(options.Fat) ?? meal.FatG,
                    on ? true : off ? false : null);
                var result = await food.EditAsync(meal.Id.ToString(), input, cancellationToken);
                FoodOutput.WriteResult(result, asJson);
                return 0;
            });
        });

        return command;
    }

    private static Command CreateDelete(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var entryArgument = new Argument<string>("entry") { Description = "The entry (its id or first 8 characters)." };
        var command = new Command("delete", "Delete a food entry (the record keeps what it was).");
        command.Arguments.Add(entryArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<FoodLogService>()
                    .DeleteAsync(parseResult.GetValue(entryArgument)!, cancellationToken);
                FoodOutput.WriteResult(result, asJson);
                return 0;
            });
        });

        return command;
    }

    private static Command CreatePhoto(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var entryArgument = new Argument<string>("entry") { Description = "The entry (its id or first 8 characters)." };
        var fileOption = new Option<string?>("--file", "-f")
        {
            Description = "A JPEG, PNG or WebP of at most 5 MB. Replaces the photo the entry has."
        };
        var removeOption = new Option<bool>("--remove") { Description = "Remove the entry's photo." };
        var command = new Command("photo", "Set or remove a food entry's photo.");
        command.Arguments.Add(entryArgument);
        command.Options.Add(fileOption);
        command.Options.Add(removeOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var entry = parseResult.GetValue(entryArgument)!;
            var path = parseResult.GetValue(fileOption);
            var remove = parseResult.GetValue(removeOption);
            return Cli.RunAsync(asJson, async () =>
            {
                if (remove == (path is not null))
                {
                    Cli.WriteError(asJson, "Give --file <path> to set the photo, or --remove to remove it.");
                    return 1;
                }

                if (path is not null && !File.Exists(path))
                {
                    Cli.WriteError(asJson, $"File not found: {path}");
                    return 1;
                }

                await using var provider = Cli.BuildServices(connectionString);
                var photos = provider.GetRequiredService<FoodPhotoService>();
                var result = path is null
                    ? await photos.RemoveAsync(entry, cancellationToken)
                    : await photos.AttachAsync(
                        entry, await File.ReadAllBytesAsync(path, cancellationToken), Path.GetFileName(path), cancellationToken);
                FoodOutput.WriteResult(result, asJson);
                return 0;
            });
        });

        return command;
    }

    // ---------------------------------------------------------------------------------
    // fast
    // ---------------------------------------------------------------------------------

    private static Command CreateFast(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("fast", "Begin, end, record, edit or delete a fast.");

        var beginAt = new Option<string?>("--at") { Description = "When it began (home-zone time). Defaults to now." };
        var begin = new Command("begin", "Begin a fast. One runs at a time.");
        begin.Options.Add(beginAt);
        begin.SetAction((parseResult, cancellationToken) => RunFast(
            parseResult, connectionOption, jsonOption,
            async food => await food.BeginFastAsync(
                await ParseAtAsync(parseResult.GetValue(beginAt), food, cancellationToken), cancellationToken)));

        var endAt = new Option<string?>("--at") { Description = "When it ended (home-zone time). Defaults to now." };
        var end = new Command("end", "End the running fast.");
        end.Options.Add(endAt);
        end.SetAction((parseResult, cancellationToken) => RunFast(
            parseResult, connectionOption, jsonOption,
            async food => await food.EndFastAsync(
                await ParseAtAsync(parseResult.GetValue(endAt), food, cancellationToken), cancellationToken)));

        var recordFrom = new Option<string?>("--from") { Description = "When it began (home-zone time)." };
        var recordTo = new Option<string?>("--to") { Description = "When it ended (home-zone time)." };
        var record = new Command("record", "Record a past fast.");
        record.Options.Add(recordFrom);
        record.Options.Add(recordTo);
        record.SetAction((parseResult, cancellationToken) => RunFast(
            parseResult, connectionOption, jsonOption,
            async food =>
            {
                var from = await ParseAtAsync(parseResult.GetValue(recordFrom), food, cancellationToken)
                           ?? throw new ArgumentException("Say when the fast began: --from \"yesterday 20:00\".");
                var to = await ParseAtAsync(parseResult.GetValue(recordTo), food, cancellationToken)
                         ?? throw new ArgumentException("Say when the fast ended: --to 12:30.");
                return await food.RecordFastAsync(from, to, cancellationToken);
            }));

        var editFast = new Argument<string>("fast") { Description = "The fast (its id or first 8 characters)." };
        var editFrom = new Option<string?>("--from") { Description = "A new start (home-zone time)." };
        var editTo = new Option<string?>("--to") { Description = "A new end (home-zone time). Left out, a running fast keeps running." };
        var edit = new Command("edit", "Change a fast's times. What you leave out stays as it was.");
        edit.Arguments.Add(editFast);
        edit.Options.Add(editFrom);
        edit.Options.Add(editTo);
        edit.SetAction((parseResult, cancellationToken) => RunFast(
            parseResult, connectionOption, jsonOption,
            async food =>
            {
                var fast = await food.ResolveFastAsync(parseResult.GetValue(editFast)!, cancellationToken);
                var from = await ParseAtAsync(parseResult.GetValue(editFrom), food, cancellationToken) ?? fast.StartedAt;
                var to = await ParseAtAsync(parseResult.GetValue(editTo), food, cancellationToken) ?? fast.EndedAt;
                return await food.EditFastAsync(fast.Id.ToString(), from, to, cancellationToken);
            }));

        var deleteFast = new Argument<string>("fast") { Description = "The fast (its id or first 8 characters)." };
        var delete = new Command("delete", "Delete a fast (the record keeps what it was).");
        delete.Arguments.Add(deleteFast);
        delete.SetAction((parseResult, cancellationToken) => RunFast(
            parseResult, connectionOption, jsonOption,
            food => food.DeleteFastAsync(parseResult.GetValue(deleteFast)!, cancellationToken)));

        command.Subcommands.Add(begin);
        command.Subcommands.Add(end);
        command.Subcommands.Add(record);
        command.Subcommands.Add(edit);
        command.Subcommands.Add(delete);
        return command;
    }

    private static Task<int> RunFast(
        ParseResult parseResult, Option<string?> connectionOption, Option<bool> jsonOption,
        Func<FoodLogService, Task<FoodWriteResult>> write)
    {
        var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
        var asJson = parseResult.GetValue(jsonOption);
        return Cli.RunAsync(asJson, async () =>
        {
            await using var provider = Cli.BuildServices(connectionString);
            var result = await write(provider.GetRequiredService<FoodLogService>());
            FoodOutput.WriteResult(result, asJson);
            return 0;
        });
    }

    // ---------------------------------------------------------------------------------
    // close
    // ---------------------------------------------------------------------------------

    private static Command CreateClose(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var dateArgument = new Argument<string?>("date")
        {
            Description = "The day to close (YYYY-MM-DD). Defaults to today.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var command = new Command("close", "Close a food day: you've logged all of it, so its food pays.");
        command.Arguments.Add(dateArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var food = provider.GetRequiredService<FoodLogService>();
                var day = await ParseDayAsync(parseResult.GetValue(dateArgument), food, cancellationToken);
                var result = await food.CloseDayAsync(day, cancellationToken);
                FoodOutput.WriteResult(result, asJson);
                return 0;
            });
        });

        return command;
    }

    // ---------------------------------------------------------------------------------
    // drinks
    // ---------------------------------------------------------------------------------

    private const int DefaultDrinkDays = 30;

    private static Command CreateDrinks(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var daysOption = new Option<int?>("--days") { Description = $"The last N days, today among them ({DefaultDrinkDays} by default)." };
        var fromOption = new Option<string?>("--from") { Description = "From this day (YYYY-MM-DD) or time (as --at reads it)." };
        var toOption = new Option<string?>("--to") { Description = "To this day (included) or time (excluded). Defaults to the end of today." };
        var command = new Command("drinks", "Count the alcoholic drinks over a range of days or times, with the sobriety streak.");
        command.Options.Add(daysOption);
        command.Options.Add(fromOption);
        command.Options.Add(toOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var days = parseResult.GetValue(daysOption);
            var fromText = parseResult.GetValue(fromOption);
            var toText = parseResult.GetValue(toOption);
            return Cli.RunAsync(asJson, async () =>
            {
                if (days is not null && (fromText is not null || toText is not null))
                {
                    throw new ArgumentException("Use --days or --from/--to, not both.");
                }

                if (days is < 1)
                {
                    throw new ArgumentException("--days is 1 or more.");
                }

                await using var provider = Cli.BuildServices(connectionString);
                var food = provider.GetRequiredService<FoodLogService>();
                var today = await food.TodayAsync(cancellationToken);
                var from = await RangeEndAsync(fromText, food, end: false, cancellationToken)
                           ?? await food.InstantAtAsync(today.AddDays(-((days ?? DefaultDrinkDays) - 1)).ToDateTime(TimeOnly.MinValue), cancellationToken);
                var to = await RangeEndAsync(toText, food, end: true, cancellationToken)
                         ?? await food.InstantAtAsync(today.AddDays(1).ToDateTime(TimeOnly.MinValue), cancellationToken);
                var tally = await food.GetDrinksAsync(from, to, cancellationToken);
                var streak = (await food.GetDayAsync(today, cancellationToken)).Sobriety;
                if (asJson)
                {
                    Cli.WriteJson(FoodOutput.DrinksJson(tally, streak));
                }
                else
                {
                    FoodOutput.WriteDrinks(tally, streak, await food.LocalAtAsync(from, cancellationToken), await food.LocalAtAsync(to, cancellationToken));
                }

                return 0;
            });
        });

        return command;
    }

    /// <summary>
    /// A range end: a day (its start, or for <paramref name="end"/> the start of the day after),
    /// a time as <c>--at</c> reads it, or null when none is given.
    /// </summary>
    private static async Task<DateTimeOffset?> RangeEndAsync(
        string? text, FoodLogService food, bool end, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().ToLowerInvariant();
        if (value is "today" or "yesterday"
            || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            var day = await ParseDayAsync(value, food, cancellationToken) ?? await food.TodayAsync(cancellationToken);
            return await food.InstantAtAsync((end ? day.AddDays(1) : day).ToDateTime(TimeOnly.MinValue), cancellationToken);
        }

        return await ParseAtAsync(text, food, cancellationToken);
    }

    // ---------------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------------

    private static readonly string[] ClockFormats = ["H:mm", "HH:mm", "h:mmtt", "h:mm tt", "htt", "h tt"];
    private static readonly string[] LocalFormats = ["yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

    /// <summary>A day: <c>YYYY-MM-DD</c>, <c>today</c> or <c>yesterday</c>; null for today.</summary>
    internal static async Task<DateOnly?> ParseDayAsync(string? text, FoodLogService food, CancellationToken cancellationToken)
    {
        var value = text?.Trim().ToLowerInvariant();
        switch (value)
        {
            case null or "" or "today":
                return null;
            case "yesterday":
                return (await food.TodayAsync(cancellationToken)).AddDays(-1);
        }

        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new ArgumentException($"'{text}' is not a day (expected YYYY-MM-DD, today or yesterday).");
    }

    /// <summary>
    /// A time (food-log plan §3.5): a clock time today (yesterday when today's is still ahead),
    /// <c>yesterday 20:00</c>, a home-zone date and time, or an instant with an offset; null when
    /// none is given.
    /// </summary>
    internal static async Task<DateTimeOffset?> ParseAtAsync(string? text, FoodLogService food, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (value.EndsWith('Z') || HasOffset(value))
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
                ? instant
                : throw BadTime(text);
        }

        if (DateTime.TryParseExact(value, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return await food.InstantAtAsync(local, cancellationToken);
        }

        var yesterday = value.StartsWith("yesterday ", StringComparison.OrdinalIgnoreCase);
        var clockText = yesterday ? value["yesterday ".Length..].Trim() : value;
        if (!TimeOnly.TryParseExact(clockText.ToUpperInvariant(), ClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
        {
            throw BadTime(text);
        }

        var today = await food.TodayAsync(cancellationToken);
        var at = await food.InstantAtAsync((yesterday ? today.AddDays(-1) : today).ToDateTime(clock), cancellationToken);
        if (!yesterday && at > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            at = await food.InstantAtAsync(today.AddDays(-1).ToDateTime(clock), cancellationToken);
        }

        return at;
    }

    private static bool HasOffset(string value)
        => value.Length > 6 && value[^6] is '+' or '-' && value[^3] == ':' && value.Contains('T');

    private static ArgumentException BadTime(string text)
        => new($"'{text}' is not a time: use 12:30, 7:30pm, \"yesterday 20:00\", \"2026-10-19 19:00\" or an ISO time with an offset.");
}

/// <summary>How <c>bsk food</c> prints and serializes the Food Log.</summary>
internal static class FoodOutput
{
    public static void WriteResult(FoodWriteResult result, bool asJson)
    {
        if (asJson)
        {
            Cli.WriteJson(new
            {
                action = result.Action,
                meal = result.Meal is { } m ? MealJson(m) : null,
                fast = result.Fast is { } f ? FastJson(f) : null,
                day = result.Day?.ToString("yyyy-MM-dd"),
                days = result.Days.Select(d => d.ToString("yyyy-MM-dd")),
                awards = RewardOutput.AwardsJson(result.Earned.Awards),
                progress = RewardOutput.ProgressJson(result.Earned.Progress)
            });
            return;
        }

        switch (result.Action)
        {
            case FoodActions.Logged:
                Console.WriteLine($"Logged {Describe(result.Meal!)}. [{FoodLogService.Short(result.Meal!.Id)}]");
                if (result.Fast is { } ended)
                {
                    Console.WriteLine($"Ended your fast: {FoodText.Duration(ended.Seconds ?? 0)}, {FoodText.Span(ended)}.");
                }

                break;
            case FoodActions.Edited:
                Console.WriteLine($"Updated: {Describe(result.Meal!)}. [{FoodLogService.Short(result.Meal!.Id)}]");
                break;
            case FoodActions.Deleted:
                Console.WriteLine($"Deleted {Describe(result.Meal!)}.");
                break;
            case FoodActions.PhotoSet:
                Console.WriteLine($"Set the photo of {result.Meal!.Name}. [{FoodLogService.Short(result.Meal.Id)}] (photo {result.Meal.PhotoArtifactId})");
                break;
            case FoodActions.PhotoRemoved:
                Console.WriteLine($"Removed the photo of {result.Meal!.Name}. [{FoodLogService.Short(result.Meal.Id)}]");
                break;
            case FoodActions.FastBegun:
                Console.WriteLine($"Began a fast at {FoodText.When(result.Fast!.StartedLocal)}. [{FoodLogService.Short(result.Fast.Id)}]");
                break;
            case FoodActions.FastEnded:
                Console.WriteLine($"Ended your fast: {FoodText.Duration(result.Fast!.Seconds ?? 0)}, {FoodText.Span(result.Fast)}.");
                break;
            case FoodActions.FastRecorded:
                Console.WriteLine(
                    $"Recorded a {FoodText.Duration(result.Fast!.Seconds ?? 0)} fast, {FoodText.Span(result.Fast)}. [{FoodLogService.Short(result.Fast.Id)}]");
                break;
            case FoodActions.FastEdited:
                Console.WriteLine($"Updated the fast: {FoodText.Span(result.Fast!)}{Length(result.Fast!)}.");
                break;
            case FoodActions.FastDeleted:
                Console.WriteLine($"Deleted the fast {FoodText.Span(result.Fast!)}.");
                break;
            case FoodActions.Closed:
                Console.WriteLine($"Closed {result.Day:yyyy-MM-dd}: its food pays.");
                break;
            case FoodActions.AlreadyClosed:
                Console.WriteLine($"{result.Day:yyyy-MM-dd} is already closed.");
                break;
        }

        WriteEarned(result.Earned);
    }

    /// <summary>
    /// What a food write earned. A re-price (a reversal and an earn of the same key) reads as one
    /// line, "Re-scored Food day Tue Oct 20: 32 -> 29 Health XP"; everything else as
    /// <c>bsk adhere</c> prints it.
    /// </summary>
    public static void WriteEarned(Earned earned)
    {
        var repriced = earned.Awards
            .GroupBy(a => a.AwardKey)
            .Where(g => g.Any(a => a.Entry == LedgerEntries.Reversal) && g.Any(a => a.Entry == LedgerEntries.Earn))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in repriced)
        {
            var was = -earned.Awards.Where(a => a.AwardKey == key && a.Entry == LedgerEntries.Reversal).Sum(a => a.Amounts.Values.Sum());
            var earn = earned.Awards.Last(a => a.AwardKey == key && a.Entry == LedgerEntries.Earn);
            Console.WriteLine($"Re-scored {earn.Note}: {was} -> {earn.Amounts.Values.Sum()} Health XP.");
        }

        RewardOutput.WriteAwards(earned.Awards.Where(a => !repriced.Contains(a.AwardKey)));
        RewardOutput.WriteProgress(earned.Progress);
    }

    public static void WriteDay(FoodDayView view, DietRules rules)
    {
        var state = view.Facts.ClosedByHand
            ? "closed"
            : view.Day < view.Today ? "closed at midnight" : view.Day == view.Today ? "open" : "ahead";
        Console.WriteLine($"{view.Day.ToString("ddd MMM d, yyyy", CultureInfo.InvariantCulture)} ({state})");

        if (view.Meals.Count == 0)
        {
            Console.WriteLine("  No food logged.");
        }

        foreach (var meal in view.Meals)
        {
            var flag = (meal.Alcoholic ? "  (drink)" : "") + (view.EatenDuringFast.ContainsKey(meal.Id) ? "  (inside a fast)" : "");
            Console.WriteLine(
                $"  {FoodLogService.Short(meal.Id)}  {FoodText.Clock(meal.EatenLocal),8}  {meal.MealType,-9} {meal.Name}  " +
                $"{meal.Calories:N0} cal, score {FoodEntries.ScoreText(meal.Score)}{Nutrients(meal)}{flag}");
        }

        foreach (var fast in view.Fasts)
        {
            Console.WriteLine($"  Fast {FoodLogService.Short(fast.Id)}: {FoodText.Duration(fast.Seconds ?? 0)}, {FoodText.Span(fast)}.");
        }

        if (view.Running is { } running)
        {
            var elapsed = (long)(DateTimeOffset.UtcNow - running.StartedAt).TotalSeconds;
            Console.WriteLine(
                $"  Fasting now {FoodLogService.Short(running.Id)}: {FoodText.Duration(elapsed)}, since {FoodText.When(running.StartedLocal)}.");
        }

        var score = view.Score;
        var average = score.AverageScore is { } s ? $", average score {s.ToString("0.0", CultureInfo.InvariantCulture)}" : "";
        Console.WriteLine($"  {view.Facts.Calories:N0} cal over {view.Facts.Meals} {(view.Facts.Meals == 1 ? "entry" : "entries")}{average}.");
        Console.WriteLine(
            $"  Quality {Part(score.Quality)} / {rules.QualityMax}; calories {Part(score.CaloriesPart)} / {rules.CaloriesMax}; " +
            $"fasting {Part(score.Fasting)} / {rules.FastingMax}.");
        if (score.Drinks > 0)
        {
            Console.WriteLine($"  {Drinks(score.Drinks)}{(!score.Cut ? "" : rules.DrinksFree > 0 ? $", {score.Drinks - rules.DrinksFree} over {rules.DrinksFree}: {CutText(score.DrinkFactor)} off the day's diet XP" : $": {CutText(score.DrinkFactor)} off the day's diet XP")}.");
        }

        Console.WriteLine(
            $"  Diet: {score.Total} of {rules.Cap} base Health XP (food {score.FoodAward}, fast {score.FastAward})" +
            (view.Facts.Closed ? "." : ", if the day closed now."));
        if (view.Paid.Fast is not null || view.Paid.Diet is not null)
        {
            Console.WriteLine($"  Paid: {Paid(view.Paid)}.");
        }

        if (view.Sobriety is { } streak)
        {
            Console.WriteLine($"  {Streak(streak)}.");
        }
    }

    /// <summary>"Sober 12 days (best 30)", or "A drink that day: sober 0 days (best 30)".</summary>
    private static string Streak(SobrietyStreak streak)
        => (streak.DrinkThatDay ? "A drink that day: sober 0 days" : $"Sober {streak.Days} day{(streak.Days == 1 ? "" : "s")}")
           + $" (best {streak.BestDays})";

    private static string Drinks(int drinks) => $"{drinks} drink{(drinks == 1 ? "" : "s")}";

    private static string CutText(decimal factor) => $"{(1m - factor) * 100m:0}%";

    public static void WriteDrinks(DrinkTally tally, SobrietyStreak? streak, DateTime fromLocal, DateTime toLocal)
    {
        var most = tally.MostOn is { } on
            ? $"; the most in a day was {tally.MostInADay}, on {on.ToString("ddd MMM d", CultureInfo.InvariantCulture)}"
            : "";
        Console.WriteLine(
            $"{Drinks(tally.Drinks)} from {FoodText.When(fromLocal)} to {FoodText.When(toLocal)}, " +
            $"on {tally.DrinkDays} day{(tally.DrinkDays == 1 ? "" : "s")}{most}.");
        if (streak is not null)
        {
            Console.WriteLine($"Today: {Streak(streak)}.");
        }
    }

    public static object DrinksJson(DrinkTally tally, SobrietyStreak? streak) => new
    {
        from = tally.From,
        to = tally.To,
        drinks = tally.Drinks,
        drinkDays = tally.DrinkDays,
        mostInADay = tally.MostInADay,
        mostOn = tally.MostOn?.ToString("yyyy-MM-dd"),
        sobriety = SobrietyJson(streak)
    };

    private static object? SobrietyJson(SobrietyStreak? streak) => streak is null
        ? null
        : new
        {
            asOf = streak.AsOf.ToString("yyyy-MM-dd"),
            days = streak.Days,
            bestDays = streak.BestDays,
            since = streak.Since.ToString("yyyy-MM-dd"),
            lastDrinkOn = streak.LastDrinkOn?.ToString("yyyy-MM-dd"),
            firstDay = streak.FirstDay.ToString("yyyy-MM-dd")
        };

    /// <summary>What the ledger pays a day: "39 Health XP (food 32 x 1.2 = 38, fast 7)".</summary>
    private static string Paid(FoodDayPaid paid)
    {
        static string? One(string what, FoodPaid? part)
            => part is null ? null
                : part.Multiplier is { } m
                    ? $"{what} {part.Base} x {m.ToString("0.0#", CultureInfo.InvariantCulture)} = {part.Paid}"
                    : $"{what} {part.Paid}";

        var parts = new[] { One("food", paid.Diet), One("fast", paid.Fast) }.OfType<string>();
        return $"{paid.Total} Health XP ({string.Join(", ", parts)})";
    }

    public static void WriteRange(FoodRange range, DietRules rules)
    {
        foreach (var day in range.Days)
        {
            var facts = day.Facts;
            var average = day.Score.AverageScore is { } s ? s.ToString("0.0", CultureInfo.InvariantCulture) : "-";
            var fast = facts.LongestFastSeconds is { } seconds ? FoodText.Duration(seconds) : "-";
            var state = facts.ClosedByHand ? "closed" : facts.Day < range.Today ? "" : facts.Day == range.Today ? "today" : "ahead";
            var paid = day.Paid.Total > 0 ? $"paid {day.Paid.Total,2}" : "       ";
            var drinks = facts.Drinks > 0 ? $"drinks {facts.Drinks,2}" : "         ";
            Console.WriteLine(
                $"{facts.Day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture)}  {facts.Calories,6:N0} cal  avg {average,3}  " +
                $"fast {fast,-9}  {drinks}  {day.Score.Total,2}/{rules.Cap}  {paid}  {state}".TrimEnd());
        }

        var past = range.Days.Where(d => d.Facts.Day <= range.Today).ToList();
        var logged = past.Where(d => d.Facts.Meals > 0).ToList();
        var total = past.Sum(d => d.Score.Total);
        Console.WriteLine(
            $"Total {total} base Health XP over {past.Count} days" +
            (past.Count > 0 ? $" ({((decimal)total / past.Count).ToString("0.0", CultureInfo.InvariantCulture)} a day)" : "") +
            $"; {range.Days.Sum(d => d.Paid.Total)} Health XP paid" +
            (logged.Count > 0 ? $"; {logged.Average(d => d.Facts.Calories):N0} cal a logged day" : "") + ".");
        if (range.Sobriety is { } streak)
        {
            var counted = past.Where(d => d.Facts.Day >= streak.FirstDay).ToList();
            Console.WriteLine(
                $"{Drinks(counted.Sum(d => d.Facts.Drinks))}; drink-free {counted.Count(d => d.Facts.Drinks == 0)} of {counted.Count} days; " +
                $"{Streak(streak)} as of {streak.AsOf.ToString("ddd MMM d", CultureInfo.InvariantCulture)}.");
        }
    }

    public static object DayJson(FoodDayView view) => new
    {
        day = view.Day.ToString("yyyy-MM-dd"),
        today = view.Today.ToString("yyyy-MM-dd"),
        meals = view.Meals.Select(m => MealJson(m, view.EatenDuringFast.GetValueOrDefault(m.Id))),
        fasts = view.Fasts.Select(FastJson),
        running = view.Running is { } r ? FastJson(r) : null,
        lastMeal = view.LastMeal is { } l ? MealJson(l) : null,
        summary = SummaryJson(view.Facts, view.Score, view.Paid),
        sobriety = SobrietyJson(view.Sobriety)
    };

    public static object RangeJson(FoodRange range) => new
    {
        from = range.From.ToString("yyyy-MM-dd"),
        to = range.To.ToString("yyyy-MM-dd"),
        today = range.Today.ToString("yyyy-MM-dd"),
        days = range.Days.Select(d => SummaryJson(d.Facts, d.Score, d.Paid)),
        sobriety = SobrietyJson(range.Sobriety)
    };

    public static object MealJson(Meal meal) => MealJson(meal, Guid.Empty);

    private static object MealJson(Meal meal, Guid duringFast) => new
    {
        id = meal.Id,
        name = meal.Name,
        score = meal.Score,
        calories = meal.Calories,
        alcoholic = meal.Alcoholic,
        proteinG = meal.ProteinG,
        carbsG = meal.CarbsG,
        fatG = meal.FatG,
        mealType = meal.MealType,
        eatenAt = meal.EatenAt,
        eatenOn = meal.EatenOn.ToString("yyyy-MM-dd"),
        photo = meal.PhotoArtifactId,
        duringFast = duringFast == Guid.Empty ? (Guid?)null : duringFast,
        loggedAt = meal.LoggedAt,
        versions = meal.Versions
    };

    public static object FastJson(Fast fast) => new
    {
        id = fast.Id,
        startedAt = fast.StartedAt,
        endedAt = fast.EndedAt,
        startedOn = fast.StartedOn.ToString("yyyy-MM-dd"),
        endedOn = fast.EndedOn?.ToString("yyyy-MM-dd"),
        running = fast.Running,
        seconds = fast.Seconds,
        mealsDuring = fast.MealsDuring
    };

    private static object SummaryJson(FoodDay facts, DietScore score, FoodDayPaid paid) => new
    {
        day = facts.Day.ToString("yyyy-MM-dd"),
        meals = facts.Meals,
        calories = facts.Calories,
        averageScore = score.AverageScore is { } s ? Math.Round(s, 2) : (decimal?)null,
        longestFastSeconds = facts.LongestFastSeconds,
        closed = facts.Closed,
        closedByHand = facts.ClosedByHand,
        closedAt = facts.ClosedAt,
        quality = Math.Round(score.Quality, 2),
        caloriesPart = Math.Round(score.CaloriesPart, 2),
        fasting = Math.Round(score.Fasting, 2),
        foodAward = score.FoodAward,
        fastAward = score.FastAward,
        total = score.Total,
        drinks = score.Drinks,
        drinkFactor = score.DrinkFactor,
        paid = new
        {
            fast = paid.Fast is { } f ? new { @base = f.Base, paid = f.Paid, multiplier = f.Multiplier } : null,
            diet = paid.Diet is { } d ? new { @base = d.Base, paid = d.Paid, multiplier = d.Multiplier } : null,
            total = paid.Total
        }
    };

    private static string Describe(Meal meal)
        => $"{meal.Name} ({(meal.Alcoholic ? "alcoholic " : "")}{meal.MealType}, {meal.Calories:N0} cal, score {FoodEntries.ScoreText(meal.Score)}{Nutrients(meal)}) at {FoodText.When(meal.EatenLocal)}";

    private static string Nutrients(Meal meal)
    {
        var parts = new List<string>();
        if (meal.ProteinG is { } p)
        {
            parts.Add($"{p} g protein");
        }

        if (meal.CarbsG is { } c)
        {
            parts.Add($"{c} g carbs");
        }

        if (meal.FatG is { } f)
        {
            parts.Add($"{f} g fat");
        }

        return parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
    }

    private static string Length(Fast fast) => fast.Seconds is { } s ? $" ({FoodText.Duration(s)})" : "";

    private static string Part(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}

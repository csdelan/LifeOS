using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Home;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Photos of each space, from a baseline (HOME-4, environment plan §8.3), under <c>bsk space</c>:
/// <list type="bullet">
/// <item><c>bsk space photo add &lt;space&gt; --file &lt;path&gt; [--day …] [--shot …]</c> adds one;</item>
/// <item><c>bsk space photo list &lt;space&gt; [--shot …]</c> lists them, newest first;</item>
/// <item><c>bsk space photo move &lt;photo&gt; [--day …] [--shot …]</c> moves one to another day or shot;</item>
/// <item><c>bsk space photo remove &lt;photo&gt;</c> removes one (its bytes stay in the record);</item>
/// <item><c>bsk space baseline &lt;photo&gt;</c> makes one its shot's baseline;</item>
/// <item><c>bsk space shot add|rename|remove</c> manages the spots a space is photographed from.</item>
/// </list>
/// <c>&lt;photo&gt;</c> is a photo's id or its first 8 characters; <c>--day</c> takes
/// <c>yyyy-MM-dd</c>, <c>today</c> or <c>yesterday</c>. Uploads read the same <c>Storage__*</c>
/// environment variables as <c>bsk food photo</c>.
/// </summary>
internal static class SpacePhotoCommand
{
    public static Command CreatePhoto(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("photo", "Photos of a space, to compare it over time: add, list, move, remove.");
        command.Subcommands.Add(CreateAdd(connectionOption, jsonOption));
        command.Subcommands.Add(CreateList(connectionOption, jsonOption));
        command.Subcommands.Add(CreateMove(connectionOption, jsonOption));
        command.Subcommands.Add(CreateRemove(connectionOption, jsonOption));
        return command;
    }

    public static Command CreateBaseline(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var photoArgument = PhotoArgument();
        var command = new Command("baseline", "Make a photo its shot's baseline: the Then its newest photo is compared against.");
        command.Arguments.Add(photoArgument);
        command.SetAction((parseResult, cancellationToken) =>
            RunAsync(parseResult, connectionOption, jsonOption, async (provider, asJson) =>
            {
                var photo = await provider.GetRequiredService<SpacePhotoService>()
                    .SetBaselineAsync(parseResult.GetValue(photoArgument)!, cancellationToken);
                return await WritePhotoAsync(provider, photo, asJson, "Made the baseline", cancellationToken);
            }));
        return command;
    }

    public static Command CreateShot(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("shot", "The spots a space is photographed from: add, rename, remove.");

        var addSpace = SpaceArgument();
        var addName = new Argument<string>("name") { Description = "The shot's name, e.g. Desk." };
        var add = new Command("add", "Add a shot to a space: a spot it's photographed from.");
        add.Arguments.Add(addSpace);
        add.Arguments.Add(addName);
        add.SetAction((parseResult, cancellationToken) =>
            RunAsync(parseResult, connectionOption, jsonOption, async (provider, asJson) =>
            {
                var space = await provider.GetRequiredService<SpaceService>()
                    .AddShotAsync(parseResult.GetValue(addSpace)!, parseResult.GetValue(addName)!, cancellationToken);
                return WriteShots(space, asJson, $"Added a shot to {space.Name}");
            }));

        var renameSpace = SpaceArgument();
        var renameShot = ShotArgument();
        var renameName = new Argument<string>("name") { Description = "The shot's new name." };
        var rename = new Command("rename", "Rename one of a space's shots.");
        rename.Arguments.Add(renameSpace);
        rename.Arguments.Add(renameShot);
        rename.Arguments.Add(renameName);
        rename.SetAction((parseResult, cancellationToken) =>
            RunAsync(parseResult, connectionOption, jsonOption, async (provider, asJson) =>
            {
                var space = await provider.GetRequiredService<SpaceService>().RenameShotAsync(
                    parseResult.GetValue(renameSpace)!, parseResult.GetValue(renameShot)!, parseResult.GetValue(renameName)!,
                    cancellationToken);
                return WriteShots(space, asJson, $"Renamed a shot of {space.Name}");
            }));

        var removeSpace = SpaceArgument();
        var removeShot = ShotArgument();
        var remove = new Command("remove", "Remove one of a space's shots. Its photos stay in the record; a space keeps at least one shot.");
        remove.Arguments.Add(removeSpace);
        remove.Arguments.Add(removeShot);
        remove.SetAction((parseResult, cancellationToken) =>
            RunAsync(parseResult, connectionOption, jsonOption, async (provider, asJson) =>
            {
                var space = await provider.GetRequiredService<SpaceService>().RemoveShotAsync(
                    parseResult.GetValue(removeSpace)!, parseResult.GetValue(removeShot)!, cancellationToken);
                return WriteShots(space, asJson, $"Removed a shot of {space.Name}");
            }));

        command.Subcommands.Add(add);
        command.Subcommands.Add(rename);
        command.Subcommands.Add(remove);
        return command;
    }

    private static Command CreateAdd(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var spaceArgument = SpaceArgument();
        var fileOption = new Option<string>("--file", "-f")
        {
            Description = "A JPEG, PNG or WebP of at most 5 MB.",
            Required = true
        };
        var dayOption = DayOption("The day it shows: yyyy-MM-dd, today or yesterday (today by default).");
        var shotOption = ShotOption("The shot it was taken from, by id or name (the space's first by default).");

        var command = new Command("add", "Add a photo of a space. The same photo already on that shot isn't added twice.");
        command.Arguments.Add(spaceArgument);
        command.Options.Add(fileOption);
        command.Options.Add(dayOption);
        command.Options.Add(shotOption);
        command.SetAction((parseResult, cancellationToken) =>
            RunAsync(parseResult, connectionOption, jsonOption, async (provider, asJson) =>
            {
                var path = parseResult.GetValue(fileOption)!;
                if (!File.Exists(path))
                {
                    Cli.WriteError(asJson, $"File not found: {path}");
                    return 1;
                }

                var day = await DayAsync(provider, parseResult.GetValue(dayOption), cancellationToken);
                var photo = await provider.GetRequiredService<SpacePhotoService>().AddAsync(
                    parseResult.GetValue(spaceArgument)!, await File.ReadAllBytesAsync(path, cancellationToken), day,
                    parseResult.GetValue(shotOption), Path.GetFileName(path), cancellationToken);
                return await WritePhotoAsync(provider, photo, asJson, "Added a photo", cancellationToken);
            }));
        return command;
    }

    private static Command CreateList(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var spaceArgument = SpaceArgument();
        var shotOption = ShotOption("Only this shot's photos, by id or name.");
        var command = new Command("list", "A space's photos, newest first, with each shot's baseline marked.");
        command.Arguments.Add(spaceArgument);
        command.Options.Add(shotOption);
        command.SetAction((parseResult, cancellationToken) =>
            RunAsync(parseResult, connectionOption, jsonOption, async (provider, asJson) =>
            {
                var list = await provider.GetRequiredService<SpacePhotoService>().ListAsync(
                    parseResult.GetValue(spaceArgument)!, parseResult.GetValue(shotOption), cancellationToken);
                var baselines = Baselines(list.Space, list.Photos);
                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        space = list.Space.Name,
                        spaceId = list.Space.Id,
                        shots = ShotsJson(list.Space),
                        photos = list.Photos.Select(p => Json(p, list.Space, baselines.Contains(p.Id)))
                    });
                    return 0;
                }

                if (list.Photos.Count == 0)
                {
                    Console.WriteLine(
                        $"No photos of {list.Space.Name} yet. Add one: bsk space photo add \"{list.Space.Name}\" --file <path>");
                    return 0;
                }

                foreach (var photo in list.Photos)
                {
                    Console.WriteLine(Line(photo, list.Space, baselines.Contains(photo.Id)));
                }

                return 0;
            }));
        return command;
    }

    private static Command CreateMove(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var photoArgument = PhotoArgument();
        var dayOption = DayOption("The day it shows: yyyy-MM-dd, today or yesterday.");
        var shotOption = ShotOption("The shot it was taken from, by id or name.");
        var command = new Command("move", "Move a photo to another day or shot.");
        command.Arguments.Add(photoArgument);
        command.Options.Add(dayOption);
        command.Options.Add(shotOption);
        command.SetAction((parseResult, cancellationToken) =>
            RunAsync(parseResult, connectionOption, jsonOption, async (provider, asJson) =>
            {
                var day = await DayAsync(provider, parseResult.GetValue(dayOption), cancellationToken);
                var photo = await provider.GetRequiredService<SpacePhotoService>().ChangeAsync(
                    parseResult.GetValue(photoArgument)!, day, parseResult.GetValue(shotOption), cancellationToken);
                return await WritePhotoAsync(provider, photo, asJson, "Moved the photo", cancellationToken);
            }));
        return command;
    }

    private static Command CreateRemove(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var photoArgument = PhotoArgument();
        var command = new Command("remove", "Remove a photo. Its bytes stay in the record. If it was the baseline, the shot's first photo left takes over.");
        command.Arguments.Add(photoArgument);
        command.SetAction((parseResult, cancellationToken) =>
            RunAsync(parseResult, connectionOption, jsonOption, async (provider, asJson) =>
            {
                var photo = await provider.GetRequiredService<SpacePhotoService>()
                    .RemoveAsync(parseResult.GetValue(photoArgument)!, cancellationToken);
                if (asJson)
                {
                    Cli.WriteJson(new { removed = photo.Id, spaceId = photo.SpaceId });
                    return 0;
                }

                Console.WriteLine($"Removed the photo [{Short(photo.Id)}].");
                return 0;
            }));
        return command;
    }

    private static Task<int> RunAsync(
        ParseResult parseResult, Option<string?> connectionOption, Option<bool> jsonOption,
        Func<IServiceProvider, bool, Task<int>> action)
    {
        var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
        var asJson = parseResult.GetValue(jsonOption);
        return Cli.RunAsync(asJson, async () =>
        {
            await using var provider = Cli.BuildServices(connectionString);
            return await action(provider, asJson);
        });
    }

    private static async Task<DateOnly?> DayAsync(IServiceProvider provider, string? text, CancellationToken cancellationToken)
    {
        if (text is null)
        {
            return null;
        }

        var today = await provider.GetRequiredService<IHomeCalendar>()
            .DateAtAsync(provider.GetRequiredService<IClock>().UtcNow, cancellationToken);
        return DomainsExceptionCommand.ParseDay(text, today);
    }

    private static async Task<int> WritePhotoAsync(
        IServiceProvider provider, SpacePhoto photo, bool asJson, string done, CancellationToken cancellationToken)
    {
        var space = await provider.GetRequiredService<ISpaceRepository>().GetSpaceAsync(photo.SpaceId, cancellationToken)
                    ?? throw new InvalidOperationException("That photo's space no longer exists.");
        var photos = await provider.GetRequiredService<ISpaceRepository>().GetPhotosAsync(space.Id, cancellationToken);
        var baseline = Baselines(space, photos).Contains(photo.Id);
        if (asJson)
        {
            Cli.WriteJson(Json(photo, space, baseline));
            return 0;
        }

        Console.WriteLine($"{done}: {Line(photo, space, baseline)}");
        return 0;
    }

    private static int WriteShots(Space space, bool asJson, string done)
    {
        if (asJson)
        {
            Cli.WriteJson(new { spaceId = space.Id, space = space.Name, shots = ShotsJson(space) });
            return 0;
        }

        Console.WriteLine($"{done}. Its shots: {string.Join(", ", space.Shots.Select(space.ShotName))}.");
        return 0;
    }

    /// <summary>The ids of the photos that are their shot's baseline.</summary>
    private static HashSet<Guid> Baselines(Space space, IReadOnlyList<SpacePhoto> photos)
        => space.Shots.Select(s => ShotPhotos.Baseline(s, photos)?.Id).OfType<Guid>().ToHashSet();

    // "Office · Desk on Tue 2026-10-27 [e1e7dc2e]", or "Office on …" for the shot named after the space.
    private static string Line(SpacePhoto photo, Space space, bool baseline)
    {
        var shot = ShotName(photo, space);
        var where = string.Equals(shot, space.Name, StringComparison.OrdinalIgnoreCase) ? space.Name : $"{space.Name} · {shot}";
        return $"{where} on {photo.Day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture)} [{Short(photo.Id)}]"
               + (baseline ? " (baseline)" : "");
    }

    private static string ShotName(SpacePhoto photo, Space space)
        => space.Shots.FirstOrDefault(s => string.Equals(s.Id, photo.ShotId, StringComparison.OrdinalIgnoreCase)) is { } shot
            ? space.ShotName(shot)
            : $"a removed shot ({photo.ShotId})";

    private static object Json(SpacePhoto photo, Space space, bool baseline) => new
    {
        id = photo.Id,
        spaceId = space.Id,
        space = space.Name,
        shotId = photo.ShotId,
        shot = space.Shots.FirstOrDefault(s => string.Equals(s.Id, photo.ShotId, StringComparison.OrdinalIgnoreCase)) is { } shot
            ? space.ShotName(shot)
            : null,
        day = photo.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        artifactId = photo.ArtifactId,
        addedAt = photo.AddedAt,
        baseline
    };

    /// <summary>
    /// A space's shots: <c>pickedBaseline</c> is the photo picked with <c>bsk space baseline</c>,
    /// null while the shot's first photo is its baseline.
    /// </summary>
    internal static IEnumerable<object> ShotsJson(Space space)
        => space.Shots.Select(s => new { id = s.Id, name = space.ShotName(s), pickedBaseline = s.Baseline });

    private static string Short(Guid id) => id.ToString()[..8];

    private static Argument<string> SpaceArgument() => new("space") { Description = "The space, by name or id." };

    private static Argument<string> ShotArgument() => new("shot") { Description = "The shot, by id or name." };

    private static Argument<string> PhotoArgument() => new("photo") { Description = "The photo (its id or first 8 characters)." };

    private static Option<string?> DayOption(string description) => new("--day") { Description = description };

    private static Option<string?> ShotOption(string description) => new("--shot") { Description = description };
}

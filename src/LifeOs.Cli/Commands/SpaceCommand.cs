using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Home;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// The home's spaces (HOME-1, environment plan §4.7):
/// <list type="bullet">
/// <item><c>bsk space add "Office" [--weight 5] [--pace 5]</c> adds one (weight 1–10 and pace 1–5, 3 by default);</item>
/// <item><c>bsk space list [--archived]</c> lists them with each one's share of the Home Score;</item>
/// <item><c>bsk space set &lt;space&gt; [--name …] [--weight N] [--pace N]</c> renames, reweights or re-paces one;</item>
/// <item><c>bsk space archive &lt;space&gt;</c> and <c>bsk space restore &lt;space&gt;</c>.</item>
/// </list>
/// <c>&lt;space&gt;</c> is a name (unique among the spaces not archived), a urn, a short id or an id.
/// </summary>
internal static class SpaceCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("space", "The home's spaces, rated for the Home Score: add, list, set, archive, restore.");
        command.Subcommands.Add(CreateAdd(connectionOption, jsonOption));
        command.Subcommands.Add(CreateList(connectionOption, jsonOption));
        command.Subcommands.Add(CreateSet(connectionOption, jsonOption));
        command.Subcommands.Add(CreateArchive(connectionOption, jsonOption, archive: true));
        command.Subcommands.Add(CreateArchive(connectionOption, jsonOption, archive: false));
        return command;
    }

    private static Command CreateAdd(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var nameArgument = new Argument<string>("name") { Description = "The space's name, e.g. Office." };
        var weightOption = WeightOption();
        var paceOption = PaceOption();

        var command = new Command("add", "Add a space to rate.");
        command.Arguments.Add(nameArgument);
        command.Options.Add(weightOption);
        command.Options.Add(paceOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var space = await provider.GetRequiredService<SpaceService>().AddAsync(
                    parseResult.GetValue(nameArgument)!, parseResult.GetValue(weightOption), parseResult.GetValue(paceOption),
                    cancellationToken);
                return await WriteAsync(provider, space, asJson, $"Added {space.Name}", cancellationToken);
            });
        });
        return command;
    }

    private static Command CreateList(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var archivedOption = new Option<bool>("--archived") { Description = "Include archived spaces." };

        var command = new Command("list", "The spaces, with each one's weight, pace and share of the Home Score.");
        command.Options.Add(archivedOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var archived = parseResult.GetValue(archivedOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var spaces = await provider.GetRequiredService<SpaceService>().ListAsync(archived, cancellationToken);
                var total = spaces.Where(s => !s.Archived).Sum(s => s.Weight);
                if (asJson)
                {
                    Cli.WriteJson(new { spaces = spaces.Select(s => Json(s, total)) });
                    return 0;
                }

                if (spaces.Count == 0)
                {
                    Console.WriteLine("No spaces yet. Add one: bsk space add \"Office\" --weight 5 --pace 5");
                    return 0;
                }

                var width = Math.Max(5, spaces.Max(s => s.Name.Length));
                Console.WriteLine($"{"Space".PadRight(width)}  Weight  Share  Pace");
                foreach (var s in spaces)
                {
                    var share = s.Archived ? "  —  " : $"{Share(s.Weight, total),4:0.0}%";
                    Console.WriteLine(
                        $"{s.Name.PadRight(width)}  {s.Weight,6}  {share}  {s.Pace,4}{(s.Archived ? "  (archived)" : "")}");
                }

                return 0;
            });
        });
        return command;
    }

    private static Command CreateSet(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var spaceArgument = new Argument<string>("space") { Description = "The space, by name or id." };
        var nameOption = new Option<string?>("--name") { Description = "A new name." };
        var weightOption = WeightOption();
        var paceOption = PaceOption();

        var command = new Command("set", "Rename, reweight or re-pace a space. A weight or pace applies to all of history.");
        command.Arguments.Add(spaceArgument);
        command.Options.Add(nameOption);
        command.Options.Add(weightOption);
        command.Options.Add(paceOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var space = await provider.GetRequiredService<SpaceService>().SetAsync(
                    parseResult.GetValue(spaceArgument)!, parseResult.GetValue(nameOption),
                    parseResult.GetValue(weightOption), parseResult.GetValue(paceOption), cancellationToken);
                return await WriteAsync(provider, space, asJson, $"Updated {space.Name}", cancellationToken);
            });
        });
        return command;
    }

    private static Command CreateArchive(Option<string?> connectionOption, Option<bool> jsonOption, bool archive)
    {
        var spaceArgument = new Argument<string>("space") { Description = "The space, by name or id." };
        var command = archive
            ? new Command("archive", "Archive a space: it leaves the Home Score from today, and its ratings stay.")
            : new Command("restore", "Bring an archived space back: it counts again for all of history.");
        command.Arguments.Add(spaceArgument);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<SpaceService>();
                var reference = parseResult.GetValue(spaceArgument)!;
                var space = archive
                    ? await service.ArchiveAsync(reference, cancellationToken)
                    : await service.RestoreAsync(reference, cancellationToken);
                return await WriteAsync(
                    provider, space, asJson, archive ? $"Archived {space.Name}" : $"Restored {space.Name}", cancellationToken);
            });
        });
        return command;
    }

    private static async Task<int> WriteAsync(
        IServiceProvider provider, Space space, bool asJson, string done, CancellationToken cancellationToken)
    {
        var total = (await provider.GetRequiredService<SpaceService>().ListAsync(cancellationToken: cancellationToken))
            .Sum(s => s.Weight);
        if (asJson)
        {
            Cli.WriteJson(Json(space, total));
            return 0;
        }

        var share = space.Archived ? "archived" : $"{Share(space.Weight, total):0.0}% of the Home Score";
        Console.WriteLine($"{done}: weight {space.Weight}, pace {space.Pace} ({share}).");
        return 0;
    }

    internal static object Json(Space space, int totalWeight) => new
    {
        id = space.Id,
        urn = space.Urn,
        name = space.Name,
        weight = space.Weight,
        pace = space.Pace,
        share = space.Archived ? (decimal?)null : Math.Round(Share(space.Weight, totalWeight), 1),
        archivedAt = space.ArchivedAt,
        archivedOn = space.ArchivedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
    };

    private static decimal Share(int weight, int total) => total == 0 ? 0m : 100m * weight / total;

    private static Option<int?> WeightOption() => new("--weight")
    {
        Description = $"How much the space counts, {SpaceLimits.MinWeight}–{SpaceLimits.MaxWeight} ({SpaceLimits.DefaultWeight} by default)."
    };

    private static Option<int?> PaceOption() => new("--pace")
    {
        Description = $"How fast the space gets messy, {SpaceLimits.MinPace}–{SpaceLimits.MaxPace} ({SpaceLimits.DefaultPace} is normal)."
    };
}

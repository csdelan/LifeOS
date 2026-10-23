using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Covers;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk cover</c> — a cover photo from Unsplash on an identity statement, goal or project
/// (COVER-1, D15; cover-images plan §3.4). Searching and setting need
/// <c>Unsplash__AccessKey</c> in the environment; showing, moving and clearing don't.
/// <list type="bullet">
/// <item><c>bsk cover search &lt;words…&gt; [--page N]</c> — landscape photos: id, photographer, description.</item>
/// <item><c>bsk cover set &lt;subject&gt; &lt;photo-id&gt; [--position N]</c> — make a photo the cover,
/// counting a download for its photographer.</item>
/// <item><c>bsk cover move &lt;subject&gt; &lt;N&gt;</c> — centre the banner N% from the top (0–100).</item>
/// <item><c>bsk cover clear &lt;subject&gt;</c> — remove the cover.</item>
/// <item><c>bsk cover show &lt;subject&gt;</c> — the cover and its credit.</item>
/// </list>
/// </summary>
internal static class CoverCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command(
            "cover",
            "Cover photos from Unsplash: 'cover search', 'cover set', 'cover move', 'cover clear', 'cover show'.");
        command.Subcommands.Add(CreateSearch(connectionOption, jsonOption));
        command.Subcommands.Add(CreateSet(connectionOption, jsonOption));
        command.Subcommands.Add(CreateMove(connectionOption, jsonOption));
        command.Subcommands.Add(CreateClear(connectionOption, jsonOption));
        command.Subcommands.Add(CreateShow(connectionOption, jsonOption));
        return command;
    }

    private static Command CreateSearch(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var wordsArgument = new Argument<string[]>("words")
        {
            Description = "What to search for, e.g. mountain sunrise.",
            Arity = ArgumentArity.OneOrMore
        };
        var pageOption = new Option<int?>("--page")
        {
            Description = $"Which page of results (1–{CoverService.MaxPage}, default 1)."
        };
        var command = new Command("search", "Search Unsplash for landscape photos.");
        command.Arguments.Add(wordsArgument);
        command.Options.Add(pageOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var words = string.Join(' ', parseResult.GetValue(wordsArgument) ?? []);
            var page = parseResult.GetValue(pageOption) ?? 1;
            return Run(parseResult, connectionOption, jsonOption, async (covers, asJson, ct) =>
            {
                var result = await covers.SearchAsync(words, page, CoverService.DefaultPerPage, ct);
                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        query = CoverService.CheckQuery(words),
                        page,
                        result.Total,
                        result.TotalPages,
                        photos = result.Photos.Select(p => new
                        {
                            p.Id,
                            p.Photographer,
                            description = p.AltDescription ?? p.Description,
                            p.Color,
                            p.Width,
                            p.Height,
                            p.PhotoUrl
                        })
                    });
                    return;
                }

                if (result.Photos.Count == 0)
                {
                    Console.WriteLine($"No photos for '{CoverService.CheckQuery(words)}'. Try fewer or simpler words.");
                    return;
                }

                Console.WriteLine(
                    $"Page {page} of {result.TotalPages} ({result.Total.ToString("N0", CultureInfo.InvariantCulture)} photos on Unsplash):");
                foreach (var photo in result.Photos)
                {
                    var about = photo.AltDescription ?? photo.Description;
                    Console.WriteLine($"  {photo.Id,-12} {photo.Photographer}{(about is null ? "" : $" — {about}")}");
                }

                Console.WriteLine("Set one with: bsk cover set <subject> <photo-id>");
            }, cancellationToken);
        });
        return command;
    }

    private static Command CreateSet(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = SubjectArgument();
        var photoArgument = new Argument<string>("photo-id")
        {
            Description = "The Unsplash photo's id, from 'bsk cover search'."
        };
        var positionOption = new Option<int?>("--position")
        {
            Description = "Centre the banner this far from the top, 0–100 (default 50)."
        };
        var command = new Command("set", "Make an Unsplash photo the cover.");
        command.Arguments.Add(subjectArgument);
        command.Arguments.Add(photoArgument);
        command.Options.Add(positionOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var subject = parseResult.GetValue(subjectArgument)!;
            var photo = parseResult.GetValue(photoArgument)!;
            var position = parseResult.GetValue(positionOption);
            return Run(parseResult, connectionOption, jsonOption, async (covers, asJson, ct) =>
            {
                var change = await covers.SetFromUnsplashAsync(subject, photo, position, ct);
                WriteChange(change, covers.AppName, asJson, "Cover set");
            }, cancellationToken);
        });
        return command;
    }

    private static Command CreateMove(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = SubjectArgument();
        var positionArgument = new Argument<int>("position")
        {
            Description = "How far from the top to centre the banner, 0 (top) to 100 (bottom)."
        };
        var command = new Command("move", "Move where the cover's banner is centred.");
        command.Arguments.Add(subjectArgument);
        command.Arguments.Add(positionArgument);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var subject = parseResult.GetValue(subjectArgument)!;
            var position = parseResult.GetValue(positionArgument);
            return Run(parseResult, connectionOption, jsonOption, async (covers, asJson, ct) =>
            {
                var change = await covers.MoveAsync(subject, position, ct);
                WriteChange(change, covers.AppName, asJson, "Cover moved");
            }, cancellationToken);
        });
        return command;
    }

    private static Command CreateClear(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = SubjectArgument();
        var command = new Command("clear", "Remove the cover.");
        command.Arguments.Add(subjectArgument);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var subject = parseResult.GetValue(subjectArgument)!;
            return Run(parseResult, connectionOption, jsonOption, async (covers, asJson, ct) =>
            {
                var cleared = await covers.ClearAsync(subject, ct);
                if (asJson)
                {
                    Cli.WriteJson(new { subject = SubjectJson(cleared.Subject), removed = cleared.HadCover });
                    return;
                }

                Console.WriteLine(cleared.HadCover
                    ? $"Removed the cover from {cleared.Subject.Title}."
                    : $"{cleared.Subject.Title} had no cover.");
            }, cancellationToken);
        });
        return command;
    }

    private static Command CreateShow(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = SubjectArgument();
        var command = new Command("show", "Show the cover and its credit.");
        command.Arguments.Add(subjectArgument);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var subject = parseResult.GetValue(subjectArgument)!;
            return Run(parseResult, connectionOption, jsonOption, async (covers, asJson, ct) =>
            {
                var view = await covers.GetAsync(subject, ct);
                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        subject = SubjectJson(view.Subject),
                        cover = view.Cover is null ? null : CoverJson(view.Cover, covers.AppName)
                    });
                    return;
                }

                if (view.Cover is null)
                {
                    Console.WriteLine($"{Heading(view.Subject)} has no cover.");
                    return;
                }

                Console.WriteLine($"{Heading(view.Subject)}:");
                WriteCover(view.Cover, covers.AppName);
            }, cancellationToken);
        });
        return command;
    }

    private static Argument<string> SubjectArgument() => new("subject")
    {
        Description = "The identity statement, goal or project (urn, short id, or title)."
    };

    private static void WriteChange(CoverChange change, string appName, bool asJson, string done)
    {
        if (asJson)
        {
            Cli.WriteJson(new
            {
                subject = SubjectJson(change.Subject),
                cover = CoverJson(change.Cover, appName),
                downloadCounted = change.DownloadCounted,
                downloadProblem = change.DownloadProblem
            });
            return;
        }

        Console.WriteLine($"{done} on {Heading(change.Subject)}:");
        WriteCover(change.Cover, appName);
        if (change.DownloadProblem is { } problem)
        {
            Console.Error.WriteLine(problem);
        }
    }

    private static void WriteCover(Cover cover, string appName)
    {
        Console.WriteLine($"  Photo by {cover.Photographer} on Unsplash{(cover.Alt is null ? "" : $" — {cover.Alt}")}");
        Console.WriteLine($"  photo:   {cover.PhotoId} ({cover.Width} × {cover.Height}, {cover.Color})");
        Console.WriteLine($"  centred: {cover.PositionY}% from the top");
        Console.WriteLine($"  credit:  {cover.PhotographerLink(appName)}");
        Console.WriteLine($"  page:    {cover.PhotoLink(appName)}");
        Console.WriteLine($"  set:     {cover.SetAt.ToUniversalTime():yyyy-MM-dd HH:mm} UTC");
    }

    private static string Heading(SubjectRef subject) => $"{subject.Title} ({subject.Type}, {subject.Urn})";

    private static object SubjectJson(SubjectRef subject)
        => new { urn = subject.Urn, type = subject.Type, title = subject.Title };

    private static object CoverJson(Cover cover, string appName) => new
    {
        cover.Source,
        cover.PhotoId,
        cover.Url,
        cover.Color,
        cover.Width,
        cover.Height,
        cover.Alt,
        cover.Photographer,
        cover.PositionY,
        cover.SetAt,
        photographerLink = cover.PhotographerLink(appName),
        photoLink = cover.PhotoLink(appName),
        unsplashLink = Cover.UnsplashLink(appName)
    };

    private static Task<int> Run(
        ParseResult parseResult, Option<string?> connectionOption, Option<bool> jsonOption,
        Func<CoverService, bool, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
        var asJson = parseResult.GetValue(jsonOption);

        return Cli.RunAsync(asJson, async () =>
        {
            await using var provider = Cli.BuildServices(connectionString);
            await action(provider.GetRequiredService<CoverService>(), asJson, cancellationToken);
            return 0;
        });
    }
}

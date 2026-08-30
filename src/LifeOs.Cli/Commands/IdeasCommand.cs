using System.CommandLine;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

internal static class IdeasCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var problemArgument = new Argument<string>("problem")
        {
            Description = "The problem statement (or an existing Problem's urn) to generate ideas for."
        };

        var countOption = new Option<int>("--count", "-n")
        {
            Description = "How many ideas to prompt for.",
            DefaultValueFactory = _ => 10
        };

        var command = new Command("ideas",
            "State a problem and capture N ideas as one immutable idea_session event.");
        command.Arguments.Add(problemArgument);
        command.Options.Add(countOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var problem = parseResult.GetValue(problemArgument)!;
            var count = Math.Max(1, parseResult.GetValue(countOption));

            return Cli.RunAsync(asJson, async () =>
            {
                var ideas = ReadIdeas(count);
                if (ideas.Count == 0)
                {
                    if (asJson)
                    {
                        Cli.WriteJson(new { captured = false, reason = "no-ideas" });
                    }
                    else
                    {
                        Console.WriteLine("No ideas entered; nothing captured.");
                    }

                    return 0;
                }

                await using var provider = Cli.BuildServices(connectionString);
                var subjects = provider.GetRequiredService<SubjectService>();
                var capture = provider.GetRequiredService<CaptureService>();

                var problemSubject = await subjects.ResolveOrCreateAsync(
                    SubjectTypes.Problem, problem, cancellationToken);

                var session = await capture.CaptureIdeaSessionAsync(
                    problemSubject.Subject.Id, problemSubject.Subject.Title, ideas, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        sessionId = session.EventId,
                        problemId = problemSubject.Subject.Id,
                        problemUrn = problemSubject.Subject.Urn,
                        problemCreated = problemSubject.Created,
                        ideaCount = session.IdeaCount
                    });
                }
                else
                {
                    var problemState = problemSubject.Created ? "new Problem" : "existing Problem";
                    Console.WriteLine(
                        $"Captured {session.IdeaCount} idea(s) for {problemState} {problemSubject.Subject.Urn} " +
                        $"(session {session.EventId}).");
                }

                return 0;
            });
        });

        return command;
    }

    // Reads up to `count` ideas, one per line, from stdin. Stops early on a blank
    // line or end of input. Prompts go to stderr so stdout stays clean for --json.
    private static List<string> ReadIdeas(int count)
    {
        var interactive = !Console.IsInputRedirected;
        if (interactive)
        {
            Console.Error.WriteLine($"Enter up to {count} ideas, one per line. Blank line to finish.");
        }

        var ideas = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            if (interactive)
            {
                Console.Error.Write($"  idea {i + 1}/{count}: ");
            }

            var line = Console.ReadLine();
            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                break;
            }

            ideas.Add(line);
        }

        return ideas;
    }
}

using System.CommandLine;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk promote &lt;source&gt; &lt;type&gt; "title"</c> — the split promote (D4).
/// A <b>source event</b> (a uuid naming a capture) is turned into a tracked subject,
/// recording the event as its <c>origin_event_id</c> (the source is never touched).
/// A <b>source subject</b> (the canonical case: an Idea) promotes into new work by
/// creating the target and a <c>results_in</c> edge (source → target), advancing an
/// Idea to <c>Promoted</c>. Either way the source is resolved out of the inbox
/// (INBOX-1 triage marker → promoted).
/// </summary>
internal static class PromoteCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var sourceArgument = new Argument<string>("source")
        {
            Description = "The source to promote: a capture event id, or a subject (an Idea) urn/short id/title."
        };
        var typeArgument = new Argument<string>("type")
        {
            Description = "The subject type to create (e.g. Project, Goal, Task, Problem)."
        };
        var titleArgument = new Argument<string>("title") { Description = "The new subject's title." };

        var command = new Command("promote", "Promote a capture event, or an Idea, into a tracked subject.");
        command.Arguments.Add(sourceArgument);
        command.Arguments.Add(typeArgument);
        command.Arguments.Add(titleArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var source = parseResult.GetValue(sourceArgument)!;
            var type = parseResult.GetValue(typeArgument)!;
            var title = parseResult.GetValue(titleArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var promotion = provider.GetRequiredService<PromotionService>();
                var triage = provider.GetRequiredService<TriageService>();

                // A uuid naming a real event is an event promote; anything else is a
                // subject (Idea) promote.
                Guid? eventId = null;
                if (Guid.TryParse(source, out var gid)
                    && await provider.GetRequiredService<IEventReader>().FindAsync(gid, cancellationToken) is not null)
                {
                    eventId = gid;
                }

                if (eventId is { } id)
                {
                    var result = await promotion.PromoteAsync(id, type, title, cancellationToken);
                    await triage.SetStateAsync(isEvent: true, result.OriginEventId, TriageStates.Promoted, cancellationToken);

                    if (asJson)
                    {
                        Cli.WriteJson(new
                        {
                            id = result.Subject.Id,
                            urn = result.Subject.Urn,
                            type = result.Subject.Type,
                            title = result.Subject.Title,
                            originEventId = result.OriginEventId
                        });
                    }
                    else
                    {
                        Console.WriteLine(
                            $"Promoted event {result.OriginEventId} into {result.Subject.Type} {result.Subject.Urn}.");
                    }
                }
                else
                {
                    var result = await promotion.PromoteSubjectAsync(source, type, title, cancellationToken);
                    await triage.SetStateAsync(isEvent: false, result.Source.Id, TriageStates.Promoted, cancellationToken);

                    if (asJson)
                    {
                        Cli.WriteJson(new
                        {
                            id = result.Target.Id,
                            urn = result.Target.Urn,
                            type = result.Target.Type,
                            title = result.Target.Title,
                            sourceUrn = result.Source.Urn,
                            relation = SubjectRelations.ResultsIn
                        });
                    }
                    else
                    {
                        Console.WriteLine(
                            $"Promoted {result.Source.Urn} into {result.Target.Type} {result.Target.Urn} " +
                            $"({SubjectRelations.ResultsIn}).");
                    }
                }

                return 0;
            });
        });

        return command;
    }
}

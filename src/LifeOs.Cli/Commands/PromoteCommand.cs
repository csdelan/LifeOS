using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk promote &lt;event-id&gt; &lt;type&gt; "title"</c> — turn a capture into a
/// tracked subject. The new subject records the source event as its
/// <c>origin_event_id</c>, which is the promotion link; the source event is never
/// touched. There is no <c>promoted_from</c> edge (see #24) — the source subject
/// stays reachable through the source event's payload.
/// </summary>
internal static class PromoteCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var eventArgument = new Argument<Guid>("event-id")
        {
            Description = "The source event to promote from."
        };
        var typeArgument = new Argument<string>("type")
        {
            Description = "The subject type to create (e.g. Project, Problem, Idea)."
        };
        var titleArgument = new Argument<string>("title") { Description = "The new subject's title." };

        var command = new Command("promote", "Promote a capture event into a tracked subject.");
        command.Arguments.Add(eventArgument);
        command.Arguments.Add(typeArgument);
        command.Arguments.Add(titleArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var eventId = parseResult.GetValue(eventArgument);
            var type = parseResult.GetValue(typeArgument)!;
            var title = parseResult.GetValue(titleArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<PromotionService>()
                    .PromoteAsync(eventId, type, title, cancellationToken);

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

                return 0;
            });
        });

        return command;
    }
}

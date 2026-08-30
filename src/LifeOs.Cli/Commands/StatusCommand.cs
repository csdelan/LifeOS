using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk status &lt;subject&gt; &lt;new-status&gt;</c> — change a subject's status by
/// appending a <c>state_change</c> event. The projection is derived: the new status
/// shows up after the next <c>bsk rebuild</c>, never by a direct write.
/// </summary>
internal static class StatusCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = new Argument<string>("subject")
        {
            Description = "The subject to transition (urn, short id, or title)."
        };
        var statusArgument = new Argument<string>("status")
        {
            Description = "The new status, e.g. open, active, done, dropped."
        };

        var command = new Command("status", "Change a subject's status via a state_change event.");
        command.Arguments.Add(subjectArgument);
        command.Arguments.Add(statusArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var subject = parseResult.GetValue(subjectArgument)!;
            var status = parseResult.GetValue(statusArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<StatusService>()
                    .ChangeStatusAsync(subject, status, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        eventId = result.EventId,
                        subject = new { result.Subject.Urn, result.Subject.Type },
                        status = result.Status
                    });
                }
                else
                {
                    Console.WriteLine(
                        $"Recorded status '{result.Status}' for {result.Subject.Urn} " +
                        $"(state_change {result.EventId}); run `bsk rebuild` to refresh the projection.");
                }

                return 0;
            });
        });

        return command;
    }
}

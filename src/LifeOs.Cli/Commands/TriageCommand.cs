using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk flag &lt;item&gt;</c>, <c>bsk drop &lt;item&gt;</c>, <c>bsk dismiss &lt;item&gt;</c>
/// — the INBOX-1 triage actions on the two axes (attention vs. status, 0021). Flag puts
/// an item (subject or event id) in the inbox. Dismiss clears it attention-only, with no
/// status change. Drop resolves it as "it's nothing" — for a <em>subject</em> a Status
/// change to the type's dismiss terminal (so the resolution lives in Status, not a triage
/// state); an event has no status, so Drop degrades to Dismiss. Flag/Dismiss (and event
/// Drop) append a <c>triage</c> event; the newest marker per item wins, and <c>v_inbox</c>
/// lists the flagged, non-terminal, non-archived ones. Confirmation for Drop is the UI's job.
/// </summary>
internal static class TriageCommand
{
    public static Command CreateFlag(Option<string?> connectionOption, Option<bool> jsonOption)
        => Build("flag", "Flag an item (subject or event id) into the inbox.",
            (service, item, ct) => service.FlagAsync(item, ct), connectionOption, jsonOption);

    public static Command CreateDrop(Option<string?> connectionOption, Option<bool> jsonOption)
        => Build("drop", "Drop an inbox item as \"it's nothing\" (a subject records this in its Status).",
            (service, item, ct) => service.DropAsync(item, ct), connectionOption, jsonOption);

    public static Command CreateDismiss(Option<string?> connectionOption, Option<bool> jsonOption)
        => Build("dismiss", "Dismiss an inbox item attention-only, with no status change.",
            (service, item, ct) => service.DismissAsync(item, ct), connectionOption, jsonOption);

    private static Command Build(
        string name, string description,
        Func<TriageService, string, CancellationToken, Task<TriageResult>> invoke,
        Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var itemArgument = new Argument<string>("item")
        {
            Description = "The item: a subject (urn, short id, or title) or an event id."
        };

        var command = new Command(name, description);
        command.Arguments.Add(itemArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var item = parseResult.GetValue(itemArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<TriageService>();
                var result = await invoke(service, item, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        eventId = result.EventId,
                        eventKind = result.StatusChanged ? "state_change" : "triage",
                        item = new { kind = result.Item.IsEvent ? "event" : "subject", id = result.Item.Id },
                        state = result.State
                    });
                }
                else if (result.StatusChanged)
                {
                    // Drop on a subject rerouted to a Status change (0021).
                    Console.WriteLine(
                        $"Dropped {result.Item.Label} — status set to {result.State} (state_change {result.EventId}).");
                }
                else
                {
                    Console.WriteLine($"Triage {result.State}: {result.Item.Label} (triage {result.EventId}).");
                }

                return 0;
            });
        });

        return command;
    }
}

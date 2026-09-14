using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk flag &lt;item&gt;</c>, <c>bsk drop &lt;item&gt;</c>, <c>bsk file &lt;item&gt;</c>
/// — the INBOX-1 triage marker. Flag puts an item (subject or event id) in the
/// inbox; Drop resolves it as "nothing to do"; File resolves it as kept-for-
/// reference. Each appends a <c>triage</c> event; the newest marker per item wins,
/// and <c>v_inbox</c> lists the flagged ones. Confirmation for Drop is the UI's job.
/// </summary>
internal static class TriageCommand
{
    public static Command CreateFlag(Option<string?> connectionOption, Option<bool> jsonOption)
        => Build("flag", "Flag an item (subject or event id) into the inbox.",
            TriageStates.Flagged, connectionOption, jsonOption);

    public static Command CreateDrop(Option<string?> connectionOption, Option<bool> jsonOption)
        => Build("drop", "Resolve an inbox item as dropped (nothing to do).",
            TriageStates.Dropped, connectionOption, jsonOption);

    public static Command CreateFile(Option<string?> connectionOption, Option<bool> jsonOption)
        => Build("file", "Resolve an inbox item as filed (kept as reference).",
            TriageStates.Filed, connectionOption, jsonOption);

    private static Command Build(
        string name, string description, string state,
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
                var result = state switch
                {
                    TriageStates.Flagged => await service.FlagAsync(item, cancellationToken),
                    TriageStates.Dropped => await service.DropAsync(item, cancellationToken),
                    _ => await service.FileAsync(item, cancellationToken)
                };

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        triageEventId = result.EventId,
                        item = new { kind = result.Item.IsEvent ? "event" : "subject", id = result.Item.Id },
                        state = result.State
                    });
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

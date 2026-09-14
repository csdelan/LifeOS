using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk archive &lt;subject&gt;</c> and <c>bsk restore &lt;subject&gt;</c> — the D9
/// universal archive flag. Archiving hides a subject from default views without
/// deleting anything; restoring reverses it. Both append an <c>archive_change</c>
/// event (never a mutating write), so the flag stays reversible with history and
/// orthogonal to workflow status.
/// </summary>
internal static class ArchiveCommand
{
    public static Command CreateArchive(Option<string?> connectionOption, Option<bool> jsonOption)
        => Build("archive", "Archive a subject (hide it from default views, reversibly).",
            archived: true, connectionOption, jsonOption);

    public static Command CreateRestore(Option<string?> connectionOption, Option<bool> jsonOption)
        => Build("restore", "Restore an archived subject back into default views.",
            archived: false, connectionOption, jsonOption);

    private static Command Build(
        string name, string description, bool archived,
        Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = new Argument<string>("subject")
        {
            Description = "The subject to archive/restore (urn, short id, or title)."
        };

        var command = new Command(name, description);
        command.Arguments.Add(subjectArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var subject = parseResult.GetValue(subjectArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<ArchiveService>();
                var result = archived
                    ? await service.ArchiveAsync(subject, cancellationToken)
                    : await service.RestoreAsync(subject, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        eventId = result.EventId,
                        subject = new { result.Subject.Urn, result.Subject.Type },
                        archived = result.Archived
                    });
                }
                else
                {
                    var verb = result.Archived ? "Archived" : "Restored";
                    Console.WriteLine($"{verb} {result.Subject.Urn} (archive_change {result.EventId}).");
                }

                return 0;
            });
        });

        return command;
    }
}

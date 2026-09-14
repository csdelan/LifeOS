using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk involve &lt;subject&gt; &lt;person&gt; --role &lt;role&gt; [--remove]</c> — attach
/// (or detach) a Person to an item in a role: attendee, owner, assignee, waiting_for,
/// or involves. This is a People-association property (attributes.people), kept
/// separate from the alignment graph and from tags.
/// </summary>
internal static class InvolveCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = new Argument<string>("subject")
        {
            Description = "The item to involve the person in (urn, short id, or title)."
        };
        var personArgument = new Argument<string>("person")
        {
            Description = "The Person (urn, short id, or title)."
        };
        var roleOption = new Option<string>("--role")
        {
            Description = $"The role: {string.Join(", ", PersonRoles.All)}.",
            DefaultValueFactory = _ => PersonRoles.Involves
        };
        var removeOption = new Option<bool>("--remove") { Description = "Remove the association instead of adding it." };

        var command = new Command("involve", "Attach or detach a Person to an item in a role.");
        command.Arguments.Add(subjectArgument);
        command.Arguments.Add(personArgument);
        command.Options.Add(roleOption);
        command.Options.Add(removeOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var subject = parseResult.GetValue(subjectArgument)!;
            var person = parseResult.GetValue(personArgument)!;
            var role = parseResult.GetValue(roleOption)!;
            var remove = parseResult.GetValue(removeOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<PeopleService>();
                var result = remove
                    ? await service.RemoveAsync(subject, person, role, cancellationToken)
                    : await service.AddAsync(subject, person, role, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        subject = result.Subject.Urn,
                        person = result.Person.Urn,
                        role = result.Role,
                        changed = result.Changed,
                        removed = remove
                    });
                }
                else
                {
                    var verb = remove ? "Detached" : "Involved";
                    var suffix = result.Changed ? "" : " (no change)";
                    Console.WriteLine($"{verb} {result.Person.Urn} ({result.Role}) on {result.Subject.Urn}{suffix}.");
                }

                return 0;
            });
        });

        return command;
    }
}

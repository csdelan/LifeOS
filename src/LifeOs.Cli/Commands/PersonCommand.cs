using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk person</c> — what only a Person has (GEN-15).
/// <list type="bullet">
/// <item><c>bsk person photo &lt;person&gt; --file &lt;path&gt; | --remove</c> — set or remove a Person's
/// photo: a JPEG, PNG or WebP of at most 5 MB, in place of any it had. Removing leaves the bytes
/// in the record.</item>
/// </list>
/// </summary>
internal static class PersonCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("person", "What only a Person has: 'person photo'.");
        command.Subcommands.Add(CreatePhoto(connectionOption, jsonOption));
        return command;
    }

    private static Command CreatePhoto(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var personArgument = new Argument<string>("person") { Description = "The Person (its URN, id or name)." };
        var fileOption = new Option<string?>("--file", "-f")
        {
            Description = "A JPEG, PNG or WebP of at most 5 MB. Replaces the photo the Person has."
        };
        var removeOption = new Option<bool>("--remove") { Description = "Remove the Person's photo." };
        var command = new Command("photo", "Set or remove a Person's photo.");
        command.Arguments.Add(personArgument);
        command.Options.Add(fileOption);
        command.Options.Add(removeOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var person = parseResult.GetValue(personArgument)!;
            var path = parseResult.GetValue(fileOption);
            var remove = parseResult.GetValue(removeOption);
            return Cli.RunAsync(asJson, async () =>
            {
                if (remove == (path is not null))
                {
                    Cli.WriteError(asJson, "Give --file <path> to set the photo, or --remove to remove it.");
                    return 1;
                }

                if (path is not null && !File.Exists(path))
                {
                    Cli.WriteError(asJson, $"File not found: {path}");
                    return 1;
                }

                await using var provider = Cli.BuildServices(connectionString);
                var photos = provider.GetRequiredService<PersonPhotoService>();
                var result = path is null
                    ? await photos.RemoveAsync(person, cancellationToken)
                    : await photos.SetAsync(
                        person, await File.ReadAllBytesAsync(path, cancellationToken), Path.GetFileName(path), cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(PersonPhotoRead.From(result));
                }
                else if (result.ArtifactId is { } photo)
                {
                    Console.WriteLine($"Set the photo of {result.Person.Title}. [{result.Person.Urn}] (photo {photo})");
                }
                else
                {
                    Console.WriteLine($"{result.Person.Title} has no photo. [{result.Person.Urn}]");
                }

                return 0;
            });
        });

        return command;
    }
}

using System.CommandLine;
using System.Text.Json.Nodes;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk new &lt;type&gt; "title"</c> — create any of the eleven subject types with
/// its relevant attributes and no DDL change. Cross-cutting flags (<c>--cadence</c>,
/// <c>--review-at</c>) apply to any subject; the rest are per-type conveniences that
/// simply land in the subject's <c>attributes</c> jsonb.
/// </summary>
internal static class NewCommand
{
    private static readonly string[] Types =
    [
        SubjectTypes.Value, SubjectTypes.Goal, SubjectTypes.Problem, SubjectTypes.Project,
        SubjectTypes.Task, SubjectTypes.Commitment, SubjectTypes.Decision, SubjectTypes.Idea,
        SubjectTypes.Person, SubjectTypes.Constraint, SubjectTypes.Season
    ];

    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var typeArgument = new Argument<string>("type")
        {
            Description = "The subject type to create (one of the eleven, e.g. Goal, Constraint, Season)."
        };
        var titleArgument = new Argument<string>("title") { Description = "The subject's title." };

        var cadenceOption = new Option<string?>("--cadence")
        {
            Description = "Expected review/activity cadence (any subject), e.g. weekly."
        };
        var reviewAtOption = new Option<string?>("--review-at")
        {
            Description = "When this subject should next be reviewed (ISO-8601, any subject)."
        };
        var endStateOption = new Option<string?>("--end-state")
        {
            Description = "Goal: the end-state that counts as reaching the goal."
        };
        var scopeOption = new Option<string?>("--scope")
        {
            Description = "Constraint: the scope it limits — capacity or interaction."
        };
        scopeOption.AcceptOnlyFromAmong("capacity", "interaction");
        var limitOption = new Option<string?>("--limit")
        {
            Description = "Constraint: the limit it imposes, e.g. \"2 open projects\"."
        };
        var focusOption = new Option<string?>("--focus") { Description = "Season: its focus." };
        var endsOption = new Option<string?>("--ends")
        {
            Description = "Season: when it ends (ISO-8601 date)."
        };
        var slotOption = new Option<string[]>("--slot")
        {
            Description = "Value: a slot this value occupies (repeatable).",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };
        var attrOption = new Option<string[]>("--attr")
        {
            Description = "Arbitrary attribute as key=value (repeatable).",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };

        var command = new Command("new", "Create a subject of any type with its attributes.");
        command.Arguments.Add(typeArgument);
        command.Arguments.Add(titleArgument);
        foreach (var option in new Option[]
                 {
                     cadenceOption, reviewAtOption, endStateOption, scopeOption, limitOption,
                     focusOption, endsOption, slotOption, attrOption
                 })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var type = parseResult.GetValue(typeArgument)!;
            var title = parseResult.GetValue(titleArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                var canonicalType = ResolveType(type);

                var attributes = new JsonObject();
                Set(attributes, "expected_cadence", parseResult.GetValue(cadenceOption));
                Set(attributes, "next_review_at", parseResult.GetValue(reviewAtOption));
                Set(attributes, "end_state", parseResult.GetValue(endStateOption));
                Set(attributes, "scope", parseResult.GetValue(scopeOption));
                Set(attributes, "limit", parseResult.GetValue(limitOption));
                Set(attributes, "focus", parseResult.GetValue(focusOption));
                Set(attributes, "ends", parseResult.GetValue(endsOption));

                var slots = parseResult.GetValue(slotOption) ?? [];
                if (slots.Length > 0)
                {
                    attributes["slots"] = new JsonArray(slots.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
                }

                foreach (var (key, value) in ParseAttrs(parseResult.GetValue(attrOption) ?? []))
                {
                    attributes[key] = value;
                }

                await using var provider = Cli.BuildServices(connectionString);
                var subjects = provider.GetRequiredService<SubjectService>();

                var created = await subjects.CreateAsync(
                    canonicalType, title, attributes.ToJsonString(), cancellationToken: cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new { id = created.Id, urn = created.Urn, type = created.Type, title = created.Title });
                }
                else
                {
                    Console.WriteLine($"Created {created.Type} {created.Urn}.");
                }

                return 0;
            });
        });

        return command;
    }

    // Accept the type case-insensitively but persist the canonical PascalCase form
    // the schema's CHECK constraint expects.
    private static string ResolveType(string type)
        => Types.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase))
           ?? throw new ArgumentException(
               $"Unknown subject type '{type}'. Expected one of: {string.Join(", ", Types)}.");

    private static void Set(JsonObject target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }

    private static IEnumerable<(string Key, string Value)> ParseAttrs(IEnumerable<string> pairs)
    {
        foreach (var pair in pairs)
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                throw new ArgumentException($"--attr expects key=value; got '{pair}'.");
            }

            yield return (pair[..separator].Trim(), pair[(separator + 1)..].Trim());
        }
    }
}

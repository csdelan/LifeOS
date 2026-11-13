using System.CommandLine;
using System.Text.Json.Nodes;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk new &lt;type&gt; "title"</c> — create any creatable subject type (<see cref="SubjectTypes.Creatable"/>) with
/// its relevant attributes and no DDL change. Cross-cutting flags (<c>--cadence</c>,
/// <c>--review-at</c>) apply to any subject; the rest are per-type conveniences that
/// simply land in the subject's <c>attributes</c> jsonb.
/// </summary>
internal static class NewCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var typeArgument = new Argument<string>("type")
        {
            Description = "The subject type to create (e.g. Goal, Task, Habit, Reward)."
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
        var areaOption = new Option<string?>("--area")
        {
            Description = "Area of Focus this item belongs to (an Area subject's urn/reference; any item)."
        };
        var parentOption = new Option<string?>("--parent")
        {
            Description = "Create beneath this parent (urn/short id/title) and relate to it atomically (GEN-7)."
        };
        var relationOption = new Option<string?>("--relation")
        {
            Description = "The child→parent relation, when the parent pair is ambiguous (default: inferred)."
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
        var statementOption = new Option<string?>("--statement")
        {
            Description = "Value: the full identity statement (the title is its short handle)."
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
                     cadenceOption, reviewAtOption, areaOption, parentOption, relationOption,
                     endStateOption, scopeOption, limitOption,
                     focusOption, endsOption, statementOption, slotOption, attrOption
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
                var canonicalType = SubjectTypes.ParseCreatable(type);

                // A Value is an identity statement, which the kernel refuses without --statement.
                var attributes = new JsonObject();
                Set(attributes, "expected_cadence", parseResult.GetValue(cadenceOption));
                Set(attributes, "next_review_at", parseResult.GetValue(reviewAtOption));
                Set(attributes, "area", parseResult.GetValue(areaOption));
                Set(attributes, "end_state", parseResult.GetValue(endStateOption));
                Set(attributes, "scope", parseResult.GetValue(scopeOption));
                Set(attributes, "limit", parseResult.GetValue(limitOption));
                Set(attributes, "focus", parseResult.GetValue(focusOption));
                Set(attributes, "ends", parseResult.GetValue(endsOption));
                Set(attributes, "statement", parseResult.GetValue(statementOption));

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

                var parent = parseResult.GetValue(parentOption);
                var relation = parseResult.GetValue(relationOption);
                if (string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(relation))
                {
                    throw new ArgumentException("--relation only applies with --parent.");
                }

                // Under a parent with its edge (GEN-7); a Problem of the same title reused (D4);
                // a Problem or Idea flagged for the Inbox; a repeating Task's first repetition (D16).
                var made = await provider.GetRequiredService<SubjectCreationService>().CreateAsync(
                    canonicalType, title, attributes.ToJsonString(), parent, relation, cancellationToken);
                var created = made.Subject;
                var childLink = made.Link;
                var reused = made.Reused;

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        id = created.Id,
                        urn = created.Urn,
                        type = created.Type,
                        title = created.Title,
                        reused,
                        parent = childLink is null ? null : new { childLink.Parent.Urn, childLink.Relation }
                    });
                }
                else if (childLink is not null)
                {
                    Console.WriteLine(
                        $"Created {created.Type} {created.Urn} ({childLink.Relation} {childLink.Parent.Urn}).");
                }
                else if (reused)
                {
                    Console.WriteLine($"Reused existing {created.Type} {created.Urn} (flagged for triage).");
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

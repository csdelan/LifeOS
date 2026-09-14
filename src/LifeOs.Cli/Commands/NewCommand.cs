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
        SubjectTypes.Person, SubjectTypes.Constraint, SubjectTypes.Season, SubjectTypes.Area,
        SubjectTypes.Habit
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
                var canonicalType = ResolveType(type);

                // A Value is an identity statement: the title is its short handle, and a
                // full statement is required (enforced by the kernel; checked here so the
                // message is actionable rather than a raw constraint violation).
                var statement = parseResult.GetValue(statementOption);
                if (canonicalType == SubjectTypes.Value && string.IsNullOrWhiteSpace(statement))
                {
                    throw new ArgumentException(
                        "A Value needs --statement: the title is the short handle, the statement is who you've chosen to be.");
                }

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
                var subjects = provider.GetRequiredService<SubjectService>();

                var parent = parseResult.GetValue(parentOption);
                var relation = parseResult.GetValue(relationOption);
                if (string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(relation))
                {
                    throw new ArgumentException("--relation only applies with --parent.");
                }

                SubjectRef created;
                ChildCreationResult? childLink = null;
                var reused = false;
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    // Parent-first: create the child and its parent edge atomically (GEN-7).
                    childLink = await provider.GetRequiredService<ChildCreationService>()
                        .CreateChildAsync(
                            canonicalType, title, parent, relation, attributes.ToJsonString(), cancellationToken);
                    created = childLink.Child;
                }
                else if (canonicalType == SubjectTypes.Problem)
                {
                    // A Problem is unique by title — the durable question you keep returning
                    // to (§6 / D4). Re-creating one reuses the existing subject rather than
                    // erroring; attributes apply only when it is genuinely new.
                    var resolved = await subjects.ResolveOrCreateAsync(
                        canonicalType, title, attributes.ToJsonString(), cancellationToken);
                    created = resolved.Subject;
                    reused = !resolved.Created;
                }
                else
                {
                    created = await subjects.CreateAsync(
                        canonicalType, title, attributes.ToJsonString(), cancellationToken: cancellationToken);
                }

                // A Problem or Idea is a capture-like subject: it enters the inbox for
                // triage on creation, whether from quick capture or global New
                // (GEN-14 / CAP-6 flag-on-capture). Other types are deliberate and not flagged.
                if (canonicalType is SubjectTypes.Problem or SubjectTypes.Idea)
                {
                    await provider.GetRequiredService<TriageService>()
                        .FlagItemAsync(isEvent: false, created.Id, cancellationToken);
                }

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

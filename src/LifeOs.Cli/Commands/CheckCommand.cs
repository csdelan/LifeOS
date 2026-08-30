using System.CommandLine;
using System.Text.Json;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk check [--only &lt;diagnostic&gt;]</c> — run the diagnostics and print an
/// evidence-bearing report. Every finding prints the subject it concerns, the
/// rule that fired, and the evidence rows that triggered it; no finding is
/// unexplained. <c>--json</c> emits the findings as data for downstream
/// consumers (later, GlobalInboxService cards).
/// </summary>
internal static class CheckCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var onlyOption = new Option<string?>("--only")
        {
            Description = "Run only the named diagnostic instead of all of them."
        };

        var command = new Command("check",
            "Run the diagnostics and print an evidence-bearing report of what fired and why.");
        command.Options.Add(onlyOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var only = parseResult.GetValue(onlyOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var report = await provider.GetRequiredService<DiagnosticRunner>()
                    .RunAsync(only, cancellationToken);

                if (asJson)
                {
                    WriteJson(report);
                }
                else
                {
                    WriteText(report);
                }

                // Findings are advisory ("explain why you're telling me this"), not a
                // pass/fail gate: a completed run exits 0 whether or not it flagged
                // anything, so `bsk check` composes in scripts.
                return 0;
            });
        });

        return command;
    }

    private static void WriteJson(DiagnosticReport report)
    {
        Cli.WriteJson(new
        {
            diagnostics = report.Diagnostics.Select(d => new
            {
                name = d.Name,
                title = d.Title,
                findings = d.Findings.Select(f => new
                {
                    subject = new { id = f.Subject.Id, urn = f.Subject.Urn, type = f.Subject.Type, title = f.Subject.Title },
                    summary = f.Summary,
                    evidence = f.Evidence
                })
            }),
            findingCount = report.FindingCount
        });
    }

    private static void WriteText(DiagnosticReport report)
    {
        if (report.Diagnostics.Count == 0)
        {
            Console.WriteLine("No diagnostics are configured yet.");
            return;
        }

        foreach (var diagnostic in report.Diagnostics)
        {
            Console.WriteLine();
            Console.WriteLine($"{diagnostic.Name} — {diagnostic.Title}");

            if (diagnostic.Findings.Count == 0)
            {
                Console.WriteLine("  (no findings)");
                continue;
            }

            foreach (var finding in diagnostic.Findings)
            {
                Console.WriteLine($"  ● {finding.Subject.Type} \"{finding.Subject.Title}\"  {finding.Subject.Urn}");
                Console.WriteLine($"    {finding.Summary}");
                foreach (var line in RenderEvidence(finding.Evidence))
                {
                    Console.WriteLine($"      - {line}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{report.FindingCount} finding(s) across {report.Diagnostics.Count} diagnostic(s).");
    }

    // One compact line per evidence element. The convention is a `kind` + `id`
    // pointing at a source row, with any other keys as context; the renderer stays
    // generic so it prints whatever a diagnostic chooses to cite.
    private static IEnumerable<string> RenderEvidence(JsonElement evidence)
    {
        foreach (var item in evidence.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                yield return item.ToString();
                continue;
            }

            // Lead with "kind id" in that order, whatever order the JSON object
            // stores its keys in (Postgres jsonb reorders them), then any other
            // keys as context.
            var lead = new List<string>();
            if (item.TryGetProperty("kind", out var kind))
            {
                lead.Add(kind.ToString());
            }

            if (item.TryGetProperty("id", out var id))
            {
                lead.Add(id.ToString());
            }

            var context = new List<string>();
            foreach (var property in item.EnumerateObject())
            {
                if (!property.NameEquals("kind") && !property.NameEquals("id"))
                {
                    context.Add($"{property.Name}={property.Value}");
                }
            }

            var line = string.Join(" ", lead);
            if (context.Count > 0)
            {
                line = line.Length > 0 ? $"{line} ({string.Join(", ", context)})" : string.Join(", ", context);
            }

            yield return line;
        }
    }
}

using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk materialize &lt;series&gt; --through YYYY-MM-DD</c> — create the occurrence
/// subjects of a recurring appointment series up to the given date (CAL-1 / D10).
/// Each occurrence is its own Appointment with its own editable status. Idempotent:
/// re-running only fills in dates not yet materialized, so it can extend the horizon.
/// </summary>
internal static class MaterializeCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var seriesArgument = new Argument<string>("series")
        {
            Description = "The recurring appointment series (urn, short id, or title)."
        };
        var throughOption = new Option<string>("--through")
        {
            Description = "Materialize occurrences up to and including this date (YYYY-MM-DD).",
            Required = true
        };

        var command = new Command("materialize", "Create a recurring appointment's occurrences through a date.");
        command.Arguments.Add(seriesArgument);
        command.Options.Add(throughOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var series = parseResult.GetValue(seriesArgument)!;
            var throughText = parseResult.GetValue(throughOption)!;

            return Cli.RunAsync(asJson, async () =>
            {
                if (!DateOnly.TryParse(throughText, CultureInfo.InvariantCulture, out var through))
                {
                    throw new ArgumentException($"'{throughText}' is not a valid date (expected YYYY-MM-DD).");
                }

                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<AppointmentService>()
                    .MaterializeAsync(series, through, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        series = result.Series.Urn,
                        created = result.Created.Select(o => o.Urn).ToArray(),
                        count = result.Created.Count
                    });
                }
                else
                {
                    Console.WriteLine(
                        $"Materialized {result.Created.Count} occurrence(s) of {result.Series.Urn} through {through:yyyy-MM-dd}.");
                }

                return 0;
            });
        });

        return command;
    }
}

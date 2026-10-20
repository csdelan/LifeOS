using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk ledger [--from &lt;date&gt;] [--to &lt;date&gt;] [--limit &lt;n&gt;]</c> — list the
/// rewards ledger newest first (REWARD-5): each earn, reversal and redemption, its
/// amounts, and what it concerns or was for. Dates are local calendar days; both ends
/// are inclusive.
/// </summary>
internal static class LedgerCommand
{
    private const int DefaultLimit = 50;

    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var fromOption = new Option<string?>("--from")
        {
            Description = "Only entries on or after this local date (yyyy-MM-dd)."
        };
        var toOption = new Option<string?>("--to")
        {
            Description = "Only entries on or before this local date (yyyy-MM-dd)."
        };
        var limitOption = new Option<int>("--limit")
        {
            Description = $"At most this many entries (default {DefaultLimit}).",
            DefaultValueFactory = _ => DefaultLimit
        };

        var command = new Command("ledger", "List rewards earned and spent, newest first.");
        command.Options.Add(fromOption);
        command.Options.Add(toOption);
        command.Options.Add(limitOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var fromText = parseResult.GetValue(fromOption);
            var toText = parseResult.GetValue(toOption);
            var limit = parseResult.GetValue(limitOption);

            return Cli.RunAsync(asJson, async () =>
            {
                var from = fromText is null ? (DateTimeOffset?)null : LocalMidnight(ParseDate(fromText, "--from"));
                var to = toText is null
                    ? (DateTimeOffset?)null
                    : LocalMidnight(ParseDate(toText, "--to").AddDays(1));

                await using var provider = Cli.BuildServices(connectionString);
                var entries = await provider.GetRequiredService<RewardService>()
                    .GetLedgerAsync(from, to, limit, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(entries.Select(e => new
                    {
                        id = e.EventId,
                        occurredAt = e.OccurredAt,
                        entry = e.Entry,
                        provenance = e.Provenance,
                        amounts = e.Amounts,
                        trigger = e.Trigger,
                        awardKey = e.AwardKey,
                        subject = e.Subject is { } s ? new { id = s.Id, urn = s.Urn, type = s.Type, title = s.Title } : null,
                        note = e.Note,
                        dollars = e.Dollars,
                        coinRate = e.CoinRate
                    }));
                    return 0;
                }

                if (entries.Count == 0)
                {
                    Console.WriteLine("No ledger entries.");
                    return 0;
                }

                foreach (var e in entries)
                {
                    var about = e.Subject is { } s ? $"{s.Title} ({s.Type})" : e.Note ?? "";
                    if (e.Subject is not null && !string.IsNullOrEmpty(e.Note))
                    {
                        about += $" — {e.Note}";
                    }

                    if (e.Dollars is { } dollars)
                    {
                        about += $" [{RewardOutput.Usd(dollars)}]";
                    }

                    Console.WriteLine(
                        $"{RewardOutput.Stamp(e.OccurredAt)}  {e.Entry,-9} {RewardOutput.Amounts(e.Amounts),-28} {about}");
                }

                return 0;
            });
        });

        return command;
    }

    private static DateOnly ParseDate(string text, string option)
        => DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"{option} expects a date as yyyy-MM-dd; got '{text}'.");

    private static DateTimeOffset LocalMidnight(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}

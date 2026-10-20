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
/// <para>
/// <c>bsk ledger --daily [--from] [--to] [--tz &lt;zone&gt;]</c> totals each day instead —
/// what it earned, gave back and spent per currency (the last 30 days by default), with
/// days read in <c>--tz</c> (an IANA name; this machine's zone by default), as
/// <c>GET /api/ledger/daily</c> reads them.
/// </para>
/// </summary>
internal static class LedgerCommand
{
    private const int DefaultLimit = 50;
    private const int DefaultDailyDays = 30;

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
        var dailyOption = new Option<bool>("--daily")
        {
            Description = $"Total each day instead: earned, given back and spent per currency (default: the last {DefaultDailyDays} days)."
        };
        var tzOption = new Option<string?>("--tz")
        {
            Description = "With --daily: the IANA time zone days are read in (default: this machine's)."
        };

        var command = new Command("ledger", "List rewards earned and spent, newest first.");
        command.Options.Add(fromOption);
        command.Options.Add(toOption);
        command.Options.Add(limitOption);
        command.Options.Add(dailyOption);
        command.Options.Add(tzOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var fromText = parseResult.GetValue(fromOption);
            var toText = parseResult.GetValue(toOption);
            var limit = parseResult.GetValue(limitOption);
            var daily = parseResult.GetValue(dailyOption);
            var tz = parseResult.GetValue(tzOption);

            if (daily)
            {
                return Cli.RunAsync(asJson, () => DailyAsync(connectionString, asJson, fromText, toText, tz, cancellationToken));
            }

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

    private static async Task<int> DailyAsync(
        string connectionString, bool asJson, string? fromText, string? toText, string? tz,
        CancellationToken cancellationToken)
    {
        var local = string.IsNullOrWhiteSpace(tz) ? LocalTimeZone() : null;
        // A machine zone with no known IANA name reads in UTC, and says so.
        var unknownLocal = string.IsNullOrWhiteSpace(tz) && local is null;
        var zone = string.IsNullOrWhiteSpace(tz) ? local ?? "UTC" : tz.Trim();
        var to = toText is null ? Today(zone) : ParseDate(toText, "--to");
        var from = fromText is null ? to.AddDays(1 - DefaultDailyDays) : ParseDate(fromText, "--from");

        await using var provider = Cli.BuildServices(connectionString);
        // --to is inclusive here; the read takes the day after it as its end.
        var days = await provider.GetRequiredService<RewardService>()
            .GetDailyAsync(from, to.AddDays(1), zone, cancellationToken);

        if (asJson)
        {
            Cli.WriteJson(new
            {
                timeZone = zone,
                from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                days = days.Select(d => new
                {
                    date = d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    earned = d.Earned,
                    reversed = d.Reversed,
                    spent = d.Spent,
                    net = d.Net
                })
            });
            return 0;
        }

        if (unknownLocal)
        {
            Console.WriteLine(
                $"This machine's time zone ('{TimeZoneInfo.Local.Id}') has no known IANA name, so days are read in UTC; pass --tz to choose one.");
        }

        Console.WriteLine($"Daily totals in {zone}, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}:");
        if (days.Count == 0)
        {
            Console.WriteLine("No ledger entries in that range.");
            return 0;
        }

        foreach (var d in days)
        {
            var parts = new List<string>();
            if (d.Earned.Count > 0)
            {
                parts.Add($"earned {RewardOutput.Amounts(d.Earned)}");
            }

            if (d.Reversed.Count > 0)
            {
                parts.Add($"gave back {RewardOutput.Amounts(Negated(d.Reversed))}");
            }

            if (d.Spent.Count > 0)
            {
                parts.Add($"spent {RewardOutput.Amounts(Negated(d.Spent))}");
            }

            if (parts.Count == 0)
            {
                parts.Add($"net {RewardOutput.Amounts(d.Net)}");
            }

            Console.WriteLine($"{d.Date:yyyy-MM-dd}  {string.Join(" · ", parts)}");
        }

        return 0;
    }

    private static Dictionary<string, long> Negated(IReadOnlyDictionary<string, long> amounts)
        => amounts.ToDictionary(kv => kv.Key, kv => -kv.Value);

    /// <summary>
    /// Common Windows zone ids and their IANA names (CLDR windowsZones). The solution runs with invariant
    /// globalization, so .NET can't convert a Windows id itself; for any zone not listed, pass --tz.
    /// </summary>
    private static readonly Dictionary<string, string> WindowsZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UTC"] = "UTC",
        ["Hawaiian Standard Time"] = "Pacific/Honolulu",
        ["Alaskan Standard Time"] = "America/Anchorage",
        ["Pacific Standard Time"] = "America/Los_Angeles",
        ["US Mountain Standard Time"] = "America/Phoenix",
        ["Mountain Standard Time"] = "America/Denver",
        ["Central Standard Time"] = "America/Chicago",
        ["Eastern Standard Time"] = "America/New_York",
        ["Atlantic Standard Time"] = "America/Halifax",
        ["Newfoundland Standard Time"] = "America/St_Johns",
        ["GMT Standard Time"] = "Europe/London",
        ["W. Europe Standard Time"] = "Europe/Berlin",
        ["Romance Standard Time"] = "Europe/Paris",
        ["Central Europe Standard Time"] = "Europe/Budapest",
        ["Central European Standard Time"] = "Europe/Warsaw",
        ["FLE Standard Time"] = "Europe/Kiev",
        ["India Standard Time"] = "Asia/Kolkata",
        ["China Standard Time"] = "Asia/Shanghai",
        ["Tokyo Standard Time"] = "Asia/Tokyo",
        ["AUS Eastern Standard Time"] = "Australia/Sydney",
        ["New Zealand Standard Time"] = "Pacific/Auckland",
    };

    /// <summary>This machine's zone as an IANA name (Windows ids are converted), or null when it can't be.</summary>
    private static string? LocalTimeZone()
    {
        var local = TimeZoneInfo.Local;
        if (local.HasIanaId)
        {
            return local.Id;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana))
        {
            return iana;
        }

        return WindowsZones.GetValueOrDefault(local.Id);
    }

    /// <summary>Today's date in <paramref name="zone"/>, or on this machine when .NET doesn't know the zone.</summary>
    private static DateOnly Today(string zone)
        => TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var info)
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, info).DateTime)
            : DateOnly.FromDateTime(DateTime.Now);

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

using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// <c>GET /api/domains</c> (REWARD-6 / REWARD-7, xp-domains plan §3.11): every life domain in
/// display order, as of <see cref="Today"/> (<c>yyyy-MM-dd</c>) in the home zone. Dates are
/// home-zone days, not the viewer's.
/// </summary>
public sealed class DomainsRead
{
    public string Today { get; set; } = "";
    public IReadOnlyList<DomainRow> Domains { get; set; } = [];
}

/// <summary>
/// One life domain: its currency, the XP it has collected, and whether it has rules. Only a
/// configured domain (Health in the pilot) has <see cref="Lifetime"/>, <see cref="Current"/>
/// and <see cref="Ranks"/>; <see cref="NextRank"/> is null at the last rank.
/// </summary>
public sealed class DomainRow
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>The domain's name, e.g. "Health".</summary>
    public string Name { get; set; } = "";
    public long TotalXp { get; set; }
    public bool Configured { get; set; }
    public DomainLifetime? Lifetime { get; set; }
    public DomainNextRank? NextRank { get; set; }
    public DomainCurrent? Current { get; set; }
    public IReadOnlyList<DomainRankRow>? Ranks { get; set; }
}

/// <summary>
/// Lifetime advancement, which a bad month never lowers: the level (stopped at the top of the
/// rank held), the level the XP alone reaches, the rank and the day it qualified (null for
/// Novice), the XP into the level and still to go, and — while capped — the XP banked.
/// </summary>
public sealed class DomainLifetime
{
    /// <summary>The first day with paid XP, or null before there is any.</summary>
    public string? Start { get; set; }
    public int Level { get; set; }
    public int RawLevel { get; set; }
    /// <summary>The highest level the rank allows (9 for Novice).</summary>
    public int TopLevel { get; set; }
    public bool Capped { get; set; }
    /// <summary>The rank held: 0 is Novice.</summary>
    public int Rank { get; set; }
    public string RankTitle { get; set; } = "";
    public string? QualifiedOn { get; set; }
    /// <summary>What the current level costs in all; the whole of it while capped.</summary>
    public long LevelXp { get; set; }
    public long XpIntoLevel { get; set; }
    public long XpToNext { get; set; }
    public long BankedXp { get; set; }
}

/// <summary>
/// The next rank's gate as it reads today: its level, bar (0–1) and window, the window's
/// successful days against those the bar takes, and the domain's age against the window.
/// Today counts once it has succeeded.
/// </summary>
public sealed class DomainNextRank
{
    public int Rank { get; set; }
    public string Title { get; set; } = "";
    public int Level { get; set; }
    public decimal Bar { get; set; }
    public int WindowDays { get; set; }
    public decimal Consistency { get; set; }
    public int SuccessfulDays { get; set; }
    public int RequiredDays { get; set; }
    public int DaysNeeded { get; set; }
    public int DaysOld { get; set; }
    public bool LevelReached { get; set; }
    public bool OldEnough { get; set; }
    public bool BarMet { get; set; }
}

/// <summary>
/// Current performance, which does drop: today's base XP against the success rule
/// (<see cref="Target"/> is its number when it has one), consistency over the last 30
/// complete days, the multiplier it sets, and the next tier with the days it still needs.
/// </summary>
public sealed class DomainCurrent
{
    public long TodayBase { get; set; }
    public bool TodaySucceeded { get; set; }
    /// <summary>The success rule in words, e.g. "at least 70 base XP".</summary>
    public string Rule { get; set; } = "";
    public long? Target { get; set; }
    public decimal Consistency { get; set; }
    public int SuccessfulDays { get; set; }
    public int WindowDays { get; set; }
    public decimal Multiplier { get; set; }
    public DomainTierRow? NextTier { get; set; }
}

/// <summary>A multiplier tier above now: the share it takes, what it pays, and the successful days still needed.</summary>
public sealed class DomainTierRow
{
    public decimal MinShare { get; set; }
    public decimal Multiplier { get; set; }
    public int DaysNeeded { get; set; }
}

/// <summary>One rung of the rank ladder: its gate, its Gems, and the day it qualified when it has.</summary>
public sealed class DomainRankRow
{
    public int Rank { get; set; }
    public string Title { get; set; } = "";
    public int Level { get; set; }
    public int TopLevel { get; set; }
    public decimal Bar { get; set; }
    public int WindowDays { get; set; }
    public int Gems { get; set; }
    public string? QualifiedOn { get; set; }
}

/// <summary>
/// <c>GET /api/domains/{key}/days</c> (xp-domains plan §3.11): one domain's last days, oldest
/// first and ending today (<c>yyyy-MM-dd</c>, home zone) — the Character page's daily chart.
/// <see cref="Target"/> is the success rule's number for a configured domain (70 for Health).
/// </summary>
public sealed class DomainDaysRead
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Configured { get; set; }
    public string Today { get; set; } = "";
    public long? Target { get; set; }
    public IReadOnlyList<DomainDayRow> Days { get; set; } = [];
}

/// <summary>
/// One day of a domain: base XP (before any multiplier), paid XP, and whether the success rule
/// passed — null for a domain without rules.
/// </summary>
public sealed class DomainDayRow
{
    public string Day { get; set; } = "";
    public long Base { get; set; }
    public long Paid { get; set; }
    public bool? Succeeded { get; set; }
}

/// <summary>
/// <c>GET /api/domains/report?days=N</c> (xp-domains plan §3.11, §3.13): every life domain over
/// its last <see cref="Days"/> days, as of <see cref="Today"/> (<c>yyyy-MM-dd</c>, home zone) —
/// what the Character page's reports plot.
/// </summary>
public sealed class DomainReportRead
{
    public string Today { get; set; } = "";
    public int Days { get; set; }
    public IReadOnlyList<DomainReportRow> Domains { get; set; } = [];
}

/// <summary>
/// One life domain over a report's period: the days it covers (<see cref="From"/>–<see cref="To"/>;
/// a configured domain's end yesterday until today succeeds) and the XP credited to them. A
/// configured domain adds the days counted (those on or after its start), the successful days,
/// consistency (0–1) and base XP a day over the days counted, and the level and rank (indexes,
/// 0 is Novice) the period started from against now, with each rank reached in it. Those are
/// null for a domain without rules.
/// </summary>
public sealed class DomainReportRow
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Configured { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public long Base { get; set; }
    public long Paid { get; set; }
    public int? DaysCounted { get; set; }
    public int? SuccessfulDays { get; set; }
    public decimal? Consistency { get; set; }
    public decimal? AverageBase { get; set; }
    public int? LevelFrom { get; set; }
    public int? LevelTo { get; set; }
    public int? RankFrom { get; set; }
    public int? RankTo { get; set; }
    /// <summary>The rank held now, e.g. "Apprentice".</summary>
    public string? RankTitle { get; set; }
    public IReadOnlyList<DomainRankReachedRow>? RanksReached { get; set; }
}

/// <summary>A rank a domain entered in a report's period, and the day it qualified.</summary>
public sealed class DomainRankReachedRow
{
    public int Rank { get; set; }
    public string Title { get; set; } = "";
    public string QualifiedOn { get; set; } = "";
}

/// <summary>Maps the Application's domain readings onto the API's shapes.</summary>
public static class DomainRows
{
    /// <summary>The longest series a days read answers: two years.</summary>
    public const int MaxDays = 731;

    public static DomainDaysRead From(DomainSeries series)
    {
        var domain = series.Domain;
        var rules = domain.Progress?.Rules;
        return new DomainDaysRead
        {
            Key = domain.Currency.Key,
            Label = domain.Currency.Label,
            Name = LifeDomains.Name(domain.Currency.Key),
            Configured = domain.Configured,
            Today = series.Days.Count > 0 ? Day(series.Days[^1].Day) : "",
            Target = rules?.Success is MinimumBaseXp minimum ? minimum.Minimum : null,
            Days = series.Days.Select(d => new DomainDayRow
            {
                Day = Day(d.Day),
                Base = d.Base,
                Paid = d.Paid,
                Succeeded = d.Succeeded
            }).ToList()
        };
    }

    public static DomainReportRead From(DomainReportReading report) => new()
    {
        Today = Day(report.Today),
        Days = report.Days,
        Domains = report.Domains.Select(From).ToList()
    };

    public static DomainReportRow From(DomainReportEntry entry)
    {
        var currency = entry.Domain.Currency;
        var row = new DomainReportRow
        {
            Key = currency.Key,
            Label = currency.Label,
            Name = LifeDomains.Name(currency.Key),
            Configured = entry.Domain.Configured,
            From = Day(entry.From),
            To = Day(entry.To),
            Base = entry.Base,
            Paid = entry.Paid
        };
        if (entry.Period is not { } period || entry.Domain.Progress is not { } progress)
        {
            return row;
        }

        row.DaysCounted = period.DaysCounted;
        row.SuccessfulDays = period.SuccessfulDays;
        row.Consistency = Math.Round(period.Consistency, 4);
        row.AverageBase = Math.Round(period.AverageBase, 1, MidpointRounding.AwayFromZero);
        row.LevelFrom = period.Before.Level;
        row.LevelTo = period.Now.Level;
        row.RankFrom = period.Before.Rank;
        row.RankTo = period.Now.Rank;
        row.RankTitle = progress.Rules.Ranks[period.Now.Rank].Title;
        row.RanksReached = period.RanksReached
            .Select(r => new DomainRankReachedRow { Rank = r.Rank, Title = r.Rule.Title, QualifiedOn = Day(r.QualifiedOn) })
            .ToList();
        return row;
    }

    public static DomainsRead From(DomainsReading reading) => new()
    {
        Today = Day(reading.Today),
        Domains = reading.Domains.Select(From).ToList()
    };

    public static DomainRow From(DomainReading reading)
    {
        var row = new DomainRow
        {
            Key = reading.Currency.Key,
            Label = reading.Currency.Label,
            Name = LifeDomains.Name(reading.Currency.Key),
            TotalXp = reading.TotalXp,
            Configured = reading.Configured
        };
        if (reading.Progress is not { } p)
        {
            return row;
        }

        var rules = p.Rules;
        row.Lifetime = new DomainLifetime
        {
            Start = p.Start is { } start ? Day(start) : null,
            Level = p.Level,
            RawLevel = p.RawLevel,
            TopLevel = p.TopLevel,
            Capped = p.Capped,
            Rank = p.Rank,
            RankTitle = p.RankRule.Title,
            QualifiedOn = p.RankQualifiedOn is { } on ? Day(on) : null,
            LevelXp = rules.Curve.CostToNext(p.Level),
            XpIntoLevel = p.XpIntoLevel,
            XpToNext = p.XpToNext,
            BankedXp = p.BankedXp
        };
        if (p.Next is { } next)
        {
            row.NextRank = new DomainNextRank
            {
                Rank = next.Rank,
                Title = next.Rule.Title,
                Level = next.Rule.StartLevel,
                Bar = next.Rule.MinConsistency,
                WindowDays = next.Rule.WindowDays,
                Consistency = Math.Round(next.Window.Share, 4),
                SuccessfulDays = next.Window.SuccessfulDays,
                RequiredDays = next.Required,
                DaysNeeded = next.DaysNeeded,
                DaysOld = next.DaysOld,
                LevelReached = next.LevelReached,
                OldEnough = next.OldEnough,
                BarMet = next.BarMet
            };
        }

        row.Current = new DomainCurrent
        {
            TodayBase = p.TodayBase,
            TodaySucceeded = p.TodaySucceeded,
            Rule = rules.Success.Describe(),
            Target = rules.Success is MinimumBaseXp minimum ? minimum.Minimum : null,
            Consistency = Math.Round(p.Consistency.Share, 4),
            SuccessfulDays = p.Consistency.SuccessfulDays,
            WindowDays = p.Consistency.Days,
            Multiplier = p.MultiplierNow,
            NextTier = p.NextTier is { } tier
                ? new DomainTierRow
                {
                    MinShare = tier.MinShare,
                    Multiplier = tier.Multiplier,
                    DaysNeeded = p.Consistency.DaysNeededFor(tier.MinShare)
                }
                : null
        };
        var qualified = p.RanksReached.ToDictionary(r => r.Rank, r => r.QualifiedOn);
        row.Ranks = rules.Ranks.Select((rank, i) => new DomainRankRow
        {
            Rank = i,
            Title = rank.Title,
            Level = rank.StartLevel,
            TopLevel = rules.TopLevelOf(i),
            Bar = rank.MinConsistency,
            WindowDays = rank.WindowDays,
            Gems = i == 0 ? 0 : rules.GemsFor(i),
            QualifiedOn = qualified.TryGetValue(i, out var day) ? Day(day) : null
        }).ToList();
        return row;
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

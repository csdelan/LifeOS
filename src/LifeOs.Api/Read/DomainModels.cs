using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// <c>GET /api/domains</c> (REWARD-6 / REWARD-7 / REWARD-8, xp-domains plan §7.4.8): every life
/// domain in display order, as of <see cref="Today"/> (<c>yyyy-MM-dd</c>) in the home zone.
/// Dates are home-zone days, not the viewer's.
/// </summary>
public sealed class DomainsRead
{
    public string Today { get; set; } = "";
    public IReadOnlyList<DomainRow> Domains { get; set; } = [];
}

/// <summary>
/// One life domain: its currency, the XP it has collected, and whether it has rules. Only a
/// configured domain (Health in the pilot) has the rest: <see cref="Lifetime"/>,
/// <see cref="Consistency"/>, <see cref="Shown"/>, <see cref="Best"/>, <see cref="Current"/>
/// and the rank ladder.
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
    public DomainConsistency? Consistency { get; set; }
    public DomainShown? Shown { get; set; }
    public DomainBest? Best { get; set; }
    public DomainCurrent? Current { get; set; }
    public IReadOnlyList<DomainRankRow>? Ranks { get; set; }
}

/// <summary>
/// The lifetime level, which never falls (§7.1): the level lifetime XP reaches, the ceiling the
/// shown level can't pass, with what the level costs, the XP into it and still to go.
/// </summary>
public sealed class DomainLifetime
{
    /// <summary>The first day with paid XP, or null before there is any.</summary>
    public string? Start { get; set; }
    public int Level { get; set; }
    /// <summary>What the current level costs in all; 0 at the top.</summary>
    public long LevelXp { get; set; }
    public long XpIntoLevel { get; set; }
    public long XpToNext { get; set; }
}

/// <summary>
/// The consistency level (§7.2): the highest level the last <see cref="WindowDays"/> counted days
/// (at most 90, <see cref="From"/>–<see cref="To"/>) support, their base XP a day, and what the
/// next level needs. <see cref="Next"/> is null at the top.
/// </summary>
public sealed class DomainConsistency
{
    public int Level { get; set; }
    public int WindowDays { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public decimal AverageBase { get; set; }
    public DomainConsistencyNeed? Next { get; set; }
}

/// <summary>
/// What a consistency level needs over the window, each test against where it stands: a base XP
/// average, active days (action XP at the level's minimum) in the window, and active days so far.
/// </summary>
public sealed class DomainConsistencyNeed
{
    public int Level { get; set; }
    public int WindowDays { get; set; }
    public int Average { get; set; }
    public decimal AverageNow { get; set; }
    public bool AverageMet { get; set; }
    public int ActiveMinimum { get; set; }
    public int ActiveDaysNeeded { get; set; }
    public int ActiveDays { get; set; }
    public bool ActiveDaysMet { get; set; }
    public int AgeNeeded { get; set; }
    public int Age { get; set; }
    public bool AgeMet { get; set; }
}

/// <summary>
/// The shown level (§7.2): it follows <see cref="Target"/> (the lower of the lifetime and
/// consistency levels), rising at once and falling <see cref="PercentPerDay"/> of itself a day.
/// <see cref="Exact"/> is it to two places. <see cref="LimitedBy"/> is <c>lifetime</c> or
/// <c>consistency</c>, whichever sets the target, or null when they're equal. While it's above
/// the target it's <see cref="Falling"/> or <see cref="Paused"/>, since <see cref="FallingSince"/>
/// from <see cref="FallingFrom"/>, and <see cref="BackOnTrack"/> says what pauses the fall.
/// </summary>
public sealed class DomainShown
{
    public int Level { get; set; }
    public decimal Exact { get; set; }
    public int Target { get; set; }
    /// <summary>The rank the level is in: 0 is Novice.</summary>
    public int Rank { get; set; }
    public string RankTitle { get; set; } = "";
    public string? LimitedBy { get; set; }
    public bool Falling { get; set; }
    public bool Paused { get; set; }
    public string? FallingSince { get; set; }
    public int? FallingFrom { get; set; }
    public decimal PercentPerDay { get; set; }
    public DomainLevelChange? LastChange { get; set; }
    public DomainBackOnTrack? BackOnTrack { get; set; }
}

/// <summary>The last day the shown level moved, and from what to what.</summary>
public sealed class DomainLevelChange
{
    public string Day { get; set; } = "";
    public int From { get; set; }
    public int To { get; set; }
}

/// <summary>
/// What pauses a fall: the last <see cref="Days"/> counted days averaging <see cref="Average"/>
/// base XP, with <see cref="ActiveDays"/> of them active (<see cref="ActiveMinimum"/> action XP).
/// </summary>
public sealed class DomainBackOnTrack
{
    public int Days { get; set; }
    public int Average { get; set; }
    public int ActiveMinimum { get; set; }
    public int ActiveDays { get; set; }
}

/// <summary>The best level and best rank ever reached, and the first day each was.</summary>
public sealed class DomainBest
{
    public int Level { get; set; }
    public string? LevelOn { get; set; }
    public int Rank { get; set; }
    public string RankTitle { get; set; } = "";
    /// <summary>The first day the best rank reached <see cref="Rank"/>; null for Novice.</summary>
    public string? RankOn { get; set; }
}

/// <summary>
/// Today: base and action XP against the active minimum (the shown level's, going into today),
/// whether today is active or an exception day, the multiplier's 30 non-exception days before
/// today, the multiplier it sets, the next tier, and the last settled day.
/// </summary>
public sealed class DomainCurrent
{
    public long TodayBase { get; set; }
    public long TodayAction { get; set; }
    public int ActiveMinimum { get; set; }
    public bool TodayActive { get; set; }
    public bool TodayException { get; set; }
    /// <summary>The active day in words, e.g. "at least 17 action XP (base XP less sleep)".</summary>
    public string Rule { get; set; } = "";
    public decimal ActiveShare { get; set; }
    public int ActiveDays { get; set; }
    public int WindowDays { get; set; }
    public decimal Multiplier { get; set; }
    public DomainTierRow? NextTier { get; set; }
    public string SettledThrough { get; set; } = "";
}

/// <summary>A multiplier tier above now: the share it takes, what it pays, and the active days still needed.</summary>
public sealed class DomainTierRow
{
    public decimal MinShare { get; set; }
    public decimal Multiplier { get; set; }
    public int DaysNeeded { get; set; }
}

/// <summary>
/// One rung of the rank ladder: its levels, what holding its first level takes over a full
/// window (null for the first rank, which needs nothing), the total XP to reach it, its Gems,
/// and the first day the best rank reached it.
/// </summary>
public sealed class DomainRankRow
{
    public int Rank { get; set; }
    public string Title { get; set; } = "";
    public int Level { get; set; }
    public int LastLevel { get; set; }
    public int? Average { get; set; }
    public int? ActiveMinimum { get; set; }
    public int? ActiveDays { get; set; }
    public int WindowDays { get; set; }
    public long TotalXp { get; set; }
    public int Gems { get; set; }
    public string? ReachedOn { get; set; }
}

/// <summary>
/// <c>GET /api/domains/{key}/days</c> (xp-domains plan §7.4.8): one domain's last days, oldest
/// first and ending today (<c>yyyy-MM-dd</c>, home zone) — the Character page's daily chart.
/// <see cref="SettledThrough"/> is the last settled day, for a configured domain.
/// </summary>
public sealed class DomainDaysRead
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Configured { get; set; }
    public string Today { get; set; } = "";
    public string? SettledThrough { get; set; }
    public IReadOnlyList<DomainDayRow> Days { get; set; } = [];
}

/// <summary>
/// One day of a domain: base XP (before any multiplier), its action and passive (sleep) parts,
/// and paid XP. A configured domain adds the exception comment (null on an unmarked day), whether
/// the day counted and was active, the active minimum that day, and the lifetime, consistency and
/// shown levels and rank at its end; those are null for a domain without rules.
/// </summary>
public sealed class DomainDayRow
{
    public string Day { get; set; } = "";
    public long Base { get; set; }
    public long Action { get; set; }
    public long Passive { get; set; }
    public long Paid { get; set; }
    public string? Exception { get; set; }
    public bool? Counted { get; set; }
    public bool? Active { get; set; }
    public int? ActiveMinimum { get; set; }
    public int? LifetimeLevel { get; set; }
    public int? ConsistencyLevel { get; set; }
    public int? Level { get; set; }
    public int? Rank { get; set; }
}

/// <summary>
/// <c>GET /api/domains/report?days=N</c> (xp-domains plan §3.13, §7.4.8): every life domain over
/// its last <see cref="Days"/> days, as of <see cref="Today"/> (<c>yyyy-MM-dd</c>, home zone) —
/// what the reports plot.
/// </summary>
public sealed class DomainReportRead
{
    public string Today { get; set; } = "";
    public int Days { get; set; }
    public IReadOnlyList<DomainReportRow> Domains { get; set; } = [];
}

/// <summary>
/// One life domain over a report's period: the days it covers (<see cref="From"/>–<see cref="To"/>;
/// a configured domain's end at the last settled day) and the XP credited to them. A configured
/// domain adds the days counted, exception days, active days and their share, base XP a counted
/// day, the level and rank (indexes, 0 is Novice) at the start against the end, the bests, each
/// rank the best rank first reached in the period, and its rank each day (<see cref="Ranks"/>).
/// Those are null for a domain without rules.
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
    public int? ExceptionDays { get; set; }
    public int? ActiveDays { get; set; }
    public decimal? ActiveShare { get; set; }
    public decimal? AverageBase { get; set; }
    public int? LevelFrom { get; set; }
    public int? LevelTo { get; set; }
    public int? RankFrom { get; set; }
    public int? RankTo { get; set; }
    /// <summary>The rank at the end of the period, e.g. "Apprentice".</summary>
    public string? RankTitle { get; set; }
    public int? BestLevelFrom { get; set; }
    public int? BestLevelTo { get; set; }
    public int? BestRank { get; set; }
    public string? BestRankTitle { get; set; }
    public IReadOnlyList<DomainRankReachedRow>? RanksReached { get; set; }
    public IReadOnlyList<DomainReportDayRow>? Ranks { get; set; }
}

/// <summary>A rank the best rank first reached in a report's period, and the day it did.</summary>
public sealed class DomainRankReachedRow
{
    public int Rank { get; set; }
    public string Title { get; set; } = "";
    public string QualifiedOn { get; set; } = "";
}

/// <summary>One day of a report's period: the shown level and rank at its end, the best rank, and whether it was an exception day.</summary>
public sealed class DomainReportDayRow
{
    public string Day { get; set; } = "";
    public int Level { get; set; }
    public int Rank { get; set; }
    public int BestRank { get; set; }
    public bool Exception { get; set; }
}

/// <summary>
/// <c>GET /api/domains/exceptions?days=N</c> (REWARD-8, xp-domains plan §7.4.3): the days Chris
/// marked as exceptions from <see cref="From"/> to <see cref="Today"/> (<c>yyyy-MM-dd</c>, home
/// zone), newest first.
/// </summary>
public sealed class ExceptionDaysRead
{
    public string Today { get; set; } = "";
    public string From { get; set; } = "";
    public IReadOnlyList<ExceptionDayRow> Exceptions { get; set; } = [];
}

/// <summary>One marked day: the day (<c>yyyy-MM-dd</c>, home zone), why, and when it was last marked.</summary>
public sealed class ExceptionDayRow
{
    public string Day { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTimeOffset MarkedAt { get; set; }
}

/// <summary>
/// <c>POST /api/domains/exceptions</c> and <c>…/{day}/unmark</c>: the day as it now reads (null
/// once unmarked), any rank-up the change paid, and each life domain whose level, rank or bests
/// it moved — as an earning write answers.
/// </summary>
public sealed class ExceptionDayResult
{
    public ExceptionDayRow? Exception { get; set; }
    public IReadOnlyList<AwardRow> Awards { get; set; } = [];
    public IReadOnlyList<DomainChangeRow> Progress { get; set; } = [];
}

/// <summary>Maps the Application's domain readings onto the API's shapes.</summary>
public static class DomainRows
{
    /// <summary>The longest series a days read answers: two years.</summary>
    public const int MaxDays = 731;

    public static ExceptionDaysRead From(ExceptionDays list) => new()
    {
        Today = Day(list.Today),
        From = Day(list.Today.AddDays(1 - list.Days)),
        Exceptions = list.Marked.Select(From).ToList()
    };

    public static ExceptionDayRow From(ExceptionDay day) => new()
    {
        Day = Day(day.Day),
        Comment = day.Comment,
        MarkedAt = day.MarkedAt
    };

    public static ExceptionDayResult From(ExceptionDayChange change)
    {
        var earned = RewardRows.Earned(change.Earned.Awards, change.Earned.Progress);
        return new ExceptionDayResult
        {
            Exception = change.Exception is { } day ? From(day) : null,
            Awards = earned.Awards,
            Progress = earned.Progress
        };
    }

    public static DomainDaysRead From(DomainSeries series)
    {
        var domain = series.Domain;
        return new DomainDaysRead
        {
            Key = domain.Currency.Key,
            Label = domain.Currency.Label,
            Name = LifeDomains.Name(domain.Currency.Key),
            Configured = domain.Configured,
            Today = series.Days.Count > 0 ? Day(series.Days[^1].Day) : "",
            SettledThrough = domain.Progress is { } p ? Day(p.SettledThrough) : null,
            Days = series.Days.Select(d => new DomainDayRow
            {
                Day = Day(d.Day),
                Base = d.Base,
                Action = d.Action,
                Passive = d.Passive,
                Paid = d.Paid,
                Exception = d.Exception,
                Counted = d.Counted,
                Active = d.Active,
                ActiveMinimum = d.Standing?.ActiveMinimum,
                LifetimeLevel = d.Standing?.LifetimeLevel,
                ConsistencyLevel = d.Standing?.ConsistencyLevel,
                Level = d.Standing?.Level,
                Rank = d.Standing?.Rank
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

        var ranks = progress.Rules.Ranks;
        row.DaysCounted = period.DaysCounted;
        row.ExceptionDays = period.ExceptionDays;
        row.ActiveDays = period.ActiveDays;
        row.ActiveShare = Math.Round(period.ActiveShare, 4);
        row.AverageBase = Math.Round(period.AverageBase, 1, MidpointRounding.AwayFromZero);
        row.LevelFrom = period.Before.Level;
        row.LevelTo = period.End.Level;
        row.RankFrom = period.Before.Rank;
        row.RankTo = period.End.Rank;
        row.RankTitle = ranks[period.End.Rank].Title;
        row.BestLevelFrom = period.Before.BestLevel;
        row.BestLevelTo = period.End.BestLevel;
        row.BestRank = period.BestRank;
        row.BestRankTitle = ranks[period.BestRank].Title;
        row.RanksReached = period.RanksReached
            .Select(r => new DomainRankReachedRow { Rank = r.Rank, Title = r.Rule.Title, QualifiedOn = Day(r.QualifiedOn) })
            .ToList();
        row.Ranks = period.Standings
            .Select(s => new DomainReportDayRow
            {
                Day = Day(s.Day),
                Level = s.Level,
                Rank = s.Rank,
                BestRank = s.BestRank,
                Exception = s.Exception
            })
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
        var consistency = rules.Consistency;
        row.Lifetime = new DomainLifetime
        {
            Start = p.Start is { } start ? Day(start) : null,
            Level = p.LifetimeLevel,
            LevelXp = rules.Curve.CostToNext(p.LifetimeLevel),
            XpIntoLevel = p.XpIntoLevel,
            XpToNext = p.XpToNext
        };
        row.Consistency = new DomainConsistency
        {
            Level = p.ConsistencyLevel,
            WindowDays = p.Window.Days,
            From = p.Window.From is { } from ? Day(from) : null,
            To = p.Window.To is { } to ? Day(to) : null,
            AverageBase = Math.Round(p.Window.Average, 1, MidpointRounding.AwayFromZero),
            Next = p.NextConsistency is { } next
                ? new DomainConsistencyNeed
                {
                    Level = next.Level,
                    WindowDays = next.WindowDays,
                    Average = next.Average,
                    AverageNow = Math.Round(next.AverageNow, 1, MidpointRounding.AwayFromZero),
                    AverageMet = next.AverageMet,
                    ActiveMinimum = next.ActiveMinimum,
                    ActiveDaysNeeded = next.ActiveDaysNeeded,
                    ActiveDays = next.ActiveDaysNow,
                    ActiveDaysMet = next.ActiveDaysMet,
                    AgeNeeded = next.AgeNeeded,
                    Age = next.AgeNow,
                    AgeMet = next.AgeMet
                }
                : null
        };
        var backOnTrackDays = rules.Fall.BackOnTrackDays;
        row.Shown = new DomainShown
        {
            Level = p.Level,
            Exact = Math.Round((decimal)p.ShownExact / FallRule.Scale, 2, MidpointRounding.AwayFromZero),
            Target = p.Target,
            Rank = p.Rank,
            RankTitle = p.RankRule.Title,
            LimitedBy = p.LimitedBy switch
            {
                LevelLimit.Lifetime => "lifetime",
                LevelLimit.Consistency => "consistency",
                _ => null
            },
            Falling = p.Falling,
            Paused = p.Paused,
            FallingSince = p.FallingSince is { } since ? Day(since) : null,
            FallingFrom = p.FallingFrom,
            PercentPerDay = rules.Fall.PercentPerDay,
            LastChange = p.LastChange is { } change
                ? new DomainLevelChange { Day = Day(change.Day), From = change.From, To = change.To }
                : null,
            BackOnTrack = p.Level > p.Target
                ? new DomainBackOnTrack
                {
                    Days = backOnTrackDays,
                    Average = consistency.Average(p.Level),
                    ActiveMinimum = consistency.ActiveMinimum(p.Level),
                    ActiveDays = consistency.ActiveDaysNeeded(p.Level, backOnTrackDays)
                }
                : null
        };
        var reached = p.RanksReached.ToDictionary(r => r.Rank, r => r.QualifiedOn);
        row.Best = new DomainBest
        {
            Level = p.BestLevel,
            LevelOn = p.BestLevelOn is { } levelOn ? Day(levelOn) : null,
            Rank = p.BestRank,
            RankTitle = p.BestRankRule.Title,
            RankOn = reached.TryGetValue(p.BestRank, out var rankOn) ? Day(rankOn) : null
        };
        row.Current = new DomainCurrent
        {
            TodayBase = p.TodayBase,
            TodayAction = p.TodayAction,
            ActiveMinimum = p.ActiveMinimum,
            TodayActive = p.TodayActive,
            TodayException = p.TodayException,
            Rule = rules.Success.Describe(p.Level),
            ActiveShare = Math.Round(p.Consistency.Share, 4),
            ActiveDays = p.Consistency.SuccessfulDays,
            WindowDays = p.Consistency.Days,
            Multiplier = p.MultiplierNow,
            NextTier = p.NextTier is { } tier
                ? new DomainTierRow
                {
                    MinShare = tier.MinShare,
                    Multiplier = tier.Multiplier,
                    DaysNeeded = p.Consistency.DaysNeededFor(tier.MinShare)
                }
                : null,
            SettledThrough = Day(p.SettledThrough)
        };
        row.Ranks = rules.Ranks.Select((rank, i) => new DomainRankRow
        {
            Rank = i,
            Title = rank.Title,
            Level = rank.StartLevel,
            LastLevel = i + 1 < rules.Ranks.Count ? rules.Ranks[i + 1].StartLevel - 1 : rules.Curve.MaxLevel,
            Average = i == 0 ? null : consistency.Average(rank.StartLevel),
            ActiveMinimum = i == 0 ? null : consistency.ActiveMinimum(rank.StartLevel),
            ActiveDays = i == 0 ? null : consistency.ActiveDaysNeeded(rank.StartLevel, consistency.WindowDays),
            WindowDays = consistency.WindowDays,
            TotalXp = rules.Curve.TotalFor(rank.StartLevel),
            Gems = i == 0 ? 0 : rules.GemsFor(i),
            ReachedOn = reached.TryGetValue(i, out var day) ? Day(day) : null
        }).ToList();
        return row;
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

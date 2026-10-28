using System.ComponentModel;
using LifeOs.Api.Read;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeOs.Api.Mcp.Tools;

/// <summary>A range of days an area tool answers: both ends included, today by default.</summary>
internal static class AreaRange
{
    /// <summary>The longest range an area tool answers.</summary>
    public const int MaxDays = 92;

    public static (DateOnly From, DateOnly To) Parse(string? from, string? to, DateOnly today, int defaultDays)
    {
        var end = McpResults.ParseDay(to, "to") ?? today;
        var start = McpResults.ParseDay(from, "from") ?? end.AddDays(1 - defaultDays);
        if (end < start)
        {
            throw new McpException($"to ({McpResults.Day(end)}) is before from ({McpResults.Day(start)}).");
        }

        return end.DayNumber - start.DayNumber >= MaxDays
            ? throw new McpException($"That's more than {MaxDays} days: ask for a shorter range.")
            : (start, end);
    }
}

/// <summary>Oura's days, workouts and tags (the <c>health</c> area; Chris accepted the risk, plan §2 decision 3).</summary>
[McpServerToolType]
public sealed class HealthTools(ActivityReader activity, McpReads reads)
{
    [McpServerTool(Name = "health", Title = "Health", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("From Chris's Oura ring, by day: sleep, readiness and activity scores, steps against his goal, workouts, and the tags he logged.")]
    public async Task<CallToolResult> Health(
        [Description("The first day (yyyy-MM-dd). Default 6 days before the last.")] string? from = null,
        [Description("The last day (yyyy-MM-dd). Default today.")] string? to = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var (start, end) = AreaRange.Parse(from, to, stamp.Today, 7);
        var days = await activity.GetDaysAsync(start, end, cancellationToken);
        var workouts = await activity.GetWorkoutsAsync(start, end, cancellationToken);
        var tags = await activity.GetTagsAsync(start, end, cancellationToken);
        return McpResults.Ok(stamp,
            $"Oura, {McpResults.Day(start)} to {McpResults.Day(end)}: {McpResults.Plural(days.Days.Count, "day")}, {McpResults.Plural(workouts.Workouts.Count, "workout")}.",
            new
            {
                From = McpResults.Day(start), To = McpResults.Day(end),
                days.MinimumSteps, days.GoalSteps, days.Days, workouts.Workouts, tags.Tags,
            });
    }
}

/// <summary>Meals, fasts, drinks and diet XP (the <c>food</c> area).</summary>
[McpServerToolType]
public sealed class FoodTools(FoodReader food, McpReads reads)
{
    /// <summary>The longest range whose meals come one by one; a longer one has the day totals only.</summary>
    public const int MealDays = 14;

    [McpServerTool(Name = "food", Title = "Food", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("Chris's food log by day: meals with their scores, fasts, drinks and the diet XP each day earned. Over 14 days, the day totals only.")]
    public async Task<CallToolResult> Food(
        [Description("The first day (yyyy-MM-dd). Default 6 days before the last.")] string? from = null,
        [Description("The last day (yyyy-MM-dd). Default today.")] string? to = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var (start, end) = AreaRange.Parse(from, to, stamp.Today, 7);
        var days = await food.GetDaysAsync(start, end, cancellationToken);
        var drinks = await food.GetDrinksAsync(McpResults.Day(start), McpResults.Day(end), cancellationToken);
        List<object>? meals = null;
        if (end.DayNumber - start.DayNumber < MealDays)
        {
            meals = [];
            for (var day = start; day <= end; day = day.AddDays(1))
            {
                var read = await food.GetDayAsync(day, cancellationToken);
                meals.Add(new { read.Day, read.Meals, read.Fasts });
            }
        }

        return McpResults.Ok(stamp,
            $"Food, {McpResults.Day(start)} to {McpResults.Day(end)}: {McpResults.Plural(drinks.Drinks, "drink")}.",
            new
            {
                From = McpResults.Day(start), To = McpResults.Day(end),
                days.Days, days.Sobriety, Drinks = drinks, MealsByDay = meals,
            });
    }
}

/// <summary>The wallet, the ledger by day, domain levels and the domain report (the <c>rewards</c> area).</summary>
[McpServerToolType]
public sealed class RewardsTools(RewardReader rewards, McpReads reads)
{
    [McpServerTool(Name = "rewards", Title = "Rewards", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        Chris's rewards: wallet balances, what he earned, had reversed and spent each day with
        the day's net per currency (read this rather than ledger entries in recent_changes), the
        reward catalog, each life domain's level and rank (XP), and the domain report over the
        days given.
        """)]
    public async Task<CallToolResult> Rewards(
        [Description("How many days back the daily ledger and the report cover, with today. Default 7, at most 92.")] int? days = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var span = Math.Clamp(days ?? 7, 1, AreaRange.MaxDays);
        var wallet = await rewards.GetWalletAsync(cancellationToken);
        var daily = await rewards.GetDailyAsync(stamp.Today.AddDays(1 - span), stamp.Today, reads.HomeTimeZone, cancellationToken);
        var catalog = await rewards.GetRewardsAsync(false, cancellationToken);
        var domains = await rewards.GetDomainsAsync(cancellationToken);
        var report = await rewards.GetDomainReportAsync(span, cancellationToken);
        return McpResults.Ok(stamp, $"Rewards over the last {McpResults.Plural(span, "day")}.",
            new { Wallet = wallet, Daily = daily, Catalog = catalog, domains.Domains, Report = report });
    }
}

/// <summary>Spaces, ratings, the Home Score and home XP (the <c>home</c> area).</summary>
[McpServerToolType]
public sealed class HomeTools(EnvironmentReader environment, McpReads reads)
{
    [McpServerTool(Name = "home", Title = "Home", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        Chris's home: each space's latest ratings (Tidiness, Cleanliness, Aesthetics) and how
        they've faded, the Home Score, what to fix next, and the score by day. Each space lists
        the spots it's photographed from (shots): how many photos, and the day and space score of
        Then (the baseline photo) and Now (the newest, when it isn't the baseline).
        """)]
    public async Task<CallToolResult> Home(
        [Description("How many days back the score by day goes, with today. Default 14, at most 92.")] int? days = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var span = Math.Clamp(days ?? 14, 1, AreaRange.MaxDays);
        var home = await environment.GetHomeAsync(cancellationToken);
        var history = await environment.GetHomeDaysAsync(span, cancellationToken);
        return McpResults.Ok(stamp,
            $"Home Score {(home.HomeScore is { } score ? score.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "not set yet")}, {McpResults.Plural(home.Spaces.Count, "space")}.",
            new { home.HomeScore, home.SettledThrough, Spaces = Brief(home.Spaces), home.FixNext, ByDay = history.Days });
    }

    /// <summary>
    /// The spaces as an agent reads them: the web's shape, but each shot only by its name, its
    /// photo count, and the day and score of Then and Now. The photos' ids, artifacts, space and
    /// parts would repeat what the space already says.
    /// </summary>
    public static IReadOnlyList<HomeSpaceBrief> Brief(IReadOnlyList<HomeSpaceRow> spaces)
        => spaces.Select(space => new HomeSpaceBrief(
            space.Id, space.Name, space.Weight, space.Pace, space.Share, space.Score, space.LastRatedOn, space.Parts,
            space.Shots.Select(shot => new HomeShotBrief(
                shot.Name,
                shot.Photos,
                Moment(shot.Baseline),
                shot.Newest is { } newest && newest.Id != shot.Baseline?.Id ? Moment(newest) : null)).ToList())).ToList();

    private static HomeShotMoment? Moment(SpacePhotoRow? photo) => photo is null ? null : new(photo.Day, photo.Score);
}

/// <summary>One space in the <c>home</c> tool, with its shots in brief.</summary>
public sealed record HomeSpaceBrief(
    Guid Id, string Name, int Weight, int Pace, decimal? Share, decimal? Score, string? LastRatedOn,
    HomePartsRow Parts, IReadOnlyList<HomeShotBrief> Shots);

/// <summary>
/// One shot of a space: its name, how many photos it has, Then (its baseline) and Now (its
/// newest photo, null while that's the baseline). Both are null with no photos.
/// </summary>
public sealed record HomeShotBrief(string Name, int Photos, HomeShotMoment? Then, HomeShotMoment? Now);

/// <summary>A photo's day and the space's score that day (null while not all three parts were rated).</summary>
public sealed record HomeShotMoment(string Day, decimal? Score);

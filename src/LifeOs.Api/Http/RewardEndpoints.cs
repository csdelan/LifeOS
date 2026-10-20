using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Api.Http;

/// <summary>
/// The rewards surface (REWARD-1..5) — the HTTP twin of <c>bsk wallet</c>, <c>bsk ledger</c>,
/// <c>bsk redeem</c>, <c>bsk rewards</c>, <c>bsk bounty</c> and <c>bsk habit target</c>. Reads
/// go through <see cref="RewardReader"/> on the reader connection; writes call the same
/// Application services the CLI does. Renaming, describing and archiving a Reward are the
/// ordinary <c>/api/rename</c>, <c>/api/attributes</c> and <c>/api/archive</c>.
/// </summary>
public static class RewardEndpoints
{
    private const int DefaultLedgerLimit = 100;
    private const int MaxLedgerLimit = 500;

    public static void MapRewardEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .WithTags("Rewards")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/wallet", GetWallet).WithName("GetWallet")
            .Produces<WalletSummary>();
        api.MapGet("/ledger", GetLedger).WithName("GetLedger")
            .Produces<IReadOnlyList<LedgerRow>>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/rewards", GetRewards).WithName("GetRewards")
            .Produces<IReadOnlyList<RewardRow>>();
        api.MapPost("/redeem", Redeem).WithName("Redeem")
            .Produces<RedemptionResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/rewards", AddReward).WithName("AddReward")
            .Produces<RewardRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/rewards/price", PriceReward).WithName("PriceReward")
            .Produces<RewardRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/rewards/target", TargetReward).WithName("TargetReward")
            .Produces<RewardRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/bounty/defaults", GetBountyDefaults).WithName("GetBountyDefaults")
            .Produces<IReadOnlyList<BountyTypeDefaults>>();
        api.MapPost("/bounty", SetBounty).WithName("SetBounty")
            .Produces<BountyResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/habits/target", SetHabitTarget).WithName("SetHabitTarget")
            .Produces<HabitTargetResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> GetWallet(RewardReader rewards, CancellationToken cancellationToken)
        => Results.Ok(await rewards.GetWalletAsync(cancellationToken));

    private static async Task<IResult> GetLedger(
        RewardReader rewards, DateTimeOffset? from, DateTimeOffset? to, int? limit,
        CancellationToken cancellationToken)
        => Results.Ok(await rewards.GetLedgerAsync(
            from, to, Math.Clamp(limit ?? DefaultLedgerLimit, 1, MaxLedgerLimit), cancellationToken));

    private static async Task<IResult> GetRewards(
        RewardReader rewards, CancellationToken cancellationToken, bool includeArchived = false)
        => Results.Ok(await rewards.GetRewardsAsync(includeArchived, cancellationToken));

    private static async Task<IResult> Redeem(
        RedeemBody body,
        RewardService rewards,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        var reward = string.IsNullOrWhiteSpace(body.Reward)
            ? null
            : WriteEndpoints.ResolveWriteRef(body.Reward, reader);
        var redemption = await rewards.RedeemAsync(
            reward, body.Amounts is { Count: > 0 } amounts ? amounts : null, body.Note, cancellationToken);
        return Results.Ok(RewardRows.From(redemption));
    }

    private static async Task<IResult> AddReward(
        AddRewardBody body,
        RewardCatalogService catalog,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.BadRequest(new ApiError("title is required."));
        }

        var reward = await catalog.AddAsync(
            body.Title, body.Kind, body.Price, body.TargetUsd, body.Description, cancellationToken);
        return Results.Ok(RewardRows.From(reward));
    }

    private static async Task<IResult> PriceReward(
        RewardPriceBody body,
        RewardCatalogService catalog,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Reward) || body.Price is not { Count: > 0 })
        {
            return Results.BadRequest(new ApiError("reward and price are required."));
        }

        var reward = await catalog.SetPriceAsync(
            WriteEndpoints.ResolveWriteRef(body.Reward, reader), body.Price, cancellationToken);
        return Results.Ok(RewardRows.From(reward));
    }

    private static async Task<IResult> TargetReward(
        RewardTargetBody body,
        RewardCatalogService catalog,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Reward))
        {
            return Results.BadRequest(new ApiError("reward is required."));
        }

        var reward = await catalog.SetTargetAsync(
            WriteEndpoints.ResolveWriteRef(body.Reward, reader), body.TargetUsd, cancellationToken);
        return Results.Ok(RewardRows.From(reward));
    }

    private static IResult GetBountyDefaults() => Results.Ok(BountyRows.Defaults());

    private static async Task<IResult> SetBounty(
        BountyBody body,
        BountyService bounties,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject))
        {
            return Results.BadRequest(new ApiError("subject is required."));
        }

        var asks = (body.Amounts is { Count: > 0 } ? 1 : 0) + (body.Nothing ? 1 : 0) + (body.Clear ? 1 : 0);
        if (asks != 1)
        {
            return Results.BadRequest(new ApiError("Send one of amounts, nothing or clear."));
        }

        var subject = WriteEndpoints.ResolveWriteRef(body.Subject, reader);
        var view = body.Clear
            ? await bounties.ClearAsync(subject, body.Trigger, cancellationToken)
            : body.Nothing
                ? await bounties.SetNothingAsync(subject, body.Trigger, cancellationToken)
                : await bounties.SetAsync(subject, body.Trigger, body.Amounts!, cancellationToken);
        return Results.Ok(BountyRows.From(view));
    }

    private static async Task<IResult> SetHabitTarget(
        HabitTargetBody body,
        HabitTargetService targets,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Habit))
        {
            return Results.BadRequest(new ApiError("habit is required."));
        }

        var setting = body.MinAdherence is not null || body.WindowDays is not null;
        if (setting == body.Clear)
        {
            return Results.BadRequest(new ApiError("Send minAdherence and/or windowDays, or clear — one of them."));
        }

        var habit = WriteEndpoints.ResolveWriteRef(body.Habit, reader);
        var view = body.Clear
            ? await targets.ClearAsync(habit, cancellationToken)
            : await targets.SetAsync(habit, body.MinAdherence, body.WindowDays, cancellationToken);
        return Results.Ok(HabitTargetResult.From(view));
    }
}

public sealed class RedeemBody
{
    /// <summary>The catalog Reward (id, urn or title); omit for a custom redemption.</summary>
    public string? Reward { get; set; }

    /// <summary>
    /// What to spend, currency → amount. Omit to pay an instant reward's catalog price; a
    /// savings goal needs <c>{"coins": n}</c>; a custom redemption needs amounts and a note.
    /// </summary>
    public Dictionary<string, long>? Amounts { get; set; }

    /// <summary>What it was for — required without a Reward.</summary>
    public string? Note { get; set; }
}

public sealed class AddRewardBody
{
    public string Title { get; set; } = "";

    /// <summary><c>instant</c> (the default) or <c>savings</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>An instant reward's price, currency → amount.</summary>
    public Dictionary<string, long>? Price { get; set; }

    /// <summary>A savings goal's target in dollars.</summary>
    public decimal? TargetUsd { get; set; }

    public string? Description { get; set; }
}

public sealed class RewardPriceBody
{
    public string Reward { get; set; } = "";

    /// <summary>The new price, currency → amount; it replaces the old one.</summary>
    public Dictionary<string, long> Price { get; set; } = new();
}

/// <summary>
/// <c>POST /api/bounty</c> (<c>bsk bounty</c>): send one of <see cref="Amounts"/>,
/// <see cref="Nothing"/> or <see cref="Clear"/>.
/// </summary>
public sealed class BountyBody
{
    /// <summary>The Task, Project, Goal or Habit (id, urn or title).</summary>
    public string Subject { get; set; } = "";

    /// <summary>
    /// The trigger to price (<c>completed</c>, <c>followed</c>, <c>streak_7</c>, …); may be
    /// omitted when the type pays on one. With <see cref="Clear"/> and no trigger, every
    /// trigger goes back on its default.
    /// </summary>
    public string? Trigger { get; set; }

    /// <summary>
    /// Currencies to set on what the trigger pays now, currency → amount: <c>karma: 3</c>
    /// adds Karma, <c>xp: 0</c> drops XP. Name all six to set the whole price.
    /// </summary>
    public Dictionary<string, long>? Amounts { get; set; }

    /// <summary>The trigger pays nothing at all.</summary>
    public bool Nothing { get; set; }

    /// <summary>Put the trigger (or, without one, every trigger) back on its type default.</summary>
    public bool Clear { get; set; }
}

/// <summary>
/// <c>POST /api/habits/target</c> (<c>bsk habit target</c>): send <see cref="MinAdherence"/>,
/// <see cref="WindowDays"/> or both — a part left out keeps what the habit has — or
/// <see cref="Clear"/>.
/// </summary>
public sealed class HabitTargetBody
{
    /// <summary>The Habit (id, urn or title).</summary>
    public string Habit { get; set; } = "";

    /// <summary>
    /// The share of days to follow: above 0 and up to 1 (0.8), or a percentage above 1 and up
    /// to 100 (80), stored as the fraction.
    /// </summary>
    public decimal? MinAdherence { get; set; }

    /// <summary>The window it is measured over, in whole days (1 to 3650).</summary>
    public int? WindowDays { get; set; }

    /// <summary>Put the habit back on the default target, 80% over 60 days.</summary>
    public bool Clear { get; set; }
}

public sealed class RewardTargetBody
{
    public string Reward { get; set; } = "";

    /// <summary>The target in dollars; null removes it.</summary>
    public decimal? TargetUsd { get; set; }
}

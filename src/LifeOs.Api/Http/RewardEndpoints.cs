using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Api.Http;

/// <summary>
/// The rewards surface (REWARD-1 / REWARD-3 / REWARD-5) — the HTTP twin of
/// <c>bsk wallet</c>, <c>bsk ledger</c>, <c>bsk redeem</c> and <c>bsk rewards</c>. Reads
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

public sealed class RewardTargetBody
{
    public string Reward { get; set; } = "";

    /// <summary>The target in dollars; null removes it.</summary>
    public decimal? TargetUsd { get; set; }
}

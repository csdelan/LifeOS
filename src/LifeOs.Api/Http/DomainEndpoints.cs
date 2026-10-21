using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Rewards;

namespace LifeOs.Api.Http;

/// <summary>
/// The life domains (REWARD-6 / REWARD-7, xp-domains plan §3.11) — the HTTP twin of
/// <c>bsk domains</c>. The read folds on the reader connection; settling writes rank-up Gems
/// through <see cref="RewardService"/>, as the <c>bsk rewards reconcile</c> sweep does.
/// </summary>
public static class DomainEndpoints
{
    public static void MapDomainEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .WithTags("Domains")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/domains", GetDomains).WithName("GetDomains")
            .Produces<DomainsRead>();
        api.MapGet("/domains/{key}/days", GetDomainDays).WithName("GetDomainDays")
            .Produces<DomainDaysRead>();
        api.MapPost("/domains/settle", Settle).WithName("SettleDomains")
            .Produces<EarnResult>();
    }

    private static async Task<IResult> GetDomains(RewardReader rewards, CancellationToken cancellationToken)
        => Results.Ok(await rewards.GetDomainsAsync(cancellationToken));

    /// <summary>
    /// One domain's last <paramref name="days"/> days (30 by default, up to two years), named by
    /// key, label or first word ("health"): what the Character page's daily chart draws.
    /// </summary>
    private static async Task<IResult> GetDomainDays(
        string key, int? days, RewardReader rewards, CancellationToken cancellationToken)
        => Results.Ok(await rewards.GetDomainDaysAsync(key, days ?? 30, cancellationToken));

    /// <summary>
    /// Pays any rank-up the domains have reached and not been paid — one reached by time alone
    /// (an old miss leaving the window, the domain turning old enough) included — and answers
    /// what it paid and each domain whose rank that moved. The Character page calls it when it
    /// opens. Safe to repeat: a rank pays once.
    /// </summary>
    private static async Task<IResult> Settle(RewardService rewards, CancellationToken cancellationToken)
    {
        var settled = await rewards.SettleDomainsAsync(cancellationToken);
        return Results.Ok(RewardRows.Earned(settled.Awards, settled.Progress));
    }
}

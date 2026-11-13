using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The reward catalog's reads (<c>bsk.v_reward</c>, migration 0028, REWARD-3): each Reward with
/// what has been redeemed against it. It reads only; a redemption is written by
/// <see cref="NpgsqlLedgerRepository"/> (code review CR47).
/// </summary>
public sealed class NpgsqlRewardCatalogReader(NpgsqlDataSource dataSource) : IRewardCatalogReader
{
    // The catalog columns, aliased to CatalogRow's property names (no underscore
    // matching in the CLI host). price is read as text and parsed leniently in C#.
    private const string CatalogSql = """
        SELECT id, urn, name, kind, price::text AS pricejson, target_usd AS targetusd,
               redemptions, coins_redeemed AS coinsredeemed, dollars_redeemed AS dollarsredeemed,
               last_redeemed_at AS lastredeemedat, percent_of_target AS percentoftarget,
               remaining_usd AS remainingusd, description, archived
        FROM bsk.v_reward
        """;

    public async Task<IReadOnlyList<CatalogReward>> GetCatalogAsync(
        bool includeArchived, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CatalogRow>(new CommandDefinition(
            $"""
            {CatalogSql}
            WHERE @includeArchived OR NOT archived
            ORDER BY lower(name), urn;
            """,
            new { includeArchived },
            cancellationToken: cancellationToken));
        return rows.Select(ToCatalogReward).ToList();
    }

    public async Task<CatalogReward?> GetCatalogRewardAsync(Guid rewardId, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CatalogRow>(new CommandDefinition(
            $"""
            {CatalogSql}
            WHERE id = @rewardId;
            """,
            new { rewardId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToCatalogReward(row);
    }

    private static CatalogReward ToCatalogReward(CatalogRow row)
        => new(
            new SubjectRef(row.Id, row.Urn, SubjectTypes.Reward, row.Name),
            row.Kind,
            RewardPrice.Parse(row.PriceJson),
            row.TargetUsd,
            row.Redemptions,
            row.CoinsRedeemed,
            row.DollarsRedeemed,
            AsUtc(row.LastRedeemedAt),
            row.PercentOfTarget,
            row.RemainingUsd,
            row.Description,
            row.Archived);

    private static DateTimeOffset? AsUtc(DateTime? value)
        => value is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;

    private sealed class CatalogRow
    {
        public Guid Id { get; set; }
        public string Urn { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string? PriceJson { get; set; }
        public decimal? TargetUsd { get; set; }
        public long Redemptions { get; set; }
        public long CoinsRedeemed { get; set; }
        public decimal DollarsRedeemed { get; set; }
        public DateTime? LastRedeemedAt { get; set; }
        public decimal? PercentOfTarget { get; set; }
        public decimal? RemainingUsd { get; set; }
        public string? Description { get; set; }
        public bool Archived { get; set; }
    }
}

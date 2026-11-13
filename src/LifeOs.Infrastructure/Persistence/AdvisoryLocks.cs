using Dapper;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The transaction-scoped advisory locks that serialize a feature's writes (code review CR20),
/// with every fixed key in one list so two features can't pick the same one.
/// </summary>
internal static class AdvisoryLocks
{
    /// <summary>Every ledger write: a balance check is only sound if nothing else can land between it and the commit.</summary>
    public const long Wallet = 0x7761_6C6C_6574; // "wallet"

    /// <summary>Every food write: a fast's overlap check reads every fast.</summary>
    public const long FoodLog = 0x666F_6F64_6C6F_67; // "foodlog"

    /// <summary>Every timer write: starting a timer can pause another, so writes to different tasks serialize too.</summary>
    public const long Timers = 0x7469_6D65_7273; // "timers"

    /// <summary>Every Oura sync.</summary>
    public const long OuraSync = 0x6F75_7261_7379_6E63; // "ourasync"

    /// <summary>Holds <paramref name="key"/> until <paramref name="transaction"/> ends.</summary>
    public static Task TakeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long key, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@key);", new { key }, transaction, cancellationToken: cancellationToken));

    /// <summary>Holds the lock named <paramref name="name"/> (hashed by Postgres) until <paramref name="transaction"/> ends.</summary>
    public static Task TakeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string name, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@name, 0));", new { name }, transaction,
            cancellationToken: cancellationToken));
}

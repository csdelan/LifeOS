using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace LifeOs.Infrastructure.Rebuild;

/// <summary>
/// Regenerates the derived projections in <c>bsk_derived</c> from source, and
/// verifies that the materialized state still matches what source would
/// produce. Rebuild is the only writer of derived tables (epic invariants 1, 2,
/// 8); its output is deterministic and byte-comparable across runs.
/// </summary>
public sealed class DerivedRebuilder
{
    // The materialized derived table and the view that defines its contents.
    private const string DerivedTable = "bsk_derived.subject_current";
    private const string SourceView = "bsk_derived.subject_current_source";

    private readonly string _connectionString;

    public DerivedRebuilder(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <summary>
    /// Truncates and repopulates every derived table from source inside a single
    /// transaction. Returns the number of rows written.
    /// </summary>
    public async Task<int> RebuildAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            $"TRUNCATE {DerivedTable};", transaction: transaction, cancellationToken: cancellationToken));

        var written = await connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {DerivedTable} SELECT * FROM {SourceView};",
            transaction: transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return written;
    }

    /// <summary>
    /// Rebuilds into a shadow (the source view) without mutating the materialized
    /// table, and diffs the two. Read-only: reports drift, changes nothing.
    /// </summary>
    public async Task<RebuildVerification> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var materialized = await SnapshotAsync(connection, DerivedTable, cancellationToken);
        var fresh = await SnapshotAsync(connection, SourceView, cancellationToken);

        return new RebuildVerification(
            HasDrift: !string.Equals(materialized, fresh, StringComparison.Ordinal),
            MaterializedChecksum: Checksum(materialized),
            FreshChecksum: Checksum(fresh));
    }

    /// <summary>
    /// A canonical, byte-comparable snapshot of the current materialized derived
    /// state. Equal snapshots mean identical derived state.
    /// </summary>
    public async Task<string> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await SnapshotAsync(connection, DerivedTable, cancellationToken);
    }

    /// <summary>Checksum of the current materialized derived state.</summary>
    public async Task<string> ChecksumAsync(CancellationToken cancellationToken = default)
        => Checksum(await SnapshotAsync(cancellationToken));

    private static async Task<string> SnapshotAsync(
        NpgsqlConnection connection, string relation, CancellationToken cancellationToken)
    {
        // `relation` is one of the two private constants above — never user input.
        var sql = CanonicalSql(relation);
        var snapshot = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return snapshot ?? string.Empty;
    }

    // Serializes a derived relation to a deterministic string: one JSON object
    // per row (jsonb canonicalizes key order and escaping), rows ordered by
    // subject_id, timestamps normalized to UTC so the result is tz-independent.
    private static string CanonicalSql(string relation) => $$"""
        SELECT coalesce(string_agg(row_json, E'\n' ORDER BY subject_id), '')
        FROM (
            SELECT
                subject_id,
                jsonb_build_object(
                    'subject_id', subject_id::text,
                    'urn', urn,
                    'type', type,
                    'title', title,
                    'status', status,
                    'status_event_id', status_event_id::text,
                    'status_occurred_at',
                        to_char(status_occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.USZ')
                )::text AS row_json
            FROM {{relation}}
        ) canonical_rows;
        """;

    private static string Checksum(string snapshot)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(snapshot));
        return Convert.ToHexStringLower(bytes);
    }
}

/// <summary>Result of a read-only rebuild verification.</summary>
/// <param name="HasDrift">True when the materialized state differs from source.</param>
/// <param name="MaterializedChecksum">Checksum of the current materialized state.</param>
/// <param name="FreshChecksum">Checksum of the state a rebuild would produce.</param>
public readonly record struct RebuildVerification(
    bool HasDrift, string MaterializedChecksum, string FreshChecksum);

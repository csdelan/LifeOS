using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The rewards ledger (REWARD-1 / D11). Reads go through the <c>bsk.v_ledger</c> /
/// <c>bsk.v_wallet</c> folds and <c>bsk.ledger_opened_at()</c> (migration 0025), and
/// reconcile's reads through <c>bsk.v_reward_due</c> / <c>bsk.v_reward_trigger</c>
/// (migration 0026). Every write takes one transaction-scoped advisory lock shared by
/// all ledger writes, re-checks that each earn or reversal's award is still in the state
/// reconcile saw, inserts each entry's event and edges, and — for a guarded entry (a
/// redemption) — re-reads the balances it touched before committing, so two
/// redemptions racing from two windows cannot together overspend, and two reconciles
/// racing on one item cannot both pay it.
/// </summary>
public sealed class NpgsqlLedgerRepository(string connectionString) : ILedgerRepository
{
    // One lock for every ledger write: a balance check is only sound if nothing else
    // can land between the check and the commit. The key is arbitrary but fixed.
    private const long WalletLockKey = 0x7761_6C6C_6574; // "wallet"

    private const string InsertEventSql = """
        INSERT INTO bsk.event
            (kind, provenance, occurred_at, recorded_at, source_id, external_id,
             payload, derived_from, artifact_id)
        VALUES
            (@Kind, @Provenance, @OccurredAt, @RecordedAt, @SourceId, @ExternalId,
             @Payload::jsonb, @DerivedFrom, @ArtifactId)
        RETURNING id;
        """;

    private const string InsertEdgeSql = """
        INSERT INTO bsk.subject_event (event_id, relation, subject_id, provenance)
        VALUES (@EventId, @Relation, @SubjectId, @Provenance);
        """;

    // Each currency a guarded entry touched that is now below zero, with the entries'
    // own (negative) amount in it, so the refusal can say what the balance was.
    private const string OverdrawnSql = """
        SELECT w.currency, w.balance,
               sum(((e.payload->'amounts')->>w.currency)::numeric)::bigint AS delta
        FROM bsk.v_wallet w
        JOIN bsk.event e
          ON e.id = ANY(@ids)
         AND jsonb_exists(e.payload->'amounts', w.currency)
        WHERE w.balance < 0
        GROUP BY w.currency, w.balance
        ORDER BY w.currency;
        """;

    // Whether an award key currently nets to non-zero in any currency — i.e. is paid.
    private const string IsPaidSql = """
        SELECT EXISTS (
            SELECT 1
            FROM bsk.v_ledger
            WHERE award_key = @key
            GROUP BY currency
            HAVING sum(amount) <> 0);
        """;

    public async Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<BalanceRow>(new CommandDefinition(
            "SELECT currency, earned, reversed, spent, balance FROM bsk.v_wallet ORDER BY currency;",
            cancellationToken: cancellationToken));
        return rows.Select(r => new WalletBalance(r.Currency, r.Earned, r.Reversed, r.Spent, r.Balance)).ToList();
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetEntriesAsync(
        DateTimeOffset? from, DateTimeOffset? to, int? limit, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Page by entry, not by row: the newest `limit` events, then every currency row
        // they carry. Aliased to the row's property names (no underscore matching in the
        // CLI host).
        var rows = await connection.QueryAsync<LedgerRow>(new CommandDefinition(
            """
            WITH picked AS (
                SELECT event_id, occurred_at
                FROM bsk.v_ledger
                WHERE (@from::timestamptz IS NULL OR occurred_at >= @from::timestamptz)
                  AND (@to::timestamptz IS NULL OR occurred_at < @to::timestamptz)
                GROUP BY event_id, occurred_at
                ORDER BY occurred_at DESC, event_id DESC
                LIMIT @limit::bigint
            )
            SELECT l.event_id AS eventid, l.occurred_at AS occurredat, l.entry, l.provenance,
                   l.currency, l.amount, l.trigger, l.award_key AS awardkey,
                   l.subject_id AS subjectid, l.subject_urn AS subjecturn,
                   l.subject_type AS subjecttype, l.subject_title AS subjecttitle,
                   l.note, l.dollars, l.coin_rate AS coinrate
            FROM bsk.v_ledger l
            JOIN picked p ON p.event_id = l.event_id
            ORDER BY l.occurred_at DESC, l.event_id DESC;
            """,
            new { from = from?.ToUniversalTime(), to = to?.ToUniversalTime(), limit = (long?)limit },
            cancellationToken: cancellationToken));

        return rows
            .GroupBy(r => r.EventId)
            .Select(ToEntry)
            .ToList();
    }

    public async Task<DateTimeOffset?> GetOpenedAtAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var openedAt = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT bsk.ledger_opened_at();", cancellationToken: cancellationToken));
        return AsUtc(openedAt);
    }

    public async Task<(DateTimeOffset OpenedAt, bool Created)> OpenAsync(
        NewEvent openEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, cancellationToken);

        var existing = AsUtc(await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT bsk.ledger_opened_at();", transaction: transaction, cancellationToken: cancellationToken)));
        if (existing is { } openedAt)
        {
            return (openedAt, false);
        }

        await InsertEventAsync(connection, transaction, openEvent, cancellationToken);

        // Read back what was stored (microsecond precision), so this opening compares
        // equal to every later read of it.
        var stored = AsUtc(await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT bsk.ledger_opened_at();", transaction: transaction, cancellationToken: cancellationToken)))!.Value;
        await transaction.CommitAsync(cancellationToken);
        return (stored, true);
    }

    public async Task<IReadOnlyList<Guid>> AppendAsync(
        IReadOnlyList<LedgerWrite> writes, CancellationToken cancellationToken = default)
    {
        if (writes.Count == 0)
        {
            return [];
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, cancellationToken);

        foreach (var write in writes)
        {
            if (write is not { AwardKey: { } awardKey, ExpectPaid: { } expectPaid })
            {
                continue;
            }

            var paid = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                IsPaidSql, new { key = awardKey }, transaction: transaction, cancellationToken: cancellationToken));
            if (paid != expectPaid)
            {
                throw new LedgerConflictException(awardKey);
            }
        }

        var ids = new List<Guid>(writes.Count);
        var guarded = new List<Guid>();
        foreach (var write in writes)
        {
            var eventId = await InsertEventAsync(connection, transaction, write.Event, cancellationToken);
            foreach (var edge in write.Edges)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    InsertEdgeSql,
                    new { EventId = eventId, edge.Relation, edge.SubjectId, edge.Provenance },
                    transaction: transaction,
                    cancellationToken: cancellationToken));
            }

            ids.Add(eventId);
            if (write.GuardBalance)
            {
                guarded.Add(eventId);
            }
        }

        if (guarded.Count > 0)
        {
            var overdrawn = await connection.QueryFirstOrDefaultAsync<OverdrawnRow>(new CommandDefinition(
                OverdrawnSql,
                new { ids = guarded.ToArray() },
                transaction: transaction,
                cancellationToken: cancellationToken));
            if (overdrawn is not null)
            {
                // Disposing the uncommitted transaction rolls every entry back.
                throw new InsufficientBalanceException(
                    overdrawn.Currency, overdrawn.Balance - overdrawn.Delta, -overdrawn.Delta);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return ids;
    }

    public Task<IReadOnlyList<DueAward>> GetDueAsync(Guid subjectId, CancellationToken cancellationToken = default)
        => GetAwardsAsync("bsk.v_reward_due", subjectId, cancellationToken);

    public Task<IReadOnlyList<DueAward>> GetTriggersAsync(Guid subjectId, CancellationToken cancellationToken = default)
        => GetAwardsAsync("bsk.v_reward_trigger", subjectId, cancellationToken);

    public async Task<IReadOnlyList<PaidAward>> GetPaidAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PaidRow>(new CommandDefinition(
            """
            SELECT event_id AS eventid, occurred_at AS occurredat, recorded_at AS recordedat,
                   award_key AS awardkey, trigger, currency, amount
            FROM bsk.v_ledger
            WHERE subject_id = @subjectId
              AND award_key IS NOT NULL;
            """,
            new { subjectId },
            cancellationToken: cancellationToken));

        // Net each key per currency; a key that nets to zero everywhere is not paid.
        return rows
            .GroupBy(r => r.AwardKey)
            .Select(key =>
            {
                var net = key
                    .GroupBy(r => r.Currency)
                    .Select(c => (Currency: c.Key, Net: c.Sum(r => r.Amount)))
                    .Where(c => c.Net != 0)
                    .OrderBy(c => DisplayOrder(c.Currency))
                    .ThenBy(c => c.Currency, StringComparer.Ordinal)
                    .ToDictionary(c => c.Currency, c => c.Net);
                var last = key
                    .OrderByDescending(r => r.OccurredAt)
                    .ThenByDescending(r => r.RecordedAt)
                    .ThenByDescending(r => r.EventId)
                    .First();
                return new PaidAward(key.Key, last.Trigger, net, last.EventId);
            })
            .Where(award => award.Net.Count > 0)
            .OrderBy(award => award.AwardKey, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<SubjectRef>> GetReconcileCandidatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SubjectRow>(new CommandDefinition(
            """
            SELECT s.id, s.urn, s.type, s.title
            FROM bsk.subject s
            WHERE s.id IN (
                SELECT subject_id FROM bsk.v_reward_due
                UNION
                SELECT subject_id FROM bsk.v_ledger
                WHERE award_key IS NOT NULL AND subject_id IS NOT NULL)
            ORDER BY s.urn;
            """,
            cancellationToken: cancellationToken));
        return rows.Select(r => new SubjectRef(r.Id, r.Urn, r.Type, r.Title)).ToList();
    }

    public async Task<Guid?> GetLatestSubjectEventAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Events name their subject either in the payload (state_change, archive_change)
        // or through an event -> subject edge (adherence, timer, activity).
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT x.id
            FROM (
                SELECT e.id, e.occurred_at, e.recorded_at
                FROM bsk.event e
                WHERE e.kind <> 'ledger'
                  AND e.payload->>'subject_id' = @subjectId::text
                UNION
                SELECT e.id, e.occurred_at, e.recorded_at
                FROM bsk.subject_event se
                JOIN bsk.event e ON e.id = se.event_id
                WHERE se.subject_id = @subjectId
                  AND e.kind <> 'ledger'
            ) x
            ORDER BY x.occurred_at DESC, x.recorded_at DESC, x.id DESC
            LIMIT 1;
            """,
            new { subjectId },
            cancellationToken: cancellationToken));
    }

    private async Task<IReadOnlyList<DueAward>> GetAwardsAsync(
        string view, Guid subjectId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AwardRow>(new CommandDefinition(
            $"""
            SELECT award_key AS awardkey, trigger, source_event_ids AS sourceeventids,
                   triggered_at AS triggeredat
            FROM {view}
            WHERE subject_id = @subjectId
            ORDER BY award_key;
            """,
            new { subjectId },
            cancellationToken: cancellationToken));
        return rows
            .Select(r => new DueAward(r.AwardKey, r.Trigger, r.SourceEventIds, AsUtc(r.TriggeredAt)!.Value))
            .ToList();
    }

    private static Task LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@key);",
            new { key = WalletLockKey },
            transaction: transaction,
            cancellationToken: cancellationToken));

    private static Task<Guid> InsertEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, NewEvent newEvent,
        CancellationToken cancellationToken)
        => connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertEventSql,
            new
            {
                newEvent.Kind,
                newEvent.Provenance,
                OccurredAt = newEvent.OccurredAt.ToUniversalTime(),
                RecordedAt = newEvent.RecordedAt.ToUniversalTime(),
                newEvent.SourceId,
                newEvent.ExternalId,
                Payload = newEvent.PayloadJson,
                DerivedFrom = (newEvent.DerivedFrom ?? []).ToArray(),
                newEvent.ArtifactId
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

    private static DateTimeOffset? AsUtc(DateTime? value)
        => value is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;

    private static LedgerEntry ToEntry(IGrouping<Guid, LedgerRow> rows)
    {
        var first = rows.First();
        // Amounts in display order; a currency the vocabulary no longer names sorts last.
        var amounts = rows
            .GroupBy(r => r.Currency)
            .OrderBy(g => DisplayOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var subject = first.SubjectId is { } subjectId
            ? new SubjectRef(subjectId, first.SubjectUrn ?? "", first.SubjectType ?? "", first.SubjectTitle ?? "")
            : null;

        return new LedgerEntry(
            first.EventId,
            AsUtc(first.OccurredAt)!.Value,
            first.Entry ?? "",
            first.Provenance,
            amounts,
            first.Trigger,
            first.AwardKey,
            subject,
            first.Note,
            first.Dollars,
            first.CoinRate);
    }

    private static int DisplayOrder(string currency)
    {
        for (var i = 0; i < Currencies.All.Count; i++)
        {
            if (string.Equals(Currencies.All[i].Key, currency, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private sealed class BalanceRow
    {
        public string Currency { get; set; } = "";
        public long Earned { get; set; }
        public long Reversed { get; set; }
        public long Spent { get; set; }
        public long Balance { get; set; }
    }

    private sealed class LedgerRow
    {
        public Guid EventId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string? Entry { get; set; }
        public string Provenance { get; set; } = "";
        public string Currency { get; set; } = "";
        public long Amount { get; set; }
        public string? Trigger { get; set; }
        public string? AwardKey { get; set; }
        public Guid? SubjectId { get; set; }
        public string? SubjectUrn { get; set; }
        public string? SubjectType { get; set; }
        public string? SubjectTitle { get; set; }
        public string? Note { get; set; }
        public decimal? Dollars { get; set; }
        public decimal? CoinRate { get; set; }
    }

    private sealed class AwardRow
    {
        public string AwardKey { get; set; } = "";
        public string Trigger { get; set; } = "";
        public Guid[] SourceEventIds { get; set; } = [];
        public DateTime TriggeredAt { get; set; }
    }

    private sealed class PaidRow
    {
        public Guid EventId { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime RecordedAt { get; set; }
        public string AwardKey { get; set; } = "";
        public string? Trigger { get; set; }
        public string Currency { get; set; } = "";
        public long Amount { get; set; }
    }

    private sealed class SubjectRow
    {
        public Guid Id { get; set; }
        public string Urn { get; set; } = "";
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
    }

    private sealed class OverdrawnRow
    {
        public string Currency { get; set; } = "";
        public long Balance { get; set; }
        public long Delta { get; set; }
    }
}

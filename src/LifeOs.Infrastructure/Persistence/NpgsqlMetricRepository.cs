using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Metrics and their readings (MEASURE-1, MEASURE-3, D20, measurements plan M1, M3). Reads go
/// through <c>bsk.v_metric</c> (migrations 0050, 0051) and <c>bsk.v_metric_reading</c> (0050); a reading version write
/// inserts each event and its <c>concerns</c> edge to the metric in one transaction, since the
/// stream is append-only. The API reads through one of these on the reader connection.
/// </summary>
public sealed class NpgsqlMetricRepository(string connectionString) : IMetricRepository
{
    // Aliased to the rows' property names (the CLI host doesn't turn on underscore matching);
    // dates and local times as text, since Dapper has no DateOnly mapping in every host.
    private const string MetricSql = """
        SELECT id, urn, name, unit, decimals, target, better, catalog, archived_at AS archivedat,
               to_char(archived_on, 'YYYY-MM-DD') AS archivedon, created_at AS createdat, low, high
        FROM bsk.v_metric
        """;

    private const string ReadingSql = """
        SELECT id, metric_id AS metricid, value, unit, note, taken_at AS takenat,
               to_char(taken_on, 'YYYY-MM-DD') AS takenon,
               to_char(taken_local, 'YYYY-MM-DD"T"HH24:MI:SS') AS takenlocal,
               first_logged_at AS firstloggedat, logged_at AS loggedat, versions, event_id AS eventid
        FROM bsk.v_metric_reading
        """;

    private const string ReadingOrder = " ORDER BY taken_at, id";

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

    public async Task<IReadOnlyList<Metric>> GetMetricsAsync(CancellationToken cancellationToken = default)
        => (await QueryAsync<MetricRow>(MetricSql + " ORDER BY lower(name), id", null, cancellationToken))
            .Select(r => r.ToMetric()).ToList();

    public async Task<Metric?> GetMetricAsync(Guid id, CancellationToken cancellationToken = default)
        => (await QueryAsync<MetricRow>(MetricSql + " WHERE id = @id", new { id }, cancellationToken))
            .Select(r => r.ToMetric()).FirstOrDefault();

    public async Task<IReadOnlyList<MetricReading>> GetReadingsAsync(
        Guid? metricId = null, DateOnly? from = null, CancellationToken cancellationToken = default)
        => (await QueryAsync<ReadingRow>(
                ReadingSql + """
                     WHERE (@metricId::uuid IS NULL OR metric_id = @metricId::uuid)
                       AND (@from::date IS NULL OR taken_on >= @from::date)
                    """ + ReadingOrder,
                new { metricId, from = from is { } f ? Text(f) : null },
                cancellationToken))
            .Select(r => r.ToReading()).ToList();

    public async Task<IReadOnlyDictionary<Guid, int>> CountReadingsAsync(CancellationToken cancellationToken = default)
        => (await QueryAsync<CountRow>(
                "SELECT metric_id AS metricid, count(*)::int AS readings FROM bsk.v_metric_reading GROUP BY metric_id",
                null, cancellationToken))
            .ToDictionary(r => r.MetricId, r => r.Readings);

    public async Task<MetricReading?> GetReadingAsync(Guid id, CancellationToken cancellationToken = default)
        => (await QueryAsync<ReadingRow>(ReadingSql + " WHERE id = @id", new { id }, cancellationToken))
            .Select(r => r.ToReading()).FirstOrDefault();

    public async Task<IReadOnlyList<MetricReading>> FindReadingsAsync(string prefix, CancellationToken cancellationToken = default)
        => (await QueryAsync<ReadingRow>(
                ReadingSql + " WHERE id::text LIKE @prefix ORDER BY taken_at DESC, id DESC LIMIT 20",
                new { prefix = IdPrefix(prefix) }, cancellationToken))
            .Select(r => r.ToReading()).ToList();

    public async Task<DateTimeOffset> InstantAtAsync(DateTime local, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var instant = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            "SELECT (@local::timestamp AT TIME ZONE bsk.home_zone());",
            new { local = local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
            cancellationToken: cancellationToken));
        return Utc(instant);
    }

    public async Task<IReadOnlyList<Guid>> AppendReadingsAsync(
        IReadOnlyList<MetricReadingWrite> writes, CancellationToken cancellationToken = default)
    {
        if (writes.Count == 0)
        {
            return [];
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var ids = new List<Guid>(writes.Count);
        foreach (var write in writes)
        {
            var e = write.Event;
            var eventId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                InsertEventSql,
                new
                {
                    e.Kind,
                    e.Provenance,
                    e.OccurredAt,
                    e.RecordedAt,
                    e.SourceId,
                    e.ExternalId,
                    Payload = e.PayloadJson,
                    DerivedFrom = (e.DerivedFrom ?? []).ToArray(),
                    e.ArtifactId
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                InsertEdgeSql,
                new
                {
                    EventId = eventId,
                    Relation = SubjectEventRelations.Concerns,
                    SubjectId = write.MetricId,
                    e.Provenance
                },
                transaction: transaction,
                cancellationToken: cancellationToken));
            ids.Add(eventId);
        }

        await transaction.CommitAsync(cancellationToken);
        return ids;
    }

    private async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? args, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.QueryAsync<T>(new CommandDefinition(sql, args, cancellationToken: cancellationToken));
    }

    /// <summary>A LIKE pattern for ids starting with <paramref name="prefix"/>: hex and dashes only.</summary>
    private static string IdPrefix(string prefix)
        => new string(prefix.Trim().ToLowerInvariant().Where(c => Uri.IsHexDigit(c) || c == '-').ToArray()) + "%";

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    // 192.40 reads back as 192.4, so a value prints the way it was given.
    private static decimal Normal(decimal value)
        => decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private sealed class MetricRow
    {
        public Guid Id { get; set; }
        public string Urn { get; set; } = "";
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public int Decimals { get; set; }
        public decimal? Target { get; set; }
        public string? Better { get; set; }
        public string? Catalog { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public string? ArchivedOn { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal? Low { get; set; }
        public decimal? High { get; set; }

        public Metric ToMetric() => new(
            Id, Urn, Name, Unit, Decimals,
            Target is { } t ? Normal(t) : null,
            Better, Catalog,
            ArchivedAt is { } at ? Utc(at) : null,
            ArchivedOn is { } on ? ParseDay(on) : null,
            Utc(CreatedAt),
            Low is { } low ? Normal(low) : null,
            High is { } high ? Normal(high) : null);
    }

    private sealed class ReadingRow
    {
        public Guid Id { get; set; }
        public Guid MetricId { get; set; }
        public decimal Value { get; set; }
        public string Unit { get; set; } = "";
        public string? Note { get; set; }
        public DateTime TakenAt { get; set; }
        public string TakenOn { get; set; } = "";
        public string TakenLocal { get; set; } = "";
        public DateTime FirstLoggedAt { get; set; }
        public DateTime LoggedAt { get; set; }
        public int Versions { get; set; }
        public Guid EventId { get; set; }

        public MetricReading ToReading() => new(
            Id, MetricId, Normal(Value), Unit, Note, Utc(TakenAt), ParseDay(TakenOn),
            DateTime.ParseExact(TakenLocal, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            Utc(FirstLoggedAt), Utc(LoggedAt), Versions, EventId);
    }

    private sealed class CountRow
    {
        public Guid MetricId { get; set; }
        public int Readings { get; set; }
    }
}

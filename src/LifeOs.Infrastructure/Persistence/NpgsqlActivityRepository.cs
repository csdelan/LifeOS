using System.Globalization;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Oura's records and Chris's workout notes (ACTIVITY-1..4, D14, activity plan A2). Reads go
/// through the views of migration 0037. A sync writes under a transaction-scoped advisory lock
/// every sync shares, reads what LifeOS holds under it, and appends only what changed — so two
/// syncs take turns and the second finds nothing new. Oura's versions are stamped with the
/// database's clock as each is written (<c>clock_timestamp()</c>, not the transaction's start),
/// so a sync that waited for the lock always stamps later than the one it waited on.
/// </summary>
public sealed class NpgsqlActivityRepository(string connectionString) : IActivityRepository
{
    // One lock for every Oura sync. Arbitrary but fixed.
    private const long SyncLockKey = 0x6F75_7261_7379_6E63; // "ourasync"

    // Aliased to the row's property names (the CLI host doesn't turn on underscore matching);
    // dates as text, since Dapper has no DateOnly mapping in every host.
    private const string ActivityDaySql = """
        SELECT to_char(day, 'YYYY-MM-DD') AS day, steps, active_calories AS activecalories,
               total_calories AS totalcalories, walking_m AS walkingm, high_s AS highs,
               medium_s AS mediums, low_s AS lows, sedentary_s AS sedentarys, resting_s AS restings,
               non_wear_s AS nonwears, score, synced_at AS syncedat, versions, event_id AS eventid
        FROM bsk.v_activity_day
        """;

    private const string WorkoutSql = """
        SELECT workout_id AS id, to_char(day, 'YYYY-MM-DD') AS day, activity, label,
               started_at AS startedat, ended_at AS endedat, seconds, calories,
               distance_m AS distancem, intensity, source, note, note_at AS noteat,
               synced_at AS syncedat, event_id AS eventid
        FROM bsk.v_workout
        """;

    private const string SleepDaySql = """
        SELECT to_char(day, 'YYYY-MM-DD') AS day, score, contributors::text AS contributors,
               bed_at AS bedat, woke_at AS wokeat, in_bed_s AS inbeds, asleep_s AS asleeps,
               deep_s AS deeps, light_s AS lights, rem_s AS rems, awake_s AS awakes, efficiency,
               average_hrv AS averagehrv, lowest_hr AS lowesthr, naps, nap_s AS naps_seconds,
               synced_at AS syncedat,
               -- The score's version leads event_ids whenever the day has a score (0037).
               CASE WHEN score IS NOT NULL THEN event_ids[1] END AS scoreeventid
        FROM bsk.v_sleep_day
        """;

    private const string ReadinessDaySql = """
        SELECT to_char(day, 'YYYY-MM-DD') AS day, score, contributors::text AS contributors,
               temperature_deviation::float8 AS temperaturedeviation,
               temperature_trend_deviation::float8 AS temperaturetrenddeviation,
               synced_at AS syncedat, event_id AS eventid
        FROM bsk.v_readiness_day
        """;

    private const string SleepPeriodSql = """
        SELECT period_id AS id, to_char(day, 'YYYY-MM-DD') AS day, type, started_at AS startedat,
               ended_at AS endedat, in_bed_s AS inbeds, asleep_s AS asleeps, deep_s AS deeps,
               light_s AS lights, rem_s AS rems, awake_s AS awakes, latency_s AS latencys, efficiency,
               average_hrv AS averagehrv, average_hr::float8 AS averagehr, lowest_hr AS lowesthr,
               synced_at AS syncedat
        FROM bsk.v_sleep_period
        """;

    private const string TagSql = """
        SELECT tag_id AS id, to_char(day, 'YYYY-MM-DD') AS day, to_char(end_day, 'YYYY-MM-DD') AS endday,
               type_code AS typecode, custom_name AS customname, started_at AS startedat,
               ended_at AS endedat, comment, synced_at AS syncedat, event_id AS eventid
        FROM bsk.v_oura_tag
        """;

    public async Task<IOuraSyncLock> BeginSyncAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        NpgsqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancellationToken);
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(@key);",
                new { key = SyncLockKey },
                transaction,
                cancellationToken: cancellationToken));
            return new SyncLock(connection, transaction);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ActivityDay>> GetActivityDaysAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<ActivityDayRow>(
                ActivityDaySql + " WHERE day BETWEEN @from::date AND @to::date ORDER BY day",
                Range(from, to), cancellationToken))
            .Select(r => r.ToDay()).ToList();

    public async Task<IReadOnlyList<Workout>> GetWorkoutsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<WorkoutRow>(
                WorkoutSql + " WHERE day BETWEEN @from::date AND @to::date ORDER BY started_at DESC, workout_id",
                Range(from, to), cancellationToken))
            .Select(r => r.ToWorkout()).ToList();

    public async Task<Workout?> GetWorkoutAsync(string id, CancellationToken cancellationToken = default)
        => (await QueryAsync<WorkoutRow>(WorkoutSql + " WHERE workout_id = @id", new { id }, cancellationToken))
            .Select(r => r.ToWorkout()).FirstOrDefault();

    public async Task<IReadOnlyList<Workout>> FindWorkoutsAsync(string prefix, CancellationToken cancellationToken = default)
        => (await QueryAsync<WorkoutRow>(
                WorkoutSql + " WHERE starts_with(workout_id, @prefix) ORDER BY started_at DESC, workout_id LIMIT 20",
                new { prefix = prefix.Trim() }, cancellationToken))
            .Select(r => r.ToWorkout()).ToList();

    public async Task<IReadOnlyList<SleepDay>> GetSleepDaysAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<SleepDayRow>(
                SleepDaySql + " WHERE day BETWEEN @from::date AND @to::date ORDER BY day",
                Range(from, to), cancellationToken))
            .Select(r => r.ToDay()).ToList();

    public async Task<IReadOnlyList<ReadinessDay>> GetReadinessDaysAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<ReadinessDayRow>(
                ReadinessDaySql + " WHERE day BETWEEN @from::date AND @to::date ORDER BY day",
                Range(from, to), cancellationToken))
            .Select(r => r.ToDay()).ToList();

    public async Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<SleepPeriodRow>(
                SleepPeriodSql + " WHERE day BETWEEN @from::date AND @to::date ORDER BY started_at, period_id",
                Range(from, to), cancellationToken))
            .Select(r => r.ToPeriod()).ToList();

    public async Task<IReadOnlyList<ActivityTag>> GetTagsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<TagRow>(
                TagSql + " WHERE day BETWEEN @from::date AND @to::date ORDER BY started_at DESC, tag_id",
                Range(from, to), cancellationToken))
            .Select(r => r.ToTag()).ToList();

    private async Task<IEnumerable<T>> QueryAsync<T>(string sql, object args, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.QueryAsync<T>(new CommandDefinition(sql, args, cancellationToken: cancellationToken));
    }

    private static object Range(DateOnly from, DateOnly to) => new { from = Text(from), to = Text(to) };

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    private static IReadOnlyDictionary<string, int?> Contributors(string? json)
    {
        var contributors = new SortedDictionary<string, int?>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json))
        {
            return contributors;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return contributors;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            contributors[property.Name] = property.Value.ValueKind == JsonValueKind.Number
                ? (int)Math.Round(property.Value.GetDouble(), MidpointRounding.AwayFromZero)
                : null;
        }

        return contributors;
    }

    /// <summary>A sync's transaction, holding the lock every sync shares until commit or dispose.</summary>
    private sealed class SyncLock(NpgsqlConnection connection, NpgsqlTransaction transaction) : IOuraSyncLock
    {
        private const string InsertSql = """
            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, external_id, payload)
            VALUES (@Kind, 'observed', @OccurredAt, clock_timestamp(), 'oura', @ExternalId, @Payload::jsonb)
            ON CONFLICT (source_id, external_id) WHERE external_id IS NOT NULL DO NOTHING;
            """;

        private bool committed;

        public async Task<IReadOnlyList<OuraHead>> GetHeadsAsync(
            OuraCollection collection, IReadOnlyCollection<string> keys, DateOnly from, DateOnly to,
            CancellationToken cancellationToken = default)
        {
            // The collection's names are constants from OuraCollections, never input.
            var known = OuraCollections.All.SingleOrDefault(c => c == collection)
                ?? throw new ArgumentException($"Not an Oura collection LifeOS keeps: {collection.Name}.", nameof(collection));
            var p = known.PayloadKey;
            var k = known.KeyField;
            var sql = $"""
                SELECT key, day, occurredat, externalid, eventid, voided FROM (
                    SELECT DISTINCT ON (e.payload->'{p}'->>'{k}')
                        e.payload->'{p}'->>'{k}'                              AS key,
                        e.payload->'{p}'->>'day'                              AS day,
                        e.occurred_at                                         AS occurredat,
                        e.external_id                                         AS externalid,
                        e.id                                                  AS eventid,
                        coalesce(e.payload->'{p}'->>'voided', 'false') = 'true' AS voided
                    FROM bsk.event e
                    WHERE e.kind = '{known.Kind}'
                      AND e.source_id = 'oura'
                      AND jsonb_exists(e.payload, '{p}')
                      AND coalesce(e.payload->'{p}'->>'{k}', '') <> ''
                    ORDER BY e.payload->'{p}'->>'{k}', e.recorded_at DESC, e.id DESC) head
                WHERE head.key = ANY(@keys) OR head.day BETWEEN @from AND @to;
                """;
            var rows = await connection.QueryAsync<HeadRow>(new CommandDefinition(
                sql,
                new { keys = keys.ToArray(), from = Text(from), to = Text(to) },
                transaction,
                cancellationToken: cancellationToken));
            return rows.Select(r => r.ToHead()).ToList();
        }

        public async Task<int> AppendAsync(IReadOnlyList<OuraVersion> versions, CancellationToken cancellationToken = default)
        {
            var written = 0;
            foreach (var version in versions)
            {
                written += await connection.ExecuteAsync(new CommandDefinition(
                    InsertSql,
                    new
                    {
                        version.Kind,
                        OccurredAt = version.OccurredAt.UtcDateTime,
                        version.ExternalId,
                        Payload = version.PayloadJson
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            return written;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                    // Already completed: nothing to roll back.
                }
                catch (NpgsqlException)
                {
                    // The connection broke; Postgres rolls back on its own.
                }
            }

            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class HeadRow
    {
        public string Key { get; set; } = "";
        public string? Day { get; set; }
        public DateTime OccurredAt { get; set; }
        public string? ExternalId { get; set; }
        public Guid EventId { get; set; }
        public bool Voided { get; set; }

        // external_id is <collection>:<key>:<hash>:<previous event id or "first">, and a key
        // may itself hold a colon, so the hash is read from the right.
        public OuraHead ToHead()
        {
            var parts = (ExternalId ?? "").Split(':');
            var hash = parts.Length >= 4 ? parts[^2] : "";
            var day = Day is not null
                      && DateOnly.TryParseExact(Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : (DateOnly?)null;
            return new OuraHead(Key, day, Utc(OccurredAt), hash, EventId, Voided);
        }
    }

    private sealed class ActivityDayRow
    {
        public string Day { get; set; } = "";
        public int? Steps { get; set; }
        public int? ActiveCalories { get; set; }
        public int? TotalCalories { get; set; }
        public int? WalkingM { get; set; }
        public int? HighS { get; set; }
        public int? MediumS { get; set; }
        public int? LowS { get; set; }
        public int? SedentaryS { get; set; }
        public int? RestingS { get; set; }
        public int? NonWearS { get; set; }
        public int? Score { get; set; }
        public DateTime SyncedAt { get; set; }
        public int Versions { get; set; }
        public Guid EventId { get; set; }

        public ActivityDay ToDay() => new(
            ParseDay(Day), Steps, ActiveCalories, TotalCalories, WalkingM, HighS, MediumS, LowS, SedentaryS,
            RestingS, NonWearS, Score, Utc(SyncedAt), Versions, EventId);
    }

    private sealed class WorkoutRow
    {
        public string Id { get; set; } = "";
        public string Day { get; set; } = "";
        public string Activity { get; set; } = "";
        public string? Label { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public int Seconds { get; set; }
        public int? Calories { get; set; }
        public int? DistanceM { get; set; }
        public string? Intensity { get; set; }
        public string Source { get; set; } = "";
        public string? Note { get; set; }
        public DateTime? NoteAt { get; set; }
        public DateTime SyncedAt { get; set; }
        public Guid EventId { get; set; }

        public Workout ToWorkout() => new(
            Id, ParseDay(Day), Activity, Label, Utc(StartedAt), Utc(EndedAt), Seconds, Calories, DistanceM,
            Intensity, Source, Note, Utc(NoteAt), Utc(SyncedAt), EventId);
    }

    private sealed class SleepDayRow
    {
        public string Day { get; set; } = "";
        public int? Score { get; set; }
        public string? Contributors { get; set; }
        public DateTime? BedAt { get; set; }
        public DateTime? WokeAt { get; set; }
        public int? InBedS { get; set; }
        public int? AsleepS { get; set; }
        public int? DeepS { get; set; }
        public int? LightS { get; set; }
        public int? RemS { get; set; }
        public int? AwakeS { get; set; }
        public int? Efficiency { get; set; }
        public int? AverageHrv { get; set; }
        public int? LowestHr { get; set; }
        public int Naps { get; set; }
        public int Naps_Seconds { get; set; }
        public DateTime SyncedAt { get; set; }
        public Guid? ScoreEventId { get; set; }

        public SleepDay ToDay() => new(
            ParseDay(Day), Score, NpgsqlActivityRepository.Contributors(Contributors), Utc(BedAt), Utc(WokeAt),
            InBedS, AsleepS, DeepS, LightS, RemS, AwakeS, Efficiency, AverageHrv, LowestHr, Naps, Naps_Seconds,
            Utc(SyncedAt), ScoreEventId);
    }

    private sealed class SleepPeriodRow
    {
        public string Id { get; set; } = "";
        public string Day { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public int? InBedS { get; set; }
        public int? AsleepS { get; set; }
        public int? DeepS { get; set; }
        public int? LightS { get; set; }
        public int? RemS { get; set; }
        public int? AwakeS { get; set; }
        public int? LatencyS { get; set; }
        public int? Efficiency { get; set; }
        public int? AverageHrv { get; set; }
        public double? AverageHr { get; set; }
        public int? LowestHr { get; set; }
        public DateTime SyncedAt { get; set; }

        public SleepPeriod ToPeriod() => new(
            Id, ParseDay(Day), Type, Utc(StartedAt), Utc(EndedAt), InBedS, AsleepS, DeepS, LightS, RemS, AwakeS,
            LatencyS, Efficiency, AverageHrv, AverageHr, LowestHr, Utc(SyncedAt));
    }

    private sealed class TagRow
    {
        public string Id { get; set; } = "";
        public string Day { get; set; } = "";
        public string? EndDay { get; set; }
        public string? TypeCode { get; set; }
        public string? CustomName { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string? Comment { get; set; }
        public DateTime SyncedAt { get; set; }
        public Guid EventId { get; set; }

        public ActivityTag ToTag() => new(
            Id, ParseDay(Day), EndDay is null ? null : ParseDay(EndDay), TypeCode, CustomName, Utc(StartedAt),
            Utc(EndedAt), Comment, Utc(SyncedAt), EventId);
    }

    private sealed class ReadinessDayRow
    {
        public string Day { get; set; } = "";
        public int? Score { get; set; }
        public string? Contributors { get; set; }
        public double? TemperatureDeviation { get; set; }
        public double? TemperatureTrendDeviation { get; set; }
        public DateTime SyncedAt { get; set; }
        public Guid EventId { get; set; }

        public ReadinessDay ToDay() => new(
            ParseDay(Day), Score, NpgsqlActivityRepository.Contributors(Contributors), TemperatureDeviation,
            TemperatureTrendDeviation, Utc(SyncedAt), EventId);
    }
}

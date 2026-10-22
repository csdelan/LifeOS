using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Food Log reads and writes (FOOD-1..4, food-log plan F1). Reads go through
/// <c>bsk.v_meal</c>, <c>bsk.v_fast</c> and <c>bsk.v_food_day</c> (migration 0034). Writes take
/// a transaction-scoped advisory lock shared by every food write, re-check the
/// <see cref="FoodGuard"/> under it, then insert the events — all or nothing, since the stream
/// is append-only. Dates and wall-clock times are the database's home zone
/// (<c>bsk.home_zone()</c>), the zone the views read.
/// </summary>
public sealed class NpgsqlFoodLogRepository(string connectionString) : IFoodLogRepository
{
    // One lock for all food writes: a fast's overlap check reads every fast. Arbitrary but fixed.
    private const long FoodLockKey = 0x666F_6F64_6C6F_67; // "foodlog"

    // Aliased to the row's property names (the CLI host doesn't turn on underscore matching);
    // dates as text, since Dapper has no DateOnly mapping in every host.
    private const string MealSql = """
        SELECT meal_id AS id, eaten_at AS eatenat, to_char(eaten_on, 'YYYY-MM-DD') AS eatenon,
               eaten_local AS eatenlocal, name, score, calories, protein_g AS proteing,
               carbs_g AS carbsg, fat_g AS fatg, meal_type AS mealtype,
               photo_artifact_id AS photoartifactid, first_logged_at AS firstloggedat,
               logged_at AS loggedat, versions, event_id AS eventid, alcoholic
        FROM bsk.v_meal
        """;

    private const string FastSql = """
        SELECT fast_id AS id, started_at AS startedat, ended_at AS endedat,
               to_char(started_on, 'YYYY-MM-DD') AS startedon, to_char(ended_on, 'YYYY-MM-DD') AS endedon,
               started_local AS startedlocal, ended_local AS endedlocal, seconds,
               meals_during AS mealsduring, recorded_at AS recordedat, event_id AS eventid
        FROM bsk.v_fast
        """;

    private const string DaySql = """
        SELECT to_char(day, 'YYYY-MM-DD') AS day, meals, calories, score_calories AS scorecalories,
               longest_fast_seconds AS longestfastseconds, longest_fast_id AS longestfastid,
               fast_event_id AS fasteventid, closed_by_hand AS closedbyhand, closed_at AS closedat,
               close_event_id AS closeeventid, closed, last_recorded_at AS lastrecordedat,
               food_event_ids AS foodeventids, drinks, drink_event_ids AS drinkeventids
        FROM bsk.v_food_day
        """;

    // A fast that's still running reaches now.
    private const string OverlapSql = FastSql + """
         WHERE (@except::uuid IS NULL OR fast_id <> @except::uuid)
           AND started_at < coalesce(@to::timestamptz, now())
           AND @from::timestamptz < coalesce(ended_at, now())
         ORDER BY started_at
        """;

    private const string InsertEventSql = """
        INSERT INTO bsk.event
            (kind, provenance, occurred_at, recorded_at, source_id, external_id,
             payload, derived_from, artifact_id)
        VALUES
            (@Kind, @Provenance, @OccurredAt, @RecordedAt, @SourceId, @ExternalId,
             @Payload::jsonb, @DerivedFrom, @ArtifactId)
        RETURNING id;
        """;

    public async Task<Meal?> GetMealAsync(Guid mealId, CancellationToken cancellationToken = default)
        => (await QueryAsync<MealRow>(MealSql + " WHERE meal_id = @mealId", new { mealId }, cancellationToken))
            .Select(r => r.ToMeal()).FirstOrDefault();

    public async Task<IReadOnlyList<Meal>> FindMealsAsync(string prefix, CancellationToken cancellationToken = default)
        => (await QueryAsync<MealRow>(
                MealSql + " WHERE meal_id::text LIKE @prefix ORDER BY eaten_at DESC LIMIT 20",
                new { prefix = IdPrefix(prefix) }, cancellationToken))
            .Select(r => r.ToMeal()).ToList();

    public async Task<IReadOnlyList<Meal>> GetMealsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<MealRow>(
                MealSql + " WHERE eaten_on BETWEEN @from::date AND @to::date ORDER BY eaten_at, meal_id",
                new { from = Text(from), to = Text(to) }, cancellationToken))
            .Select(r => r.ToMeal()).ToList();

    public async Task<IReadOnlyList<Meal>> GetLatestMealsAsync(
        int limit, bool withCalories, CancellationToken cancellationToken = default)
        => (await QueryAsync<MealRow>(
                MealSql + " WHERE (NOT @withCalories OR calories > 0) ORDER BY eaten_at DESC, meal_id LIMIT @limit",
                new { limit, withCalories }, cancellationToken))
            .Select(r => r.ToMeal()).ToList();

    public async Task<Fast?> GetFastAsync(Guid fastId, CancellationToken cancellationToken = default)
        => (await QueryAsync<FastRow>(FastSql + " WHERE fast_id = @fastId", new { fastId }, cancellationToken))
            .Select(r => r.ToFast()).FirstOrDefault();

    public async Task<IReadOnlyList<Fast>> FindFastsAsync(string prefix, CancellationToken cancellationToken = default)
        => (await QueryAsync<FastRow>(
                FastSql + " WHERE fast_id::text LIKE @prefix ORDER BY started_at DESC LIMIT 20",
                new { prefix = IdPrefix(prefix) }, cancellationToken))
            .Select(r => r.ToFast()).ToList();

    public async Task<Fast?> GetRunningFastAsync(CancellationToken cancellationToken = default)
        => (await QueryAsync<FastRow>(
                FastSql + " WHERE state = 'running' ORDER BY started_at DESC LIMIT 1", null, cancellationToken))
            .Select(r => r.ToFast()).FirstOrDefault();

    public async Task<IReadOnlyList<Fast>> GetFastsEndedAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<FastRow>(
                FastSql + " WHERE ended_on BETWEEN @from::date AND @to::date ORDER BY ended_at, fast_id",
                new { from = Text(from), to = Text(to) }, cancellationToken))
            .Select(r => r.ToFast()).ToList();

    public async Task<IReadOnlyList<Fast>> GetOverlappingFastsAsync(
        FastSpan span, CancellationToken cancellationToken = default)
        => (await QueryAsync<FastRow>(OverlapSql, OverlapArgs(span), cancellationToken))
            .Select(r => r.ToFast()).ToList();

    public async Task<IReadOnlyList<FoodDay>> GetDaysAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => (await QueryAsync<DayRow>(
                DaySql + " WHERE day BETWEEN @from::date AND @to::date ORDER BY day",
                new { from = Text(from), to = Text(to) }, cancellationToken))
            .Select(r => r.ToDay()).ToList();

    public async Task<DrinkHistory> GetDrinkHistoryAsync(DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var first = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT to_char(min(day), 'YYYY-MM-DD') FROM bsk.v_food_day;", cancellationToken: cancellationToken));
        var drinkDays = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT to_char(day, 'YYYY-MM-DD') FROM bsk.v_food_day WHERE drinks > 0 AND day <= @to::date ORDER BY day;",
            new { to = Text(to) },
            cancellationToken: cancellationToken));
        return new DrinkHistory(first is null ? null : ParseDay(first), drinkDays.Select(ParseDay).ToList());
    }

    public async Task<IReadOnlyList<(DateOnly Day, int Drinks)>> CountDrinksAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => (await QueryAsync<DrinkDayRow>(
                """
                SELECT to_char(eaten_on, 'YYYY-MM-DD') AS day, count(*)::int AS drinks
                FROM bsk.v_meal
                WHERE alcoholic AND eaten_at >= @from::timestamptz AND eaten_at < @to::timestamptz
                GROUP BY eaten_on
                ORDER BY eaten_on
                """,
                new { from = from.ToUniversalTime(), to = to.ToUniversalTime() }, cancellationToken))
            .Select(r => (ParseDay(r.Day), r.Drinks)).ToList();

    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var day = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT to_char(bsk.home_today(), 'YYYY-MM-DD');", cancellationToken: cancellationToken));
        return ParseDay(day!);
    }

    public async Task<DateTime> LocalAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var local = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            "SELECT @instant::timestamptz AT TIME ZONE bsk.home_zone();",
            new { instant = instant.ToUniversalTime() },
            cancellationToken: cancellationToken));
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

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

    public async Task<IReadOnlyList<Guid>> AppendAsync(
        IReadOnlyList<NewEvent> events, FoodGuard guard, CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return [];
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@key);",
            new { key = FoodLockKey },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await CheckAsync(connection, transaction, guard, cancellationToken);

        var ids = new List<Guid>(events.Count);
        foreach (var e in events)
        {
            ids.Add(await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
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
                cancellationToken: cancellationToken)));
        }

        await transaction.CommitAsync(cancellationToken);
        return ids;
    }

    /// <summary>Re-checks the guard under the lock; throws when it no longer holds.</summary>
    private static async Task CheckAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, FoodGuard guard,
        CancellationToken cancellationToken)
    {
        if (guard.RunningFast is { } expected)
        {
            var running = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT fast_id FROM bsk.v_fast WHERE state = 'running' ORDER BY started_at DESC LIMIT 1;",
                transaction: transaction,
                cancellationToken: cancellationToken));
            var actual = running ?? Guid.Empty;
            if (actual != expected)
            {
                throw new FoodConflictException(expected == Guid.Empty
                    ? "A fast is already running — it was started elsewhere. Refresh and try again."
                    : "That fast is no longer running — it was changed elsewhere. Refresh and try again.");
            }
        }

        if (guard.Span is { } span)
        {
            var overlap = await connection.QueryFirstOrDefaultAsync<FastRow>(new CommandDefinition(
                OverlapSql + " LIMIT 1",
                OverlapArgs(span),
                transaction: transaction,
                cancellationToken: cancellationToken));
            if (overlap is not null)
            {
                throw new FoodConflictException(
                    $"That overlaps your fast {FoodText.Span(overlap.ToFast())}. Change or delete that fast first.");
            }
        }
    }

    private async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? args, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.QueryAsync<T>(new CommandDefinition(sql, args, cancellationToken: cancellationToken));
    }

    private static object OverlapArgs(FastSpan span) => new
    {
        except = span.Except,
        from = span.From.ToUniversalTime(),
        to = span.To?.ToUniversalTime()
    };

    /// <summary>A LIKE pattern for ids starting with <paramref name="prefix"/>: hex and dashes only.</summary>
    private static string IdPrefix(string prefix)
    {
        var clean = new string(prefix.Trim().ToLowerInvariant().Where(c => Uri.IsHexDigit(c) || c == '-').ToArray());
        return clean + "%";
    }

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTime Local(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private sealed class MealRow
    {
        public Guid Id { get; set; }
        public DateTime EatenAt { get; set; }
        public string EatenOn { get; set; } = "";
        public DateTime EatenLocal { get; set; }
        public string? Name { get; set; }
        public int? Score { get; set; }
        public int? Calories { get; set; }
        public int? ProteinG { get; set; }
        public int? CarbsG { get; set; }
        public int? FatG { get; set; }
        public string? MealType { get; set; }
        public Guid? PhotoArtifactId { get; set; }
        public DateTime FirstLoggedAt { get; set; }
        public DateTime LoggedAt { get; set; }
        public int Versions { get; set; }
        public Guid EventId { get; set; }
        public bool Alcoholic { get; set; }

        public Meal ToMeal() => new(
            Id, Utc(EatenAt), ParseDay(EatenOn), Local(EatenLocal), Name ?? "", Score ?? 0, Calories ?? 0,
            ProteinG, CarbsG, FatG, MealType ?? FoodEntries.Snack, PhotoArtifactId, Utc(FirstLoggedAt), Utc(LoggedAt),
            Versions, EventId, Alcoholic);
    }

    private sealed class FastRow
    {
        public Guid Id { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string StartedOn { get; set; } = "";
        public string? EndedOn { get; set; }
        public DateTime StartedLocal { get; set; }
        public DateTime? EndedLocal { get; set; }
        public long? Seconds { get; set; }
        public int MealsDuring { get; set; }
        public DateTime RecordedAt { get; set; }
        public Guid EventId { get; set; }

        public Fast ToFast() => new(
            Id, Utc(StartedAt), EndedAt is { } end ? Utc(end) : null, ParseDay(StartedOn),
            EndedOn is { } on ? ParseDay(on) : null, Local(StartedLocal), EndedLocal is { } el ? Local(el) : null,
            Seconds, MealsDuring, Utc(RecordedAt), EventId);
    }

    private sealed class DrinkDayRow
    {
        public string Day { get; set; } = "";
        public int Drinks { get; set; }
    }

    private sealed class DayRow
    {
        public string Day { get; set; } = "";
        public int Meals { get; set; }
        public long Calories { get; set; }
        public long ScoreCalories { get; set; }
        public long? LongestFastSeconds { get; set; }
        public Guid? LongestFastId { get; set; }
        public Guid? FastEventId { get; set; }
        public bool ClosedByHand { get; set; }
        public DateTime? ClosedAt { get; set; }
        public Guid? CloseEventId { get; set; }
        public bool Closed { get; set; }
        public DateTime? LastRecordedAt { get; set; }
        public Guid[] FoodEventIds { get; set; } = [];
        public int Drinks { get; set; }
        public Guid[] DrinkEventIds { get; set; } = [];

        public FoodDay ToDay() => new(
            ParseDay(Day), Meals, Calories, ScoreCalories, LongestFastSeconds, LongestFastId, FastEventId,
            ClosedByHand, ClosedAt is { } c ? Utc(c) : null, CloseEventId, Closed,
            LastRecordedAt is { } l ? Utc(l) : null, FoodEventIds, Drinks, DrinkEventIds);
    }
}

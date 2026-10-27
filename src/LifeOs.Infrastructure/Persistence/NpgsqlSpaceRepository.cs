using System.Data;
using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Home;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The home's spaces, their ratings and their photos (HOME-1, HOME-2, HOME-4, environment plan
/// E1, E6). Reads go through <c>bsk.v_space</c>, <c>bsk.v_space_rating</c> (migration 0044) and
/// <c>bsk.v_space_photo</c> (0045); a rating or photo version write inserts each event and its
/// <c>concerns</c> edge to the space in one transaction, since the stream is append-only. The ledger repository reads the same views through <see cref="ReadHomeAsync"/>,
/// on its own connection, so every life-domain fold sees the spaces and ratings beside the
/// credit (environment plan §4.6).
/// </summary>
public sealed class NpgsqlSpaceRepository(string connectionString) : ISpaceRepository
{
    // Aliased to the rows' property names (the CLI host doesn't turn on underscore matching);
    // dates as text, since Dapper has no DateOnly mapping in every host. The fold's read
    // (HomeSql) leaves out the shots, so it never needs a database past 0044.
    private const string HomeSql = """
        SELECT id, urn, name, weight, pace, archived_at AS archivedat,
               to_char(archived_on, 'YYYY-MM-DD') AS archivedon, created_at AS createdat
        FROM bsk.v_space
        """;

    private const string SpaceSql = """
        SELECT id, urn, name, weight, pace, archived_at AS archivedat,
               to_char(archived_on, 'YYYY-MM-DD') AS archivedon, created_at AS createdat,
               shots::text AS shots
        FROM bsk.v_space
        """;

    private const string PhotoSql = """
        SELECT id, space_id AS spaceid, shot_id AS shotid, to_char(day, 'YYYY-MM-DD') AS day,
               artifact_id AS artifactid, added_at AS addedat, changed_at AS changedat, event_id AS eventid
        FROM bsk.v_space_photo
        """;

    private const string PhotoOrder = " ORDER BY day, added_at, id";

    private const string RatingSql = """
        SELECT id, space_id AS spaceid, to_char(day, 'YYYY-MM-DD') AS day, tidiness, cleanliness,
               aesthetics, note, rated_at AS ratedat, event_id AS eventid
        FROM bsk.v_space_rating
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

    private const string InsertEdgeSql = """
        INSERT INTO bsk.subject_event (event_id, relation, subject_id, provenance)
        VALUES (@EventId, @Relation, @SubjectId, @Provenance);
        """;

    public async Task<IReadOnlyList<Space>> GetSpacesAsync(CancellationToken cancellationToken = default)
        => (await QueryAsync<SpaceRow>(SpaceSql + " ORDER BY lower(name), id", null, cancellationToken))
            .Select(r => r.ToSpace()).ToList();

    public async Task<Space?> GetSpaceAsync(Guid id, CancellationToken cancellationToken = default)
        => (await QueryAsync<SpaceRow>(SpaceSql + " WHERE id = @id", new { id }, cancellationToken))
            .Select(r => r.ToSpace()).FirstOrDefault();

    public async Task<IReadOnlyList<SpaceRating>> GetRatingsAsync(
        Guid? spaceId = null, DateOnly? from = null, CancellationToken cancellationToken = default)
        => (await QueryAsync<RatingRow>(
                RatingSql + """
                     WHERE (@spaceId::uuid IS NULL OR space_id = @spaceId::uuid)
                       AND (@from::date IS NULL OR day >= @from::date)
                     ORDER BY day, rated_at, id
                    """,
                new { spaceId, from = from is { } f ? Text(f) : null },
                cancellationToken))
            .Select(r => r.ToRating()).ToList();

    public async Task<SpaceRating?> GetRatingAsync(Guid id, CancellationToken cancellationToken = default)
        => (await QueryAsync<RatingRow>(RatingSql + " WHERE id = @id", new { id }, cancellationToken))
            .Select(r => r.ToRating()).FirstOrDefault();

    public async Task<IReadOnlyList<SpaceRating>> FindRatingsAsync(string prefix, CancellationToken cancellationToken = default)
        => (await QueryAsync<RatingRow>(
                RatingSql + " WHERE id::text LIKE @prefix ORDER BY day DESC, rated_at DESC LIMIT 20",
                new { prefix = IdPrefix(prefix) }, cancellationToken))
            .Select(r => r.ToRating()).ToList();

    public async Task<DateTimeOffset> NoonAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var instant = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            "SELECT ((@day::date + time '12:00')::timestamp AT TIME ZONE bsk.home_zone());",
            new { day = Text(day) },
            cancellationToken: cancellationToken));
        return Utc(instant);
    }

    public Task<IReadOnlyList<Guid>> AppendRatingsAsync(
        IReadOnlyList<SpaceRatingWrite> writes, CancellationToken cancellationToken = default)
        => AppendAsync(writes.Select(w => (w.Event, w.SpaceId)).ToList(), cancellationToken);

    public async Task<IReadOnlyList<SpacePhoto>> GetPhotosAsync(Guid? spaceId = null, CancellationToken cancellationToken = default)
        => (await QueryAsync<PhotoRow>(
                PhotoSql + " WHERE (@spaceId::uuid IS NULL OR space_id = @spaceId::uuid)" + PhotoOrder,
                new { spaceId }, cancellationToken))
            .Select(r => r.ToPhoto()).ToList();

    public async Task<SpacePhoto?> GetPhotoAsync(Guid id, CancellationToken cancellationToken = default)
        => (await QueryAsync<PhotoRow>(PhotoSql + " WHERE id = @id", new { id }, cancellationToken))
            .Select(r => r.ToPhoto()).FirstOrDefault();

    public async Task<IReadOnlyList<SpacePhoto>> FindPhotosAsync(string prefix, CancellationToken cancellationToken = default)
        => (await QueryAsync<PhotoRow>(
                PhotoSql + " WHERE id::text LIKE @prefix ORDER BY day DESC, added_at DESC LIMIT 20",
                new { prefix = IdPrefix(prefix) }, cancellationToken))
            .Select(r => r.ToPhoto()).ToList();

    public Task<IReadOnlyList<Guid>> AppendPhotoVersionsAsync(
        IReadOnlyList<SpacePhotoWrite> writes, CancellationToken cancellationToken = default)
        => AppendAsync(writes.Select(w => (w.Event, w.SpaceId)).ToList(), cancellationToken);

    // Each event and its concerns edge to the space, all in one transaction.
    private async Task<IReadOnlyList<Guid>> AppendAsync(
        IReadOnlyList<(NewEvent Event, Guid SpaceId)> writes, CancellationToken cancellationToken)
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
                    SubjectId = write.SpaceId,
                    e.Provenance
                },
                transaction: transaction,
                cancellationToken: cancellationToken));
            ids.Add(eventId);
        }

        await transaction.CommitAsync(cancellationToken);
        return ids;
    }

    /// <summary>
    /// Every space and every live rating, read on <paramref name="connection"/>: what the Home
    /// Score is worked out from. A database without migration 0044 reads as no spaces, so a
    /// host a step ahead of its database still folds every other domain.
    /// </summary>
    internal static async Task<HomeInputs> ReadHomeAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken, IDbTransaction? transaction = null)
    {
        try
        {
            var spaces = await connection.QueryAsync<SpaceRow>(new CommandDefinition(
                HomeSql + " ORDER BY lower(name), id", transaction: transaction, cancellationToken: cancellationToken));
            var ratings = await connection.QueryAsync<RatingRow>(new CommandDefinition(
                RatingSql + " ORDER BY day, rated_at, id", transaction: transaction, cancellationToken: cancellationToken));
            return new HomeInputs(spaces.Select(r => r.ToSpace()).ToList(), ratings.Select(r => r.ToRating()).ToList());
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return HomeInputs.None;
        }
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

    private sealed class SpaceRow
    {
        public Guid Id { get; set; }
        public string Urn { get; set; } = "";
        public string Name { get; set; } = "";
        public int Weight { get; set; }
        public int Pace { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public string? ArchivedOn { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Shots { get; set; }

        public Space ToSpace() => new(
            Id, Urn, Name, Weight, Pace,
            ArchivedAt is { } at ? Utc(at) : null,
            ArchivedOn is { } on ? ParseDay(on) : null,
            Utc(CreatedAt),
            SpaceShotsJson.Parse(Shots));
    }

    private sealed class PhotoRow
    {
        public Guid Id { get; set; }
        public Guid SpaceId { get; set; }
        public string ShotId { get; set; } = "";
        public string Day { get; set; } = "";
        public Guid ArtifactId { get; set; }
        public DateTime AddedAt { get; set; }
        public DateTime ChangedAt { get; set; }
        public Guid EventId { get; set; }

        public SpacePhoto ToPhoto() => new(Id, SpaceId, ShotId, ParseDay(Day), ArtifactId, Utc(AddedAt), Utc(ChangedAt), EventId);
    }

    private sealed class RatingRow
    {
        public Guid Id { get; set; }
        public Guid SpaceId { get; set; }
        public string Day { get; set; } = "";
        public decimal? Tidiness { get; set; }
        public decimal? Cleanliness { get; set; }
        public decimal? Aesthetics { get; set; }
        public string? Note { get; set; }
        public DateTime RatedAt { get; set; }
        public Guid EventId { get; set; }

        public SpaceRating ToRating() => new(
            Id, SpaceId, ParseDay(Day), Normal(Tidiness), Normal(Cleanliness), Normal(Aesthetics), Note, Utc(RatedAt), EventId);

        // 4.0 reads back as 4, so a rating prints the way it was given.
        private static decimal? Normal(decimal? value)
            => value is { } v
                ? decimal.Parse(v.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                : null;
    }
}

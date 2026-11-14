using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Npgsql;

namespace LifeOs.Api.Read;

/// <summary>
/// Every read the API makes, as plain SQL against the <c>bsk_reader</c>
/// flattened views. Ported from <c>LifeOs.Pilot.Reader.SubjectReader</c>.
/// No writes live here by design.
/// </summary>
/// <remarks>
/// Status is folded from <c>bsk_derived.subject_current_source</c> (the live
/// fold of <c>state_change</c> events) rather than the materialized
/// <c>bsk.v_subject_current</c>. Archive is already derived-on-read via
/// <c>bsk.is_archived</c> on <c>v_subject</c>. Together that means a status or
/// archive write is visible on the next read without a <c>bsk rebuild</c>.
/// </remarks>
/// <param name="dataSource">The reader connection's pool (<see cref="ReaderKernel"/>).</param>
/// <param name="settings">The host's settings: the <c>utm_source</c> on a cover's links back to Unsplash (COVER-1).</param>
public sealed class SubjectReader(NpgsqlDataSource dataSource, KernelSettings settings)
{
    private readonly string coverAppName = settings.Integrations?.Unsplash?.AppName ?? Cover.DefaultAppName;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
        => await dataSource.OpenConnectionAsync(cancellationToken);

    /// <summary>Subject types with counts — the Browse tree. Archived items are excluded by default (D9).</summary>
    public async Task<IReadOnlyList<TypeCount>> GetTypeCountsAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var archived = includeArchived ? "" : "WHERE NOT archived";
        return (await db.QueryAsync<TypeCount>(new CommandDefinition($"""
            SELECT type, count(*) AS n FROM bsk.v_subject {archived} GROUP BY type ORDER BY type
            """, cancellationToken: cancellationToken))).AsList();
    }

    public Task<IReadOnlyList<SubjectListItem>> GetSubjectsAsync(string type, bool includeArchived = false, CancellationToken cancellationToken = default)
        => QuerySubjectsAsync(type, includeArchived, cancellationToken: cancellationToken);

    // A list row's columns. A repetition of a repeating Task (D16) carries its series, its date
    // and its series' pattern, and pays its series' price unless it has its own.
    private const string ListSelect = """
        SELECT s.id, s.urn, s.type, s.title,
               c.status,
               s.attributes->>'due' AS due,
               s.attributes->>'scheduled' AS scheduled,
               s.attributes->>'target_date' AS target_date,
               s.area,
               a.name AS area_name,
               (SELECT string_agg(t.tag, ', ' ORDER BY t.tag)
                FROM bsk.v_item_tag t
                WHERE t.item_kind = 'subject' AND t.item_id = s.id) AS tags,
               s.archived,
               s.created_at,
               s.expected_cadence,
               s.next_review_at,
               s.attributes->>'person_kind' AS person_kind,
               (s.attributes->>'vision_order')::int AS vision_order,
               bsk.is_task_series(s.type, s.attributes) AS is_series,
               ser.id AS series_id,
               CASE WHEN ser.id IS NOT NULL THEN s.attributes->>'occurrence' END AS occurrence,
               CASE WHEN bsk.is_task_series(s.type, s.attributes) THEN (s.attributes->'recurrence')::text
                    WHEN ser.id IS NOT NULL THEN (ser.attributes->'recurrence')::text END AS repeat,
               coalesce(s.attributes->>'bounty', ser.attributes->>'bounty') AS bounty_json,
               s.attributes->>'cover' AS cover_json
        FROM bsk.v_subject s
        LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
        LEFT JOIN bsk.v_area a ON a.urn = s.area
        LEFT JOIN bsk.v_subject ser
               ON s.type = 'Task' AND ser.type = 'Task' AND ser.urn = s.attributes->>'series'
        """;

    /// <summary>
    /// Subjects of <paramref name="type"/> (every type when null), by title. Archived ones only
    /// when asked (D9), and a repeating Task's series only when asked: its repetitions are the
    /// Tasks to do (D16).
    /// </summary>
    public Task<IReadOnlyList<SubjectListItem>> QuerySubjectsAsync(
        string? type, bool includeArchived = false, bool includeSeries = false, CancellationToken cancellationToken = default)
        => ListSubjectsAsync(
            new SubjectFilter { Type = type, IncludeArchived = includeArchived, IncludeSeries = includeSeries },
            cancellationToken);

    /// <summary>
    /// The subjects <paramref name="filter"/> picks, filtered, ordered and cut in SQL (code review
    /// CR14), so a list asked for a few items reads only those however much history there is.
    /// By title, or by due date then title when the filter asks.
    /// </summary>
    public async Task<IReadOnlyList<SubjectListItem>> ListSubjectsAsync(
        SubjectFilter filter, CancellationToken cancellationToken = default)
    {
        var status = filter.Status?.Trim();
        var parameters = new
        {
            type = filter.Type,
            includeArchived = filter.IncludeArchived,
            includeSeries = filter.IncludeSeries,
            hidden = filter.HiddenTypes.ToArray(),
            open = string.Equals(status, SubjectFilter.Open, StringComparison.OrdinalIgnoreCase),
            closed = string.Equals(status, SubjectFilter.Closed, StringComparison.OrdinalIgnoreCase),
            status = string.IsNullOrEmpty(status) ? null : status,
            area = filter.Area?.Trim() is { Length: > 0 } area ? area : null,
            tag = filter.Tag?.Trim() is { Length: > 0 } tag ? tag : null,
            dueFrom = filter.DueFrom,
            dueTo = filter.DueTo,
            dayFrom = filter.DueOrScheduledFrom,
            dayTo = filter.DueOrScheduledTo,
            byDue = filter.ByDue,
            limit = filter.Limit,
        };
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<SubjectListItem, BountyText, CoverText, SubjectListItem>(new CommandDefinition(ListSelect + """
            WHERE (@type::text IS NULL OR s.type = @type)
              AND (@includeArchived OR NOT s.archived)
              AND (@includeSeries OR NOT bsk.is_task_series(s.type, s.attributes))
              AND NOT (s.type = ANY(@hidden))
              AND (NOT @open OR NOT bsk.is_terminal_status(c.status))
              AND (NOT @closed OR bsk.is_terminal_status(c.status))
              AND (@open OR @closed OR @status::text IS NULL OR lower(btrim(c.status)) = lower(@status))
              AND (@area::text IS NULL OR lower(btrim(s.area)) = lower(@area) OR lower(btrim(a.name)) = lower(@area))
              AND (@tag::text IS NULL OR EXISTS (
                     SELECT 1 FROM bsk.v_item_tag t
                     WHERE t.item_kind = 'subject' AND t.item_id = s.id AND lower(btrim(t.tag)) = lower(@tag)))
              AND (@dueFrom::text IS NULL OR (s.attributes->>'due') COLLATE "C" >= @dueFrom)
              AND (@dueTo::text IS NULL OR (s.attributes->>'due') COLLATE "C" <= @dueTo)
              AND (@dayFrom::text IS NULL
                   OR (s.attributes->>'due') COLLATE "C" BETWEEN @dayFrom AND @dayTo
                   OR (s.attributes->>'scheduled') COLLATE "C" BETWEEN @dayFrom AND @dayTo)
            ORDER BY (CASE WHEN @byDue THEN s.attributes->>'due' END) COLLATE "C", s.title
            LIMIT @limit
            """, parameters, cancellationToken: cancellationToken),
            WithHeadline,
            splitOn: "bounty_json,cover_json")).AsList();
    }

    /// <summary>
    /// Subjects whose title or one of whose tags contains <paramref name="query"/>, ignoring case
    /// (agent-access plan §4.3, <c>find_items</c>): titles that start with it first, then by
    /// title. Archived ones only when asked; a repeating Task's series is included, since an
    /// agent may be asking about the series itself. At most <paramref name="limit"/> rows.
    /// </summary>
    public async Task<IReadOnlyList<SubjectListItem>> SearchSubjectsAsync(
        string query, string? type = null, bool includeArchived = false, int limit = 50,
        IReadOnlyCollection<string>? hiddenTypes = null, CancellationToken cancellationToken = default)
    {
        var pattern = "%" + EscapeLike(query.Trim()) + "%";
        var prefix = EscapeLike(query.Trim()) + "%";
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<SubjectListItem, BountyText, CoverText, SubjectListItem>(new CommandDefinition(ListSelect + """
            WHERE (@type IS NULL OR s.type = @type)
              AND (@includeArchived OR NOT s.archived)
              AND NOT (s.type = ANY(@hidden))
              AND (s.title ILIKE @pattern
                   OR EXISTS (SELECT 1 FROM bsk.v_item_tag t
                              WHERE t.item_kind = 'subject' AND t.item_id = s.id AND t.tag ILIKE @pattern))
            ORDER BY (s.title ILIKE @prefix) DESC, s.archived, s.title
            LIMIT @limit
            """, new { type, includeArchived, pattern, prefix, limit, hidden = (hiddenTypes ?? []).ToArray() }, cancellationToken: cancellationToken),
            WithHeadline,
            splitOn: "bounty_json,cover_json")).AsList();
    }

    /// <summary>
    /// The command palette's search (code review CR76): subjects with every word of
    /// <paramref name="query"/>, in any order and case, somewhere in their title, type (and its
    /// label, Identity Statement or Person / Agent), URN, area's name or tags. A title equal to the
    /// query comes first, then one starting with it, then one with a word starting with its first
    /// word, then one containing it, then the rest, each archived one after every live one; by title
    /// within each. A repeating Task's series is left out, as the lists leave it. At most
    /// <paramref name="limit"/> rows, and <see cref="SubjectSearchResult.Total"/> counts them all.
    /// </summary>
    public async Task<SubjectSearchResult> FindSubjectsAsync(
        string query, IReadOnlyCollection<string>? types, int limit, CancellationToken cancellationToken = default)
    {
        var q = query.Trim().ToLowerInvariant();
        var words = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return new SubjectSearchResult();
        }

        var parameters = new
        {
            q,
            words,
            prefix = EscapeLike(q) + "%",
            contains = "%" + EscapeLike(q) + "%",
            wordStart = "(^|[[:space:]/:,.()\u2013\u2014-])" + EscapeRegex(words[0]),
            types = types is { Count: > 0 } ? types.ToArray() : null,
            limit,
        };
        const string where = """
            WHERE (@types::text[] IS NULL OR s.type = ANY(@types))
              AND NOT bsk.is_task_series(s.type, s.attributes)
              AND NOT EXISTS (
                    SELECT 1 FROM unnest(@words::text[]) AS w(word)
                    WHERE strpos(lower(concat_ws(' ', s.title, s.type,
                              CASE s.type WHEN 'Value' THEN 'Identity Statement' WHEN 'Person' THEN 'Person / Agent' END,
                              s.urn, a.name,
                              (SELECT string_agg(t.tag, ' ') FROM bsk.v_item_tag t
                               WHERE t.item_kind = 'subject' AND t.item_id = s.id))), w.word) = 0)
            """;
        await using var db = await OpenAsync(cancellationToken);
        var total = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.v_subject s LEFT JOIN bsk.v_area a ON a.urn = s.area " + where,
            parameters, cancellationToken: cancellationToken));
        var matches = (await db.QueryAsync<SubjectListItem, BountyText, CoverText, SubjectListItem>(new CommandDefinition(ListSelect + where + """
            ORDER BY (CASE WHEN lower(s.title) = @q THEN 0
                           WHEN lower(s.title) LIKE @prefix THEN 1
                           WHEN lower(s.title) ~ @wordStart THEN 2
                           WHEN lower(s.title) LIKE @contains THEN 3
                           ELSE 4 END) + (CASE WHEN s.archived THEN 10 ELSE 0 END),
                     lower(s.title), s.title
            LIMIT @limit
            """, parameters, cancellationToken: cancellationToken),
            WithHeadline,
            splitOn: "bounty_json,cover_json")).AsList();
        return new SubjectSearchResult { Matches = matches, Total = total };
    }

    /// <summary>How many items wait in the Inbox (<c>bsk.v_inbox</c>), as <see cref="GetInboxAsync"/> lists them.</summary>
    public async Task<int> CountInboxAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.v_inbox;", cancellationToken: cancellationToken));
    }

    // A regular expression's own characters, matched literally: each ASCII character that isn't a
    // letter or digit gets a backslash (one before a letter would make it an escape).
    private static string EscapeRegex(string text)
        => string.Concat(text.Select(ch => ch < 128 && !char.IsAsciiLetterOrDigit(ch) ? "\\" + ch : ch.ToString()));

    // ILIKE's own wildcards and its escape character, matched literally.
    private static string EscapeLike(string text)
        => text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    public async Task<SubjectListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<SubjectListItem, BountyText, CoverText, SubjectListItem>(new CommandDefinition(ListSelect + " WHERE s.id = @id", new { id }, cancellationToken: cancellationToken),
            WithHeadline,
            splitOn: "bounty_json,cover_json")).FirstOrDefault();
    }

    public Task<SubjectDetail?> GetSubjectAsync(Guid id, CancellationToken cancellationToken = default) => DetailAsync("s.id = @id", new { id }, cancellationToken);

    public Task<SubjectDetail?> GetSubjectByUrnAsync(string urn, CancellationToken cancellationToken = default) => DetailAsync("s.urn = @urn", new { urn }, cancellationToken);

    /// <summary>
    /// The subject an id or a URN names exactly, or null: how a read, or a check made before a
    /// write, looks an item up. A write itself names it to its service, which reads titles and
    /// short ids too (<c>SubjectService.ResolveAsync</c>).
    /// </summary>
    public Task<SubjectDetail?> FindByIdOrUrnAsync(string? reference, CancellationToken cancellationToken = default)
    {
        var key = (reference ?? "").Trim();
        return Guid.TryParse(key, out var id) ? GetSubjectAsync(id, cancellationToken)
            : key.Length > 0 ? GetSubjectByUrnAsync(key, cancellationToken)
            : Task.FromResult<SubjectDetail?>(null);
    }

    /// <summary>One subject's detail, found by <paramref name="where"/>, with its series, price and cover.</summary>
    private async Task<SubjectDetail?> DetailAsync(string where, object parameters, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(cancellationToken);
        var detail = await db.QueryFirstOrDefaultAsync<SubjectDetail>(new CommandDefinition($"""
            SELECT s.id, s.urn, s.type, s.title,
                   c.status,
                   s.attributes->>'due' AS due,
                   s.attributes->>'target_date' AS target_date,
                   s.expected_cadence, s.next_review_at, s.scope, s.statement, s.created_at,
                   s.archived, s.area, a.name AS area_name,
                   s.attributes::text AS attributes,
                   CASE WHEN s.type = 'Person' AND bsk.is_uuid_text(s.attributes->>'photo')
                        THEN (s.attributes->>'photo')::uuid END AS photo_artifact_id
            FROM bsk.v_subject s
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
            LEFT JOIN bsk.v_area a ON a.urn = s.area
            WHERE {where}
            """, parameters, cancellationToken: cancellationToken));
        if (detail is not null)
        {
            var bountyJson = MemberJsonOf(detail.Attributes, "bounty");
            if (detail.Type == SubjectTypes.Task && await RepeatAsync(db, detail.Id, cancellationToken) is { } repeat)
            {
                detail.Repeating = repeat.Read;

                // A repetition without a price of its own pays its series' (D16): the rule
                // ItemPrices holds for the writes, read here on the reader connection.
                if (!repeat.Read.IsSeries && bountyJson is null)
                {
                    bountyJson = repeat.SeriesBountyJson;
                    detail.BountyFromSeries = true;
                }
            }

            detail.Bounty = BountyRows.Lines(detail.Type, bountyJson);
            detail.Cover = CoverOf(detail.Type, MemberJsonOf(detail.Attributes, Cover.AttributeKey));
        }

        return detail;
    }

    /// <summary>The repeating Task a Task is (or belongs to), with the series' price; null for a plain Task.</summary>
    private static async Task<(TaskRepeatRead Read, string? SeriesBountyJson)?> RepeatAsync(
        NpgsqlConnection db, Guid id, CancellationToken cancellationToken)
    {
        var row = await db.QueryFirstOrDefaultAsync<RepeatRow>(new CommandDefinition("""
            SELECT (ts.id = t.id) AS is_series,
                   ts.id AS series_id, ts.urn AS series_urn, ts.title AS series_title,
                   ts.recurrence::text AS recurrence,
                   to_char(ts.start_date, 'YYYY-MM-DD') AS start,
                   ts.status AS series_status, ts.stopped,
                   CASE WHEN ts.id <> t.id THEN t.attributes->>'occurrence' END AS occurrence,
                   ts.open_count, ts.completed_count, ts.missed_count, ts.cancelled_count,
                   to_char(ts.last_completed_on, 'YYYY-MM-DD') AS last_completed_on,
                   to_char(ts.next_due, 'YYYY-MM-DD') AS next_due,
                   to_char(ts.latest_occurrence, 'YYYY-MM-DD') AS latest_occurrence,
                   ser.attributes->>'bounty' AS series_bounty_json
            FROM bsk.v_subject t
            JOIN bsk.v_task_series ts ON ts.id = t.id OR ts.urn = t.attributes->>'series'
            JOIN bsk.v_subject ser ON ser.id = ts.id
            WHERE t.id = @id AND t.type = 'Task'
            LIMIT 1
            """, new { id }, cancellationToken: cancellationToken));
        return row is null
            ? null
            : (new TaskRepeatRead
            {
                IsSeries = row.IsSeries,
                SeriesId = row.SeriesId,
                SeriesUrn = row.SeriesUrn,
                SeriesTitle = row.SeriesTitle,
                Recurrence = row.Recurrence,
                Start = row.Start,
                SeriesStatus = row.SeriesStatus,
                Stopped = row.Stopped,
                Occurrence = row.Occurrence,
                OpenCount = row.OpenCount,
                CompletedCount = row.CompletedCount,
                MissedCount = row.MissedCount,
                CancelledCount = row.CancelledCount,
                LastCompletedOn = row.LastCompletedOn,
                NextDue = row.NextDue,
                LatestOccurrence = row.LatestOccurrence
            }, row.SeriesBountyJson);
    }

    /// <summary>A <see cref="TaskRepeatRead"/> as read, with the series' bounty alongside.</summary>
    private sealed class RepeatRow
    {
        public bool IsSeries { get; set; }
        public Guid SeriesId { get; set; }
        public string SeriesUrn { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string Recurrence { get; set; } = "";
        public string? Start { get; set; }
        public string? SeriesStatus { get; set; }
        public bool Stopped { get; set; }
        public string? Occurrence { get; set; }
        public long OpenCount { get; set; }
        public long CompletedCount { get; set; }
        public long MissedCount { get; set; }
        public long CancelledCount { get; set; }
        public string? LastCompletedOn { get; set; }
        public string? NextDue { get; set; }
        public string? LatestOccurrence { get; set; }
        public string? SeriesBountyJson { get; set; }
    }

    /// <summary>Every repeating Task (D16), by title; archived ones only when asked.</summary>
    public async Task<IReadOnlyList<TaskSeriesRow>> GetTaskSeriesAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return await QuerySeriesAsync(db, includeArchived, id: null, cancellationToken);
    }

    /// <summary>A repeating Task and every repetition it has made, latest first; null when it isn't one.</summary>
    public async Task<TaskSeriesHistory?> GetTaskSeriesHistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var series = (await QuerySeriesAsync(db, includeArchived: true, id, cancellationToken)).FirstOrDefault();
        if (series is null)
        {
            return null;
        }

        var repetitions = (await db.QueryAsync<TaskRepetitionRow>(new CommandDefinition("""
            SELECT id, urn, title,
                   to_char(occurrence, 'YYYY-MM-DD') AS occurrence,
                   to_char(due, 'YYYY-MM-DD') AS due,
                   to_char(scheduled, 'YYYY-MM-DD') AS scheduled,
                   status, closed,
                   to_char(closed_on, 'YYYY-MM-DD') AS closed_on,
                   archived
            FROM bsk.v_task_repetition
            WHERE series_id = @id
            ORDER BY occurrence DESC NULLS LAST, created_at DESC
            """, new { id }, cancellationToken: cancellationToken))).AsList();
        return new TaskSeriesHistory { Series = series, Repetitions = repetitions };
    }

    /// <summary>
    /// Every repetition due from <paramref name="from"/> to <paramref name="to"/> (yyyy-MM-dd) of
    /// an unarchived repeating Task, with its series, in one read (code review CR54): a review's
    /// "Repeating tasks" read each series' whole history before.
    /// </summary>
    public async Task<IReadOnlyList<SeriesRepetitionDue>> GetRepetitionsDueAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<SeriesRepetitionDue>(new CommandDefinition("""
            SELECT r.series_id, s.title AS series_title, r.id,
                   to_char(r.due, 'YYYY-MM-DD') AS due, r.status, r.archived
            FROM bsk.v_task_repetition r
            JOIN bsk.v_subject s ON s.id = r.series_id
            WHERE NOT s.archived AND r.due BETWEEN @from::date AND @to::date
            ORDER BY s.title, r.due
            """, new { from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd") },
            cancellationToken: cancellationToken))).AsList();
    }

    private static async Task<List<TaskSeriesRow>> QuerySeriesAsync(
        NpgsqlConnection db, bool includeArchived, Guid? id, CancellationToken cancellationToken)
        => (await db.QueryAsync<TaskSeriesRow, BountyText, TaskSeriesRow>(new CommandDefinition("""
            SELECT ts.id, ts.urn, ts.title,
                   ts.recurrence::text AS recurrence,
                   to_char(ts.start_date, 'YYYY-MM-DD') AS start,
                   ts.status, ts.archived, ts.stopped,
                   s.area, a.name AS area_name,
                   ts.open_count, ts.completed_count, ts.missed_count, ts.cancelled_count,
                   to_char(ts.last_completed_on, 'YYYY-MM-DD') AS last_completed_on,
                   to_char(ts.next_due, 'YYYY-MM-DD') AS next_due,
                   to_char(ts.latest_occurrence, 'YYYY-MM-DD') AS latest_occurrence,
                   s.attributes->>'bounty' AS bounty_json
            FROM bsk.v_task_series ts
            JOIN bsk.v_subject s ON s.id = ts.id
            LEFT JOIN bsk.v_area a ON a.urn = s.area
            WHERE (@includeArchived OR NOT ts.archived)
              AND (@id::uuid IS NULL OR ts.id = @id::uuid)
            ORDER BY ts.title
            """, new { includeArchived, id }, cancellationToken: cancellationToken),
            (row, bounty) =>
            {
                row.Bounty = BountyRows.Headline(SubjectTypes.Task, bounty?.BountyJson);
                return row;
            },
            splitOn: "bounty_json")).AsList();

    /// <summary>
    /// The raw <paramref name="key"/> member of an attributes document, as
    /// <c>attributes-&gt;&gt;'key'</c> reads it.
    /// </summary>
    private static string? MemberJsonOf(string? attributes, string key)
    {
        if (string.IsNullOrWhiteSpace(attributes))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(attributes) is JsonObject bag
                   && bag[key] is { } member
                ? member.ToJsonString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private SubjectListItem WithHeadline(SubjectListItem item, BountyText? bounty, CoverText? cover)
    {
        item.Bounty = BountyRows.Headline(item.Type, bounty?.BountyJson);
        item.Cover = CoverOf(item.Type, cover?.CoverJson);
        return item;
    }

    /// <summary>A cover as pages read it; null when there's none, it's malformed, or the type can't have one.</summary>
    private CoverRead? CoverOf(string type, string? coverJson)
        => Cover.Allowed(type) ? CoverRead.FromJson(coverJson, coverAppName) : null;

    /// <summary>The split-off <c>bounty_json</c> column of a list read; Dapper hands null when it is.</summary>
    private sealed class BountyText
    {
        public string? BountyJson { get; set; }
    }

    /// <summary>
    /// The split-off <c>cover_json</c> column of a list read. It's its own split, not a second
    /// column on <see cref="BountyText"/>: Dapper hands a null object whenever a split's first
    /// column is null, so a cover sharing the bounty's split would vanish on every subject
    /// without a bounty.
    /// </summary>
    private sealed class CoverText
    {
        public string? CoverJson { get; set; }
    }

    /// <summary>What this subject serves / results in (outgoing edges) — parents in the Map.</summary>
    public async Task<IReadOnlyList<RelationEdge>> GetServesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<RelationEdge>(new CommandDefinition("""
            SELECT r.relation, r.to_urn AS urn, r.to_type AS type, r.to_subject AS subject_id, s.title
            FROM bsk.v_subject_relation r
            JOIN bsk.v_subject s ON s.id = r.to_subject
            WHERE r.from_subject = @id
            ORDER BY r.to_type, r.to_urn
            """, new { id }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>What serves / results in this subject (incoming edges) — children in the Map.</summary>
    public async Task<IReadOnlyList<RelationEdge>> GetServedByAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<RelationEdge>(new CommandDefinition("""
            SELECT r.relation, r.from_urn AS urn, r.from_type AS type, r.from_subject AS subject_id, s.title
            FROM bsk.v_subject_relation r
            JOIN bsk.v_subject s ON s.id = r.from_subject
            WHERE r.to_subject = @id
            ORDER BY r.from_type, r.from_urn
            """, new { id }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// The unarchived items <paramref name="fromIds"/> point at by an edge of
    /// <paramref name="relations"/>: for a few items' "serves" without reading every edge
    /// (code review CR54).
    /// </summary>
    public async Task<IReadOnlyList<AlignmentTarget>> GetAlignmentTargetsAsync(
        IReadOnlyCollection<Guid> fromIds, IReadOnlyCollection<string> relations, CancellationToken cancellationToken = default)
    {
        if (fromIds.Count == 0)
        {
            return [];
        }

        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<AlignmentTarget>(new CommandDefinition("""
            SELECT r.from_subject AS from_id, t.id, t.type, t.title
            FROM bsk.v_subject_relation r
            JOIN bsk.v_subject t ON t.id = r.to_subject
            WHERE r.from_subject = ANY(@fromIds) AND r.relation = ANY(@relations) AND NOT t.archived
            ORDER BY t.title
            """, new { fromIds = fromIds.ToArray(), relations = relations.ToArray() }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<AlignmentEdge>> GetAlignmentEdgesAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<AlignmentEdge>(new CommandDefinition("""
            SELECT r.from_subject AS from_id, r.to_subject AS to_id, r.relation
            FROM bsk.v_subject_relation r
            JOIN bsk.v_subject f ON f.id = r.from_subject
            JOIN bsk.v_subject t ON t.id = r.to_subject
            WHERE @includeArchived OR (NOT f.archived AND NOT t.archived)
            """, new { includeArchived }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<ConcerningEvent>> GetConcerningEventsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<ConcerningEvent>(new CommandDefinition("""
            SELECT se.event_kind AS kind, e.occurred_at, se.event_id, a.content
            FROM bsk.v_subject_event se
            JOIN bsk.v_event e ON e.id = se.event_id
            LEFT JOIN bsk.artifact a ON a.id = e.artifact_id
            WHERE se.subject_id = @id AND se.relation = 'concerns'
            ORDER BY e.occurred_at DESC
            """, new { id }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// The item's journal entries, oldest first; with <paramref name="limit"/>, only the newest
    /// that many, cut in SQL (code review CR54), so an item written about daily reads the same.
    /// </summary>
    public async Task<IReadOnlyList<JournalEntry>> GetJournalAsync(
        Guid id, int? limit = null, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<JournalEntry>(new CommandDefinition("""
            SELECT * FROM (
                SELECT se.event_id, e.occurred_at, coalesce(a.content, '') AS content
                FROM bsk.v_subject_event se
                JOIN bsk.v_event e ON e.id = se.event_id
                LEFT JOIN bsk.artifact a ON a.id = e.artifact_id
                WHERE se.subject_id = @id AND se.relation = 'concerns' AND e.kind = 'journal'
                ORDER BY e.occurred_at DESC
                LIMIT @limit
            ) newest
            ORDER BY occurred_at ASC
            """, new { id, limit }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// What waits in the Inbox, newest first, cut in SQL (code review CR54): at most
    /// <paramref name="limit"/> items, only <paramref name="itemId"/> when given, and none of a
    /// type in <paramref name="hiddenTypes"/>.
    /// </summary>
    public async Task<IReadOnlyList<InboxItem>> GetInboxAsync(
        int? limit = null, Guid? itemId = null, IReadOnlyCollection<string>? hiddenTypes = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<InboxItem, ArtifactRecord?, InboxItem>(new CommandDefinition($"""
            SELECT i.item_id, i.item_kind, i.triaged_at,
                   i.subject_urn, i.subject_type, i.subject_title,
                   i.event_kind, i.event_content,
                   ARRAY(SELECT t.tag FROM bsk.v_item_tag t
                         WHERE t.item_kind = i.item_kind AND t.item_id = i.item_id
                         ORDER BY t.tag) AS tags,
                   {AttachmentColumns}
            FROM bsk.v_inbox i
            {AttachmentJoin("i")}
            WHERE (@itemId::uuid IS NULL OR i.item_id = @itemId)
              AND (i.subject_type IS NULL OR NOT (i.subject_type = ANY(@hidden)))
            ORDER BY i.triaged_at DESC
            LIMIT @limit
            """, new { limit, itemId, hidden = (hiddenTypes ?? []).ToArray() }, cancellationToken: cancellationToken),
            WithAttachment,
            splitOn: "id")).AsList();
    }

    // The file or audio an Inbox item carries (CAP-2 / CAP-4): a raw capture's own, or for an Idea
    // or Problem made from a capture, the earliest note or voice event concerning it that has bytes.
    // needs_transcription comes first so it maps onto the item; the split starts at id.
    private const string AttachmentColumns =
        """
        coalesce(att.needs_transcription, false) AS needs_transcription,
        att.id, att.event_id, att.event_kind, att.filename, att.content_type,
        att.byte_size, att.sha256, att.has_bytes
        """;

    private static string AttachmentJoin(string alias) =>
        $"""
        LEFT JOIN LATERAL (
            SELECT va.id, va.event_id, ce.kind AS event_kind, va.filename, va.content_type,
                   va.byte_size, va.sha256, va.has_bytes,
                   (ce.kind = 'voice' AND coalesce(btrim(ca.content), '') = '') AS needs_transcription
            FROM bsk.event ce
            JOIN bsk.artifact ca ON ca.id = ce.artifact_id
            JOIN bsk.v_artifact va ON va.id = ce.artifact_id AND va.has_bytes
            WHERE ce.kind IN ('note', 'voice')
              AND ((({alias}.item_kind = 'event') AND ce.id = {alias}.item_id)
                   OR ({alias}.item_kind = 'subject' AND ce.id IN (
                         SELECT se.event_id FROM bsk.v_subject_event se
                         WHERE se.subject_id = {alias}.item_id AND se.relation = 'concerns')))
            ORDER BY ce.recorded_at, ce.id
            LIMIT 1
        ) att ON true
        """;

    private static InboxItem WithAttachment(InboxItem item, ArtifactRecord? attachment)
    {
        item.Attachment = attachment;
        return item;
    }

    private static ResolvedInboxItem WithAttachment(ResolvedInboxItem item, ArtifactRecord? attachment)
    {
        item.Attachment = attachment;
        return item;
    }

    /// <summary>
    /// A stored file's metadata by its artifact id, or by the id of the event that carries it.
    /// Null when neither names an artifact with bytes.
    /// </summary>
    public async Task<ArtifactRecord?> GetArtifactAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Two keyed lookups rather than one OR: `va.id = @id OR va.event_id = @id` can use neither
        // key, so it read every artifact and the first event of each (code review CR33).
        await using var db = await OpenAsync(cancellationToken);
        return await db.QueryFirstOrDefaultAsync<ArtifactRecord>(new CommandDefinition("""
            SELECT id, event_id, event_kind, filename, content_type, byte_size, sha256, has_bytes
            FROM (
                SELECT 0 AS pick, va.id, va.event_id, va.kind AS event_kind, va.filename,
                       va.content_type, va.byte_size, va.sha256, va.has_bytes
                FROM bsk.v_artifact va
                WHERE va.id = @id AND va.has_bytes
                UNION ALL
                SELECT 1, va.id, va.event_id, va.kind, va.filename,
                       va.content_type, va.byte_size, va.sha256, va.has_bytes
                FROM bsk.event e
                JOIN bsk.v_artifact va ON va.id = e.artifact_id
                WHERE e.id = @id AND va.event_id = @id AND va.has_bytes
            ) found
            ORDER BY pick
            LIMIT 1
            """, new { id }, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The files on a subject (its Files section): every stored file on an event that concerns
    /// it, oldest first. A cover or a food photo isn't one: those hang off their own events.
    /// </summary>
    public async Task<IReadOnlyList<ArtifactRecord>> GetSubjectFilesAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<ArtifactRecord>(new CommandDefinition("""
            SELECT va.id, va.event_id, va.kind AS event_kind, va.filename, va.content_type,
                   va.byte_size, va.sha256, va.has_bytes
            FROM bsk.v_subject_event se
            JOIN bsk.event e ON e.id = se.event_id
            JOIN bsk.v_artifact va ON va.id = e.artifact_id AND va.has_bytes
            WHERE se.subject_id = @subjectId AND se.relation = 'concerns'
              AND e.kind IN ('note', 'voice')
            ORDER BY e.recorded_at, e.id
            """, new { subjectId }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// Items that have left the inbox, most recently resolved first — the way back to
    /// a dismissed capture. Capped at <paramref name="limit"/> rows.
    /// </summary>
    public async Task<IReadOnlyList<ResolvedInboxItem>> GetResolvedInboxAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<ResolvedInboxItem, ArtifactRecord?, ResolvedInboxItem>(new CommandDefinition($"""
            SELECT r.item_id, r.item_kind, r.triaged_at,
                   r.resolution, r.resolved_at,
                   r.subject_status, r.subject_status_terminal,
                   r.subject_urn, r.subject_type, r.subject_title,
                   r.event_kind, r.event_content,
                   ARRAY(SELECT t.tag FROM bsk.v_item_tag t
                         WHERE t.item_kind = r.item_kind AND t.item_id = r.item_id
                         ORDER BY t.tag) AS tags,
                   {AttachmentColumns}
            FROM bsk.v_inbox_resolved r
            {AttachmentJoin("r")}
            ORDER BY r.resolved_at DESC
            LIMIT @limit
            """, new { limit }, cancellationToken: cancellationToken),
            WithAttachment,
            splitOn: "id")).AsList();
    }

    public async Task<int> GetInboxCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*) FROM bsk.v_inbox", cancellationToken: cancellationToken));
    }

    /// <summary>The item's status changes, newest first; at most <paramref name="limit"/> when given (code review CR54).</summary>
    public async Task<IReadOnlyList<StatusHistoryEntry>> GetStatusHistoryAsync(
        Guid id, int? limit = null, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<StatusHistoryEntry>(new CommandDefinition("""
            SELECT status, occurred_at, id
            FROM bsk.v_event
            WHERE kind = 'state_change' AND subject_id = @id
            ORDER BY occurred_at DESC
            LIMIT @limit
            """, new { id = id.ToString(), limit }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<string>(new CommandDefinition("""
            SELECT tag FROM bsk.v_item_tag
            WHERE item_kind = 'subject' AND item_id = @subjectId
            ORDER BY tag
            """, new { subjectId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<TagUniverseItem>> GetTagUniverseAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<TagUniverseItem>(new CommandDefinition("SELECT tag, item_count FROM bsk.v_tag_universe ORDER BY tag", cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// Every timed Task's timer (TASKS-2), running first, then most recently changed.
    /// Archived tasks are left out, as in every default view (D9).
    /// </summary>
    public async Task<IReadOnlyList<TaskTimer>> GetTimersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<TaskTimer>(new CommandDefinition("""
            SELECT subject_id, subject_urn, subject_title, state, running_since,
                   total_seconds, session_seconds, session_started_at, sessions, last_changed_at
            FROM bsk.v_task_timer
            WHERE NOT archived
            ORDER BY (state = 'running') DESC, (state = 'paused') DESC, last_changed_at DESC
            """, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<TaskTimer?> GetTimerAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return await db.QueryFirstOrDefaultAsync<TaskTimer>(new CommandDefinition("""
            SELECT subject_id, subject_urn, subject_title, state, running_since,
                   total_seconds, session_seconds, session_started_at, sessions, last_changed_at
            FROM bsk.v_task_timer
            WHERE subject_key = @subjectId::text
            """, new { subjectId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AreaRow>> GetAreasAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<AreaRow>(new CommandDefinition("SELECT id, urn, name, description, notes, created_at FROM bsk.v_area ORDER BY name", cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<HabitRow>> GetHabitsAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var rows = (await db.QueryAsync<HabitRow, BountyText, HabitEstablishment, HabitRow>(new CommandDefinition("""
            SELECT h.id, h.urn, h.name, h.cue, h.routine, h.reward,
                   h.start_date, h.end_date, h.allows_partial,
                   h.recurrence::text AS recurrence, h.archived, h.created_at,
                   coalesce(st.current_streak, 0) AS current_streak,
                   st.last_state,
                   c.status,
                   s.attributes->>'bounty' AS bounty_json,
                   e.min_adherence, e.window_days, e.window_start::text AS window_start,
                   e.followed, e.partial, e.missed, e.concluded, e.adherence,
                   e.age_days, e.target_met
            FROM bsk.v_habit h
            JOIN bsk.v_subject s ON s.id = h.id
            LEFT JOIN bsk.v_habit_streak st ON st.habit_id = h.id
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
            LEFT JOIN bsk.v_habit_establishment e ON e.habit_id = h.id
            WHERE @includeArchived OR NOT h.archived
            ORDER BY h.name
            """, new { includeArchived }, cancellationToken: cancellationToken),
            (habit, bounty, establishment) =>
            {
                habit.Bounty = BountyRows.Headline(SubjectTypes.Habit, bounty?.BountyJson);
                habit.Establishment = establishment;
                return habit;
            },
            splitOn: "bounty_json,min_adherence")).AsList();
        foreach (var row in rows)
        {
            row.LastState = MapAdherenceState(row.LastState);
        }

        return rows;
    }

    public async Task<IReadOnlyList<HabitOccurrenceRow>> GetHabitOccurrencesAsync(
        Guid? habitId = null, DateOnly? on = null, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var onText = on?.ToString("yyyy-MM-dd");
        var fromText = from?.ToString("yyyy-MM-dd");
        var toText = to?.ToString("yyyy-MM-dd");
        var rows = (await db.QueryAsync<HabitOccurrenceRow>(new CommandDefinition("""
            SELECT o.habit_id, o.habit_urn, h.name AS habit_name,
                   o.occurrence_date, o.state, h.allows_partial,
                   nullif(btrim(ae.payload->>'note'), '') AS note
            FROM bsk.v_habit_occurrence o
            JOIN bsk.v_habit h ON h.id = o.habit_id
            LEFT JOIN bsk.v_event ae ON ae.id = o.adherence_event_id
            WHERE (@habitId IS NULL OR o.habit_id = @habitId)
              AND (@onText IS NULL OR o.occurrence_date = @onText::date)
              AND (@fromText IS NULL OR o.occurrence_date >= @fromText::date)
              AND (@toText IS NULL OR o.occurrence_date <= @toText::date)
            ORDER BY o.occurrence_date DESC, h.name
            """, new { habitId, onText, fromText, toText }, cancellationToken: cancellationToken))).AsList();
        foreach (var row in rows)
        {
            row.State = MapAdherenceState(row.State) ?? row.State;
        }

        return rows;
    }

    public async Task<IReadOnlyList<AppointmentRow>> GetAppointmentsAsync(bool includeArchived = false, string? onIso = null, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<AppointmentRow>(new CommandDefinition("""
            SELECT id, urn, title, date, start_time, end_time, all_day,
                   location, meeting_link, area, series_urn,
                   recurrence::text AS recurrence, status, archived, created_at
            FROM bsk.v_appointment
            WHERE (@includeArchived OR NOT archived)
              AND (@onIso IS NULL OR date = @onIso)
            ORDER BY date NULLS LAST, start_time NULLS LAST, title
            """, new { includeArchived, onIso }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// The unarchived appointments dated from <paramref name="from"/> to <paramref name="to"/>
    /// (yyyy-MM-dd), a series' occurrences among them, by day and start time.
    /// </summary>
    public async Task<IReadOnlyList<AppointmentRow>> GetAppointmentsBetweenAsync(
        string from, string to, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<AppointmentRow>(new CommandDefinition("""
            SELECT id, urn, title, date, start_time, end_time, all_day,
                   location, meeting_link, area, series_urn,
                   recurrence::text AS recurrence, status, archived, created_at
            FROM bsk.v_appointment
            WHERE NOT archived AND date COLLATE "C" BETWEEN @from AND @to
            ORDER BY date NULLS LAST, start_time NULLS LAST, title
            """, new { from, to }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// The appointments on one day (CAL-1's "Today's Appointments"): one-time appointments and a
    /// series' occurrences, never the series itself, whose pattern makes those occurrences.
    /// All-day ones first, then by start time.
    /// </summary>
    public async Task<IReadOnlyList<AppointmentRow>> GetAppointmentsOnAsync(string day, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<AppointmentRow>(new CommandDefinition("""
            SELECT id, urn, title, date, start_time, end_time, all_day,
                   location, meeting_link, area, series_urn,
                   recurrence::text AS recurrence, status, archived, created_at
            FROM bsk.v_appointment
            WHERE NOT archived AND date = @day AND recurrence IS NULL
            ORDER BY (lower(coalesce(all_day, '')) IN ('true', 't')) DESC, start_time NULLS LAST, title
            """, new { day }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<PersonRow>> GetPeopleAsync(
        bool includeArchived = false, string? kind = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<PersonRow>(new CommandDefinition("""
            SELECT s.id, s.urn, s.title,
                   s.attributes->>'person_kind' AS person_kind,
                   s.attributes->>'role' AS role,
                   s.archived,
                   CASE WHEN bsk.is_uuid_text(s.attributes->>'photo') THEN (s.attributes->>'photo')::uuid END AS photo_artifact_id
            FROM bsk.v_subject s
            WHERE s.type = 'Person'
              AND (@includeArchived OR NOT s.archived)
              AND (@kind IS NULL OR lower(coalesce(s.attributes->>'person_kind', 'human')) = lower(@kind))
            ORDER BY s.title
            LIMIT @limit
            """, new { includeArchived, kind, limit }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<PersonAssociationRow>> GetInvolvementsAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<PersonAssociationRow>(new CommandDefinition("""
            SELECT subject_id, subject_urn, subject_type, subject_title, role,
                   person_id, person_urn, person_name
            FROM bsk.v_person_association
            WHERE person_id = @personId
            ORDER BY subject_type, subject_title
            """, new { personId }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>What each of <paramref name="personIds"/> is involved in, in one read (code review CR14).</summary>
    public async Task<ILookup<Guid, PersonAssociationRow>> GetInvolvementsAsync(
        IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<PersonAssociationRow>(new CommandDefinition("""
            SELECT subject_id, subject_urn, subject_type, subject_title, role,
                   person_id, person_urn, person_name
            FROM bsk.v_person_association
            WHERE person_id = ANY(@ids)
            ORDER BY subject_type, subject_title
            """, new { ids = personIds.ToArray() }, cancellationToken: cancellationToken)))
            .ToLookup(r => r.PersonId);
    }

    public async Task<IReadOnlyList<PersonAssociationRow>> GetPeopleOnAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        return (await db.QueryAsync<PersonAssociationRow>(new CommandDefinition("""
            SELECT subject_id, subject_urn, subject_type, subject_title, role,
                   person_id, person_urn, person_name
            FROM bsk.v_person_association
            WHERE subject_id = @subjectId
            ORDER BY role, person_name
            """, new { subjectId }, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// The UI's <c>AdherenceState</c> uses <c>not_followed</c> where the kernel
    /// stores <c>missed</c>. Map at the JSON edge so the contract matches
    /// <c>production-ui-types.ts</c>.
    /// </summary>
    private static string? MapAdherenceState(string? state)
        => string.Equals(state, "missed", StringComparison.OrdinalIgnoreCase)
            ? "not_followed"
            : state;
}

/// <summary>
/// Which subjects <see cref="SubjectReader.ListSubjectsAsync"/> reads. Every filter left null
/// passes everything; dates are yyyy-MM-dd and compare as text, as the attributes hold them.
/// </summary>
public sealed record SubjectFilter
{
    /// <summary>The <see cref="Status"/> for any status that isn't finished.</summary>
    public const string Open = "open";

    /// <summary>The <see cref="Status"/> for any finished status.</summary>
    public const string Closed = "closed";

    /// <summary>The canonical type, or null for every type.</summary>
    public string? Type { get; init; }

    public bool IncludeArchived { get; init; }

    /// <summary>Include a repeating Task's series; its repetitions are the Tasks to do (D16).</summary>
    public bool IncludeSeries { get; init; }

    /// <summary>Types left out whatever else is asked (an agent's switched-off areas).</summary>
    public IReadOnlyCollection<string> HiddenTypes { get; init; } = [];

    /// <summary><see cref="Open"/>, <see cref="Closed"/>, or a status as the type spells it, in any case.</summary>
    public string? Status { get; init; }

    /// <summary>An area's URN or name, in any case.</summary>
    public string? Area { get; init; }

    /// <summary>A tag the subject carries, in any case.</summary>
    public string? Tag { get; init; }

    /// <summary>Due on or after this day.</summary>
    public string? DueFrom { get; init; }

    /// <summary>Due on or before this day.</summary>
    public string? DueTo { get; init; }

    /// <summary>With <see cref="DueOrScheduledTo"/>: due or scheduled from this day to that one.</summary>
    public string? DueOrScheduledFrom { get; init; }

    public string? DueOrScheduledTo { get; init; }

    /// <summary>By due date, then title; else by title.</summary>
    public bool ByDue { get; init; }

    /// <summary>At most this many rows; null for all.</summary>
    public int? Limit { get; init; }
}

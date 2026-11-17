using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The Primary focus through <c>bsk.v_focus</c> (TODAY-1, D6, migration 0074), with its Goal and
/// review as they are now. Read-only: <c>FocusService</c> writes the focus event. The API reads
/// through one of these on the reader connection.
/// </summary>
public sealed class NpgsqlFocusRepository(NpgsqlDataSource dataSource) : IFocusRepository
{
    // Aliased to the row's property names (the CLI host doesn't turn on underscore matching).
    // v_focus filters on level, its partition key, so the lookup goes below its window.
    private const string FocusSql = """
        SELECT f.level, f.goal_id AS goalid, g.urn AS goalurn, g.title AS goaltitle,
               gs.status AS goalstatus,
               CASE WHEN f.goal_id IS NULL THEN false ELSE bsk.is_archived(f.goal_id) END AS goalarchived,
               f.review_id AS reviewid, r.title AS reviewtitle, f.chosen_at AS chosenat, f.event_id AS eventid
        FROM bsk.v_focus f
        LEFT JOIN bsk.subject g ON g.id = f.goal_id
        LEFT JOIN bsk_derived.subject_current_source gs ON gs.subject_id = f.goal_id
        LEFT JOIN bsk.subject r ON r.id = f.review_id
        WHERE f.level = @level
        """;

    private const string StateSql = """
        SELECT (SELECT status FROM bsk_derived.subject_current_source WHERE subject_id = @id) AS status,
               bsk.is_archived(@id) AS archived
        """;

    public async Task<ChosenFocus?> GetAsync(string level, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<FocusRow>(
            new CommandDefinition(FocusSql, new { level }, cancellationToken: cancellationToken));
        return row?.ToFocus();
    }

    public async Task<(string? Status, bool Archived)> GetStateAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<StateRow>(
            new CommandDefinition(StateSql, new { id = subjectId }, cancellationToken: cancellationToken));
        return (row.Status, row.Archived);
    }

    private sealed class StateRow
    {
        public string? Status { get; set; }
        public bool Archived { get; set; }
    }

    private sealed class FocusRow
    {
        public string Level { get; set; } = "";
        public Guid? GoalId { get; set; }
        public string? GoalUrn { get; set; }
        public string? GoalTitle { get; set; }
        public string? GoalStatus { get; set; }
        public bool GoalArchived { get; set; }
        public Guid? ReviewId { get; set; }
        public string? ReviewTitle { get; set; }
        public DateTime ChosenAt { get; set; }
        public Guid EventId { get; set; }

        public ChosenFocus ToFocus() => new(
            Level, GoalId, GoalUrn, GoalTitle, GoalStatus, GoalArchived, ReviewId, ReviewTitle,
            new DateTimeOffset(DateTime.SpecifyKind(ChosenAt, DateTimeKind.Utc)), EventId);
    }
}

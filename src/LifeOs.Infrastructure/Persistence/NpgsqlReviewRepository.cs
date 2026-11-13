using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Reviews through <c>bsk.v_review</c> (REVIEW-1, REVIEW-2, migration 0052). Read-only: opening
/// and completing a review go through the subject and status services. The API reads through one
/// of these on the reader connection.
/// </summary>
public sealed class NpgsqlReviewRepository(NpgsqlDataSource dataSource) : IReviewRepository
{
    // Aliased to the row's property names (the CLI host doesn't turn on underscore matching);
    // days as text, since Dapper has no DateOnly mapping in every host.
    private const string ReviewSql = """
        SELECT id, urn, title, kind, to_char(period, 'YYYY-MM-DD') AS period, status,
               keep_focus AS keepfocus, completed_at AS completedat,
               first_completed_at AS firstcompletedat,
               to_char(first_completed_on, 'YYYY-MM-DD') AS firstcompletedon,
               archived, created_at AS createdat,
               reflection_path AS reflectionpath, reflection_vault AS reflectionvault,
               reflection_summary AS reflectionsummary, reflection_at AS reflectionat
        FROM bsk.v_review
        """;

    public async Task<Review?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => (await QueryAsync(ReviewSql + " WHERE id = @id", new { id }, cancellationToken)).FirstOrDefault();

    public async Task<Review?> FindAsync(string kind, DateOnly period, CancellationToken cancellationToken = default)
        => (await QueryAsync(
            ReviewSql + " WHERE kind = @kind AND period = @period::date",
            new { kind, period = Text(period) }, cancellationToken)).FirstOrDefault();

    public Task<IReadOnlyList<Review>> ListAsync(
        string? kind = null, DateOnly? from = null, DateOnly? to = null, bool includeArchived = false,
        CancellationToken cancellationToken = default)
        => QueryAsync(
            ReviewSql + """
                 WHERE (@kind::text IS NULL OR kind = @kind)
                   AND (@from::date IS NULL OR period >= @from::date)
                   AND (@to::date IS NULL OR period <= @to::date)
                   AND (@includeArchived OR NOT archived)
                 ORDER BY period, CASE kind WHEN 'daily' THEN 0 ELSE 1 END, id
                """,
            new
            {
                kind,
                from = from is { } f ? Text(f) : null,
                to = to is { } t ? Text(t) : null,
                includeArchived
            },
            cancellationToken);

    private async Task<IReadOnlyList<Review>> QueryAsync(string sql, object args, CancellationToken cancellationToken)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ReviewRow>(new CommandDefinition(sql, args, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToReview()).ToList();
    }

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class ReviewRow
    {
        public Guid Id { get; set; }
        public string Urn { get; set; } = "";
        public string Title { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Period { get; set; } = "";
        public string? Status { get; set; }
        public bool KeepFocus { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FirstCompletedAt { get; set; }
        public string? FirstCompletedOn { get; set; }
        public bool Archived { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ReflectionPath { get; set; }
        public string? ReflectionVault { get; set; }
        public string? ReflectionSummary { get; set; }
        public DateTime? ReflectionAt { get; set; }

        public Review ToReview() => new(
            Id, Urn, Title, Kind, ParseDay(Period), Status, KeepFocus,
            CompletedAt is { } c ? Utc(c) : null,
            FirstCompletedAt is { } f ? Utc(f) : null,
            FirstCompletedOn is { } on ? ParseDay(on) : null,
            Archived,
            Utc(CreatedAt),
            // A link is whole or not there: one missing a part, written by hand, reads as none.
            ReflectionPath is { Length: > 0 } path && ReflectionVault is { Length: > 0 } vault
                && ReflectionSummary is { } summary && ReflectionAt is { } at
                ? new ReviewReflection(path, vault, summary, Utc(at))
                : null);
    }
}

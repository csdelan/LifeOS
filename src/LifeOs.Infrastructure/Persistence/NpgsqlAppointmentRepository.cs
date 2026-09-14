using Dapper;
using LifeOs.Application.Abstractions;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Computes which occurrence dates of a recurring appointment series are not yet
/// materialized: expand the series' recurrence (from its start through the requested
/// horizon) and drop any date that already exists as an occurrence subject
/// (<c>attributes.series</c> = the series urn, <c>attributes.date</c> = the date).
/// </summary>
public sealed class NpgsqlAppointmentRepository(string connectionString) : IAppointmentRepository
{
    public async Task<IReadOnlyList<DateOnly>> PendingOccurrenceDatesAsync(
        Guid seriesId, string seriesUrn, DateOnly through, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Anchor and window start at the series' start_date (or its creation date).
        // start_date is the series anchor; start/end are the time-of-day fields (CAL-1).
        const string sql = """
            SELECT to_char(d, 'YYYY-MM-DD')
            FROM bsk.subject s
            CROSS JOIN LATERAL (
                SELECT coalesce(bsk.try_to_date(s.attributes->>'start_date'), s.created_at::date) AS start_date
            ) anchor
            CROSS JOIN LATERAL bsk.recurrence_occurrences(
                s.attributes->'recurrence', anchor.start_date, anchor.start_date, @through::date) AS d
            WHERE s.id = @seriesId
              AND NOT EXISTS (
                  SELECT 1 FROM bsk.subject o
                  WHERE o.type = 'Appointment'
                    AND o.attributes->>'series' = @seriesUrn
                    AND o.attributes->>'date' = to_char(d, 'YYYY-MM-DD'))
            ORDER BY d;
            """;

        var dates = await connection.QueryAsync<string>(new CommandDefinition(
            sql,
            new { seriesId, seriesUrn, through = through.ToString("yyyy-MM-dd") },
            cancellationToken: cancellationToken));
        return dates.Select(d => DateOnly.ParseExact(d, "yyyy-MM-dd")).ToList();
    }
}

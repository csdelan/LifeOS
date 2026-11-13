using System.Data;
using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The life-domain credits a fold reads (<c>bsk.v_award_credit</c>, xp-domains plan §3.4), with
/// the exception days (<c>bsk.v_exception_day</c>) and the Home's spaces and ratings
/// (environment plan §4.6) beside them. It reads only; the ledger's writes are
/// <see cref="NpgsqlLedgerRepository"/>'s (code review CR47).
/// <para>
/// <paramref name="homeTimeZone"/> (an IANA name, <see cref="HomeZone"/>) puts each credit on a
/// local day: its own credited day when it has one, else its instant read in that zone.
/// </para>
/// <para>
/// Today and the last settled day are <paramref name="clock"/>'s, read in that zone, the way
/// <see cref="NpgsqlHomeCalendar"/> reads them, not the database's <c>now()</c> (code review
/// CR62): the services that mark an exception day or settle a fold ask the home calendar, and
/// the two disagreed whenever the clocks did, as a test's <c>TestClock</c> always does.
/// </para>
/// </summary>
public sealed class NpgsqlDomainCreditReader(NpgsqlDataSource dataSource, IClock clock, string homeTimeZone = HomeZone.Default)
    : IDomainCreditReader
{
    public async Task<DomainCredit> GetDomainDaysAsync(
        IReadOnlyCollection<string> currencies, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Aliased to CreditRow's names (no underscore matching in the CLI host). A passive
        // award's key starts with its trigger (sleep:2026-10-24, §7.4.2), and no item trigger
        // shares a passive trigger's name, so the key's first part says which base is passive.
        const string daysSql = """
            SELECT c.currency,
                   to_char(coalesce(c.credited_on, (c.occurred_at AT TIME ZONE @tz)::date), 'YYYY-MM-DD') AS day,
                   sum(c.base)::bigint AS base,
                   sum(c.paid)::bigint AS paid,
                   coalesce(sum(c.base) FILTER (WHERE split_part(c.award_key, ':', 1) = ANY(@passive)), 0)::bigint AS passive
            FROM bsk.v_award_credit c
            WHERE c.currency = ANY(@currencies)
            GROUP BY 1, 2
            ORDER BY 1, 2;
            """;

        try
        {
            // Every statement reads one snapshot (code review CR47): the dates, the credit, the
            // exception days and the Home, so a write landing between them can't hand a fold
            // inputs from two moments. The dates are the kernel clock's, read once (CR62).
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

            // The last settled day: yesterday once it's past the settle hour in the home zone,
            // the day before until then.
            var dates = await connection.QuerySingleAsync<DatesRow>(new CommandDefinition(
                """
                SELECT to_char((@now AT TIME ZONE @tz)::date, 'YYYY-MM-DD') AS today,
                       to_char((bsk.ledger_opened_at() AT TIME ZONE @tz)::date, 'YYYY-MM-DD') AS openedon,
                       to_char(((@now AT TIME ZONE @tz) - make_interval(hours => @settle))::date - 1, 'YYYY-MM-DD') AS settledthrough;
                """,
                new { now = clock.UtcNow.ToUniversalTime(), tz = homeTimeZone, settle = LifeDomains.SettleHour },
                transaction, cancellationToken: cancellationToken));
            var rows = await connection.QueryAsync<CreditRow>(new CommandDefinition(
                daysSql,
                new { tz = homeTimeZone, currencies = currencies.ToArray(), passive = LifeDomains.PassiveTriggers.ToArray() },
                transaction, cancellationToken: cancellationToken));
            var exceptions = await connection.QueryAsync<ExceptionRow>(new CommandDefinition(
                """
                SELECT to_char(x.day, 'YYYY-MM-DD') AS day, x.comment, x.marked_at AS markedat
                FROM bsk.v_exception_day x
                ORDER BY x.day;
                """,
                transaction: transaction, cancellationToken: cancellationToken));
            var home = await NpgsqlSpaceRepository.ReadHomeAsync(connection, cancellationToken, transaction);
            await transaction.CommitAsync(cancellationToken);
            return new DomainCredit(
                ParseDay(dates.Today),
                rows.Select(r => new CreditDay(r.Currency, ParseDay(r.Day), r.Base, r.Paid, r.Passive)).ToList(),
                dates.OpenedOn is { } openedOn ? ParseDay(openedOn) : null)
            {
                ExceptionDays = exceptions.Select(ToExceptionDay).ToList(),
                SettledThrough = ParseDay(dates.SettledThrough),
                Home = home
            };
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InvalidParameterValue)
        {
            throw UnknownHomeZone(e);
        }
    }

    public async Task<(DateOnly Today, IReadOnlyList<ExceptionDay> Days)> GetExceptionDaysAsync(
        int days, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            var today = ParseDay(await connection.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT to_char((@now AT TIME ZONE @tz)::date, 'YYYY-MM-DD');",
                new { now = clock.UtcNow.ToUniversalTime(), tz = homeTimeZone }, cancellationToken: cancellationToken)));
            var rows = await connection.QueryAsync<ExceptionRow>(new CommandDefinition(
                """
                SELECT to_char(x.day, 'YYYY-MM-DD') AS day, x.comment, x.marked_at AS markedat
                FROM bsk.v_exception_day x
                WHERE x.day BETWEEN @from::date AND @to::date
                ORDER BY x.day DESC;
                """,
                new
                {
                    from = today.AddDays(1 - days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    to = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                cancellationToken: cancellationToken));
            return (today, rows.Select(ToExceptionDay).ToList());
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InvalidParameterValue)
        {
            throw UnknownHomeZone(e);
        }
    }

    private static ExceptionDay ToExceptionDay(ExceptionRow row)
        => new(ParseDay(row.Day), row.Comment, new DateTimeOffset(DateTime.SpecifyKind(row.MarkedAt, DateTimeKind.Utc)));

    public async Task<Guid?> GetLatestCreditEventAsync(
        string currency, DateOnly day, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                """
                SELECT c.event_id
                FROM bsk.v_award_credit c
                CROSS JOIN LATERAL (SELECT coalesce(c.credited_on, (c.occurred_at AT TIME ZONE @tz)::date) AS day) d
                WHERE c.currency = @currency AND d.day <= @day::date
                ORDER BY d.day DESC, c.occurred_at DESC, c.event_id DESC
                LIMIT 1;
                """,
                new { tz = homeTimeZone, currency, day = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InvalidParameterValue)
        {
            throw UnknownHomeZone(e);
        }
    }

    // `time zone "…" not recognized`: the configured home zone is well formed but unknown.
    private InvalidOperationException UnknownHomeZone(PostgresException e) => NpgsqlHomeCalendar.UnknownZone(homeTimeZone, e);

    // Days come back as text: Dapper has no DateOnly mapping in every host.
    private static DateOnly ParseDay(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class DatesRow
    {
        public string Today { get; set; } = "";
        public string? OpenedOn { get; set; }
        public string SettledThrough { get; set; } = "";
    }

    private sealed class CreditRow
    {
        public string Currency { get; set; } = "";
        public string Day { get; set; } = "";
        public long Base { get; set; }
        public long Paid { get; set; }
        public long Passive { get; set; }
    }

    private sealed class ExceptionRow
    {
        public string Day { get; set; } = "";
        public string Comment { get; set; } = "";
        public DateTime MarkedAt { get; set; }
    }
}

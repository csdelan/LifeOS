using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// GEN-3 / REWARD-2 (migration 0033): the habit views' "today" is the date in the home zone
/// (<c>bsk.home_today()</c>), not the database's UTC date. Before 0033, from about 5 pm
/// Pacific today's unlogged habits read as missed and the current streak fell to 0.
/// <para>
/// The tests' database pins <c>bsk.home_zone</c> to UTC (<see cref="PostgresFixture"/>); each
/// test here sets another zone on its own transaction, where <c>now()</c>, and so today, holds
/// still. UTC+14 (Kiritimati) and UTC−11 (Pago Pago) are at least a day apart at any instant:
/// Pago Pago's today is always over in Kiritimati, which is the evening case.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HabitTodayTests(PostgresFixture postgres)
{
    private const string Kiritimati = "Pacific/Kiritimati";
    private const string PagoPago = "Pacific/Pago_Pago";

    private static readonly DateOnly TodayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Kernel()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    /// <summary>A session whose home zone is <paramref name="zone"/> (null: the database's), for one transaction.</summary>
    private sealed class Session(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public Task<T?> ScalarAsync<T>(string sql, object? args = null, CancellationToken ct = default)
            => connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, args, transaction, cancellationToken: ct));

        public async Task<List<T>> QueryAsync<T>(string sql, object? args = null, CancellationToken ct = default)
            => (await connection.QueryAsync<T>(new CommandDefinition(sql, args, transaction, cancellationToken: ct))).ToList();

        public async Task<DateOnly> TodayAsync(CancellationToken ct)
            => DateOnly.Parse((await ScalarAsync<string>("SELECT to_char(bsk.home_today(), 'YYYY-MM-DD');", ct: ct))!);

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private async Task<Session> SessionAsync(string? zone)
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var transaction = await connection.BeginTransactionAsync(Ct);
        if (zone is not null)
        {
            // Local to the transaction, so it never leaks into a pooled connection.
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT set_config('bsk.home_zone', @zone, true);", new { zone }, transaction, cancellationToken: Ct));
        }

        return new Session(connection, transaction);
    }

    private sealed record OccurrenceRow(string Day, string State);

    private static Task<List<OccurrenceRow>> OccurrencesAsync(Session session, Guid habitId, CancellationToken ct)
        => session.QueryAsync<OccurrenceRow>(
            """
            SELECT to_char(occurrence_date, 'YYYY-MM-DD') AS Day, state AS State
            FROM bsk.v_habit_occurrence WHERE habit_id = @habitId ORDER BY occurrence_date;
            """,
            new { habitId }, ct);

    private static Task<long> StreakAsync(Session session, Guid habitId, CancellationToken ct)
        => session.ScalarAsync<long>(
            "SELECT coalesce(max(current_streak), 0) FROM bsk.v_habit_streak WHERE habit_id = @habitId;",
            new { habitId }, ct);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd");

    private Task<SubjectRef> HabitAsync(ServiceProvider kernel, object attributes)
        => kernel.CreateTrackedHabitAsync(
            $"home-today habit {Guid.NewGuid():N}", JsonSerializer.Serialize(attributes), Ct);

    // With no status, so the status change written days back is its newest.
    private Task<SubjectRef> HabitWithoutStatusAsync(ServiceProvider kernel, object attributes)
        => kernel.CreateHabitWithoutStatusAsync(
            $"home-today habit {Guid.NewGuid():N}", JsonSerializer.Serialize(attributes), Ct);

    [Fact]
    public async Task Today_is_the_date_in_the_home_zone_and_an_unset_zone_means_pacific()
    {
        await using (var unset = await SessionAsync(""))
        {
            Assert.Equal(HomeZone.Default, await unset.ScalarAsync<string>("SELECT bsk.home_zone();", ct: Ct));
        }

        await using (var database = await SessionAsync(null))
        {
            Assert.Equal(PostgresFixture.HabitViewZone, await database.ScalarAsync<string>("SELECT bsk.home_zone();", ct: Ct));
        }

        await using var ahead = await SessionAsync(Kiritimati);
        await using var behind = await SessionAsync(PagoPago);
        var (aheadToday, behindToday) = (await ahead.TodayAsync(Ct), await behind.TodayAsync(Ct));
        Assert.InRange(aheadToday.DayNumber - behindToday.DayNumber, 1, 2);

        // The views and the app's calendar (which dates check-offs) agree on the day.
        var calendar = new NpgsqlHomeCalendar(postgres.ConnectionString, Kiritimati);
        Assert.Contains(aheadToday, new[]
        {
            await calendar.DateAtAsync(DateTimeOffset.UtcNow.AddMinutes(-1), Ct),
            await calendar.DateAtAsync(DateTimeOffset.UtcNow, Ct)
        });
    }

    [Fact]
    public async Task An_unlogged_day_reads_as_missed_only_once_its_over_in_the_home_zone()
    {
        await using var kernel = Kernel();
        var start = TodayUtc.AddDays(-4);
        var habit = await HabitAsync(kernel, new { start = Iso(start), recurrence = new { freq = "daily" } });
        await using var behind = await SessionAsync(PagoPago);
        var behindToday = await behind.TodayAsync(Ct);

        // Followed every day up to Pago Pago's yesterday; its today isn't logged yet.
        var adherence = kernel.GetRequiredService<AdherenceService>();
        for (var day = start; day < behindToday; day = day.AddDays(1))
        {
            await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, day, cancellationToken: Ct);
        }

        var followedDays = behindToday.DayNumber - start.DayNumber;

        // Where it's still that day, the day is open and the streak stands.
        var open = await OccurrencesAsync(behind, habit.Id, Ct);
        Assert.Equal(new OccurrenceRow(Iso(behindToday), "unrecorded"), open[^1]);
        Assert.Equal(followedDays, await StreakAsync(behind, habit.Id, Ct));

        // Where that day is over, it's missed and the streak has broken.
        await using var ahead = await SessionAsync(Kiritimati);
        var aheadToday = await ahead.TodayAsync(Ct);
        var over = await OccurrencesAsync(ahead, habit.Id, Ct);
        Assert.Equal(new OccurrenceRow(Iso(aheadToday), "unrecorded"), over[^1]);
        Assert.Contains(new OccurrenceRow(Iso(behindToday), "missed"), over);
        Assert.Equal(0, await StreakAsync(ahead, habit.Id, Ct));
    }

    [Fact]
    public async Task A_habit_graduates_on_its_status_changes_date_in_the_home_zone()
    {
        await using var kernel = Kernel();
        var habit = await HabitWithoutStatusAsync(kernel, new { start = "2003-02-01", recurrence = new { freq = "daily" } });
        // 06:30 UTC on 15 February 2003 is 10:30 pm on the 14th in Pacific time. Long before
        // the wallet opened, so it pays nothing.
        await kernel.GetRequiredService<IEventStore>().AppendAsync(
            new NewEvent(EventKinds.StateChange, Provenances.Declared,
                new DateTimeOffset(2003, 2, 15, 6, 30, 0, TimeSpan.Zero), DateTimeOffset.UtcNow, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new { subject_id = habit.Id.ToString(), status = "Established" })),
            Ct);

        const string graduation = """
            SELECT to_char(max(graduated_on), 'YYYY-MM-DD') || ' ' || to_char(max(occurrence_date), 'YYYY-MM-DD')
            FROM bsk_derived.habit_occurrence_source WHERE habit_id = @habitId;
            """;
        await using var pacific = await SessionAsync(HomeZone.Default);
        await using var utc = await SessionAsync("UTC");

        // Graduated on, then scheduled through the day before.
        Assert.Equal("2003-02-14 2003-02-13", await pacific.ScalarAsync<string>(graduation, new { habitId = habit.Id }, Ct));
        Assert.Equal("2003-02-15 2003-02-14", await utc.ScalarAsync<string>(graduation, new { habitId = habit.Id }, Ct));
    }

    [Fact]
    public async Task A_habit_without_a_start_begins_on_its_creation_date_in_the_home_zone()
    {
        await using var kernel = Kernel();
        var habit = await HabitAsync(kernel, new { recurrence = new { freq = "daily" } });

        foreach (var zone in new[] { Kiritimati, PagoPago })
        {
            await using var session = await SessionAsync(zone);
            var today = await session.TodayAsync(Ct);

            Assert.Equal([new OccurrenceRow(Iso(today), "unrecorded")], await OccurrencesAsync(session, habit.Id, Ct));
        }
    }

    [Fact]
    public async Task The_establishment_window_ends_today_in_the_home_zone()
    {
        await using var kernel = Kernel();
        var start = TodayUtc.AddDays(-10);
        var habit = await HabitAsync(kernel, new { start = Iso(start), recurrence = new { freq = "daily" } });

        foreach (var zone in new[] { Kiritimati, PagoPago })
        {
            await using var session = await SessionAsync(zone);
            var today = await session.TodayAsync(Ct);

            var window = await session.ScalarAsync<string>(
                """
                SELECT to_char(window_start, 'YYYY-MM-DD') || ' ' || age_days || ' ' || missed
                FROM bsk.v_habit_establishment WHERE habit_id = @habitId;
                """,
                new { habitId = habit.Id }, Ct);

            // The default 60-day window, the habit's age, and every concluded day before today missed.
            var age = today.DayNumber - start.DayNumber;
            Assert.Equal($"{Iso(today.AddDays(-59))} {age} {age}", window);
        }
    }
}

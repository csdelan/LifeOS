using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// CAL-1 / D10 (migration 0020): the Appointment type and materialized occurrences.
/// A one-time appointment is a single subject with an editable status; a recurring
/// series materializes each occurrence as its own subject that inherits the series'
/// template fields and carries its own status. Materialization is idempotent.
/// </summary>
public sealed class AppointmentTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // Materialize reads today from the home calendar, so the kernel's zone is the database's.
    private IServiceProvider Provider(IClock? clock = null)
    {
        var services = classDb.Services();
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        return services.BuildServiceProvider();
    }

    private static readonly DateTimeOffset June2026 = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private async Task<SubjectRef> SeriesAsync(IServiceProvider provider, string start, string recurrence)
        => await provider.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Appointment, $"Series {Guid.NewGuid():N}",
            attributesJson: $"{{\"start_date\":\"{start}\",\"recurrence\":{recurrence}}}",
            cancellationToken: Ct);

    private async Task<List<string>> OccurrenceDatesAsync(SubjectRef series)
    {
        await using var c = await OpenAsync();
        return (await c.QueryAsync<string>(new CommandDefinition(
            "SELECT attributes->>'date' FROM bsk.subject WHERE type = 'Appointment' AND attributes->>'series' = @s ORDER BY 1;",
            new { s = series.Urn }, cancellationToken: Ct))).ToList();
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task A_one_time_appointment_defaults_to_scheduled_and_takes_a_status()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var status = provider.GetRequiredService<StatusService>();

        var appt = await subjects.CreateAsync(
            SubjectTypes.Appointment, $"Dentist {Guid.NewGuid():N}",
            attributesJson: """{"date":"2026-02-10","start":"14:00"}""", cancellationToken: Ct);

        await using var c = await OpenAsync();
        var before = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT status FROM bsk.v_appointment WHERE id = @id;", new { id = appt.Id }, cancellationToken: Ct));
        Assert.Equal("Scheduled", before);

        await status.ChangeStatusAsync(appt.Urn, "Completed", Ct);
        var after = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT status FROM bsk.v_appointment WHERE id = @id;", new { id = appt.Id }, cancellationToken: Ct));
        Assert.Equal("Completed", after);
    }

    [Fact]
    public async Task Materializing_a_weekly_series_creates_inheriting_occurrences()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var appointments = provider.GetRequiredService<AppointmentService>();

        // Weekly on Mondays from 2026-01-01; Mondays in January are 5, 12, 19, 26.
        var series = await subjects.CreateAsync(
            SubjectTypes.Appointment, $"Standup {Guid.NewGuid():N}",
            attributesJson: "{\"start_date\":\"2026-01-01\",\"start\":\"09:00\",\"location\":\"Room A\"," +
                            "\"recurrence\":{\"freq\":\"weekly\",\"on\":[\"mon\"]}}",
            cancellationToken: Ct);

        var result = await appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 1, 31), Ct);
        Assert.Equal(4, result.Created.Count);

        await using var c = await OpenAsync();
        var occurrences = (await c.QueryAsync<(string Date, string Start, string Location, string Status)>(
            new CommandDefinition(
                """
                SELECT date, start_time, location, status
                FROM bsk.v_appointment WHERE series_urn = @s ORDER BY date;
                """,
                new { s = series.Urn }, cancellationToken: Ct))).ToList();

        Assert.Equal(["2026-01-05", "2026-01-12", "2026-01-19", "2026-01-26"], occurrences.Select(o => o.Date));
        // Each occurrence inherits the series' time and location, and defaults to Scheduled.
        Assert.All(occurrences, o =>
        {
            Assert.Equal("09:00", o.Start);
            Assert.Equal("Room A", o.Location);
            Assert.Equal("Scheduled", o.Status);
        });
    }

    [Fact]
    public async Task Materialization_is_idempotent()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var appointments = provider.GetRequiredService<AppointmentService>();

        var series = await subjects.CreateAsync(
            SubjectTypes.Appointment, $"Weekly {Guid.NewGuid():N}",
            attributesJson: "{\"start_date\":\"2026-01-01\"," +
                            "\"recurrence\":{\"freq\":\"weekly\",\"on\":[\"tue\"]}}",
            cancellationToken: Ct);

        var first = await appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 1, 31), Ct);
        Assert.True(first.Created.Count > 0);

        var second = await appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 1, 31), Ct);
        Assert.Empty(second.Created); // nothing new the second time
    }

    [Fact]
    public async Task Each_occurrence_carries_its_own_status()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var appointments = provider.GetRequiredService<AppointmentService>();
        var status = provider.GetRequiredService<StatusService>();

        var series = await subjects.CreateAsync(
            SubjectTypes.Appointment, $"Series {Guid.NewGuid():N}",
            attributesJson: "{\"start_date\":\"2026-01-01\"," +
                            "\"recurrence\":{\"freq\":\"weekly\",\"on\":[\"wed\"]}}",
            cancellationToken: Ct);
        await appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 1, 31), Ct);

        await using var c = await OpenAsync();
        var firstUrn = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT urn FROM bsk.v_appointment WHERE series_urn = @s ORDER BY date LIMIT 1;",
            new { s = series.Urn }, cancellationToken: Ct));

        await status.ChangeStatusAsync(firstUrn!, "Cancelled", Ct);

        var statuses = (await c.QueryAsync<string>(new CommandDefinition(
            "SELECT status FROM bsk.v_appointment WHERE series_urn = @s ORDER BY date;",
            new { s = series.Urn }, cancellationToken: Ct))).ToList();

        Assert.Equal("Cancelled", statuses[0]);                 // only the one changed
        Assert.All(statuses.Skip(1), s => Assert.Equal("Scheduled", s));
    }

    // ---- Code review CR65: bounded, locked per series, keyed ----

    [Fact]
    public async Task Materialize_reaches_two_years_past_today_and_no_further()
    {
        var provider = Provider(new TestClock(June2026));
        var appointments = provider.GetRequiredService<AppointmentService>();
        var series = await SeriesAsync(provider, "2026-06-01", """{"freq":"weekly","on":["mon"]}""");

        var refusal = await Assert.ThrowsAsync<ArgumentException>(
            () => appointments.MaterializeAsync(series.Urn, new DateOnly(2099, 12, 31), Ct));
        Assert.Contains("at most 2 years ahead, through 2028-06-01", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await OccurrenceDatesAsync(series));

        await Assert.ThrowsAsync<ArgumentException>(() => appointments.MaterializeAsync(series.Urn, new DateOnly(2028, 6, 2), Ct));
        var made = await appointments.MaterializeAsync(series.Urn, new DateOnly(2028, 6, 1), Ct);
        Assert.Equal(new DateOnly(2028, 5, 29).ToString("yyyy-MM-dd"), (await OccurrenceDatesAsync(series))[^1]);
        Assert.Equal(105, made.Created.Count);
    }

    [Fact]
    public async Task One_run_makes_at_most_750_and_more_than_that_makes_none()
    {
        var provider = Provider(new TestClock(June2026));
        var appointments = provider.GetRequiredService<AppointmentService>();
        // Daily since 2024: 883 days through 2026-06-01.
        var series = await SeriesAsync(provider, "2024-01-01", """{"freq":"daily"}""");

        var refusal = await Assert.ThrowsAsync<ArgumentException>(
            () => appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 6, 1), Ct));
        Assert.Contains("has 883 occurrences to make through 2026-06-01", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await OccurrenceDatesAsync(series));

        Assert.Equal(31, (await appointments.MaterializeAsync(series.Urn, new DateOnly(2024, 1, 31), Ct)).Created.Count);
    }

    [Fact]
    public async Task Runs_at_once_make_each_date_once()
    {
        var provider = Provider();
        var appointments = provider.GetRequiredService<AppointmentService>();
        var series = await SeriesAsync(provider, "2026-01-01", """{"freq":"daily"}""");

        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(
            _ => Task.Run(() => appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 3, 31), Ct), Ct)));

        var dates = await OccurrenceDatesAsync(series);
        Assert.Equal(90, dates.Count);
        Assert.Equal(90, dates.Distinct().Count());
        Assert.Equal(90, runs.Sum(r => r.Created.Count));
    }

    [Fact]
    public async Task An_occurrence_moved_to_another_day_still_holds_its_date()
    {
        var provider = Provider();
        var appointments = provider.GetRequiredService<AppointmentService>();
        var series = await SeriesAsync(provider, "2026-01-01", """{"freq":"weekly","on":["mon"]}""");
        var first = (await appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 1, 31), Ct)).Created[0];

        // The Monday the 5th moves to Tuesday the 6th.
        await provider.GetRequiredService<AttributeService>()
            .SetAsync(first.Urn, [new AttributeAssignment("date", "2026-01-06")], Ct);
        var again = await appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 1, 31), Ct);

        Assert.Empty(again.Created);
        Assert.Equal(["2026-01-06", "2026-01-12", "2026-01-19", "2026-01-26"], await OccurrenceDatesAsync(series));
    }

    [Fact]
    public async Task An_occurrences_series_and_slot_are_set_only_by_materialize_and_the_store_keys_them()
    {
        var provider = Provider();
        var appointments = provider.GetRequiredService<AppointmentService>();
        var series = await SeriesAsync(provider, "2026-01-01", """{"freq":"weekly","on":["mon"]}""");
        var first = (await appointments.MaterializeAsync(series.Urn, new DateOnly(2026, 1, 11), Ct)).Created[0];

        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<AttributeService>()
            .SetAsync(first.Urn, [new AttributeAssignment("occurrence", "2026-01-12")], Ct));
        Assert.Contains("bsk materialize", refusal.Message, StringComparison.Ordinal);

        // A second occurrence for the same slot, by any path, is refused by the store.
        await using var c = await OpenAsync();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title, attributes)
            VALUES (@urn, 'Appointment', 'Copy', jsonb_build_object('series', @s, 'occurrence', '2026-01-05', 'date', '2026-01-05'));
            """,
            new { urn = $"urn:bsk:appointment:copy-{Guid.NewGuid():N}", s = series.Urn }, cancellationToken: Ct)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }
}

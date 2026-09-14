using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// CAL-1 / D10 (migration 0020): the Appointment type and materialized occurrences.
/// A one-time appointment is a single subject with an editable status; a recurring
/// series materializes each occurrence as its own subject that inherits the series'
/// template fields and carries its own status. Materialization is idempotent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AppointmentTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
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
}

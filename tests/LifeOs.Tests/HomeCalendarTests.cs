using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// A habit check-off without a date lands on today in the home zone
/// (<c>Rewards:HomeTimeZone</c>), never the server's: at 9 pm Pacific the UTC date is already
/// tomorrow. <see cref="IHomeCalendar"/> answers what day it is there; <c>bsk adhere</c> and
/// <c>POST /api/adhere</c> leave an omitted date to it.
/// <para>
/// UTC+14 (Kiritimati) and UTC−11 (Pago Pago) are at least a day apart at any instant, so
/// whichever zone a check-off used shows in its date whenever the test runs.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HomeCalendarTests(PostgresFixture postgres)
{
    private const string Kiritimati = "Pacific/Kiritimati";
    private const string PagoPago = "Pacific/Pago_Pago";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Kernel(string homeTimeZone)
        => new ServiceCollection()
            .AddLifeOsKernel(postgres.ConnectionString, homeTimeZone: homeTimeZone)
            .BuildServiceProvider();

    private Task<SubjectRef> HabitAsync(ServiceProvider kernel)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
        return kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, $"calendar habit {Guid.NewGuid():N}",
            JsonSerializer.Serialize(new { start = start.ToString("yyyy-MM-dd"), recurrence = new { freq = "daily" } }),
            cancellationToken: Ct);
    }

    /// <summary>Today in <paramref name="kernel"/>'s home zone, read before and after <paramref name="act"/>.</summary>
    private static async Task<(T Result, DateOnly[] Today)> AroundAsync<T>(
        ServiceProvider kernel, Func<Task<T>> act, CancellationToken ct)
    {
        var calendar = kernel.GetRequiredService<IHomeCalendar>();
        var before = await calendar.DateAtAsync(DateTimeOffset.UtcNow, ct);
        var result = await act();
        var after = await calendar.DateAtAsync(DateTimeOffset.UtcNow, ct);
        return (result, [before, after]);
    }

    [Theory]
    [InlineData(HomeZone.Default, "2003-02-14")]
    [InlineData("UTC", "2003-02-15")]
    [InlineData(Kiritimati, "2003-02-15")]
    [InlineData(PagoPago, "2003-02-14")]
    public async Task The_calendar_reads_an_instant_on_the_home_zones_date(string zone, string expected)
    {
        await using var kernel = Kernel(zone);

        // 06:30 UTC on 15 February 2003: 10:30 pm on the 14th in Pacific time.
        var day = await kernel.GetRequiredService<IHomeCalendar>()
            .DateAtAsync(new DateTimeOffset(2003, 2, 15, 6, 30, 0, TimeSpan.Zero), Ct);

        Assert.Equal(DateOnly.Parse(expected), day);
    }

    [Fact]
    public async Task A_zone_the_database_doesnt_know_fails_loudly()
    {
        await using var kernel = Kernel("Mars/Olympus");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => kernel.GetRequiredService<IHomeCalendar>().DateAtAsync(DateTimeOffset.UtcNow, Ct));

        Assert.Contains("Mars/Olympus", refused.Message);
        Assert.Contains(HomeZone.ConfigurationKey, refused.Message);
    }

    [Fact]
    public async Task A_check_off_without_a_date_lands_on_today_in_the_home_zone()
    {
        await using var ahead = Kernel(Kiritimati);
        await using var behind = Kernel(PagoPago);
        var habit = await HabitAsync(ahead);

        var (aheadDay, aheadToday) = await AroundAsync(ahead, () => ahead.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, cancellationToken: Ct), Ct);
        var (behindDay, behindToday) = await AroundAsync(behind, () => behind.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, cancellationToken: Ct), Ct);

        Assert.Contains(aheadDay.Occurrence, aheadToday);
        Assert.Contains(behindDay.Occurrence, behindToday);
        Assert.InRange(aheadDay.Occurrence.DayNumber - behindDay.Occurrence.DayNumber, 1, 2);
    }

    [Fact]
    public async Task A_check_off_with_a_date_keeps_it()
    {
        await using var kernel = Kernel(Kiritimati);
        var habit = await HabitAsync(kernel);
        var twoDaysAgo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

        var recorded = await kernel.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, twoDaysAgo, cancellationToken: Ct);

        Assert.Equal(twoDaysAgo, recorded.Occurrence);
    }

    [Fact]
    public async Task The_api_leaves_an_omitted_date_to_the_home_zone()
    {
        await using var host = await ApiTestHost.StartAsync(
            postgres.ConnectionString, Ct, new Dictionary<string, string?> { [HomeZone.ConfigurationKey] = Kiritimati });
        await using var kernel = Kernel(Kiritimati);
        var habit = await HabitAsync(kernel);

        var (response, today) = await AroundAsync(kernel, () => host.Client.PostAsJsonAsync(
            "/api/adhere", new { habit = habit.Id.ToString(), state = "followed" }, Ct), Ct);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var occurrence = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            """
            SELECT payload->>'occurrence' FROM bsk.event
            WHERE kind = 'activity' AND payload->>'subject_id' = @id
            ORDER BY recorded_at DESC LIMIT 1;
            """,
            new { id = habit.Id.ToString() }, cancellationToken: Ct));
        Assert.Contains(DateOnly.Parse(occurrence!), today);
    }
}

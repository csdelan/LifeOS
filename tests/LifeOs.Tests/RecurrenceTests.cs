using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// D8 (migration 0016): the structured recurrence representation and its in-database
/// expansion (bsk.recurrence_occurrences). Covers each frequency's expansion, the
/// Domain builder's validation, and the RecurrenceService round-trip (stored as a
/// jsonb object the expander can read).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RecurrenceTests(PostgresFixture postgres)
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

    private async Task<List<string>> ExpandAsync(
        NpgsqlConnection c, string recurrenceJson, string anchor, string start, string end)
        => (await c.QueryAsync<string>(new CommandDefinition(
                """
                SELECT to_char(d, 'YYYY-MM-DD')
                FROM bsk.recurrence_occurrences(@r::jsonb, @anchor::date, @start::date, @end::date) d
                ORDER BY d;
                """,
                new { r = recurrenceJson, anchor, start, end }, cancellationToken: Ct)))
            .ToList();

    [Fact]
    public async Task Daily_expands_to_every_day_in_the_window()
    {
        await using var c = await OpenAsync();
        var days = await ExpandAsync(c, Recurrence.Daily(), "2026-01-01", "2026-01-01", "2026-01-05");
        Assert.Equal(["2026-01-01", "2026-01-02", "2026-01-03", "2026-01-04", "2026-01-05"], days);
    }

    [Fact]
    public async Task Weekly_expands_to_the_named_weekdays()
    {
        await using var c = await OpenAsync();
        // 2026-01-04 is a Sunday, 2026-01-07 a Wednesday.
        var days = await ExpandAsync(c, Recurrence.Weekly(["sun", "wed"]), "2026-01-01", "2026-01-04", "2026-01-10");
        Assert.Equal(["2026-01-04", "2026-01-07"], days);
    }

    [Fact]
    public async Task Interval_expands_phased_from_the_anchor()
    {
        await using var c = await OpenAsync();
        var days = await ExpandAsync(c, Recurrence.Interval(10, "days"), "2026-01-01", "2026-01-01", "2026-01-31");
        Assert.Equal(["2026-01-01", "2026-01-11", "2026-01-21", "2026-01-31"], days);
    }

    [Fact]
    public async Task Monthly_last_expands_to_each_months_last_day()
    {
        await using var c = await OpenAsync();
        var days = await ExpandAsync(c, Recurrence.MonthlyLast(), "2026-01-01", "2026-01-01", "2026-03-31");
        Assert.Equal(["2026-01-31", "2026-02-28", "2026-03-31"], days);
    }

    [Fact]
    public async Task Monthly_day_skips_months_without_that_day()
    {
        await using var c = await OpenAsync();
        // Day 31 exists in January but not February.
        var days = await ExpandAsync(c, Recurrence.MonthlyDay(31), "2026-01-01", "2026-01-01", "2026-02-28");
        Assert.Equal(["2026-01-31"], days);
    }

    [Fact]
    public async Task Trigger_has_no_scheduled_occurrences()
    {
        await using var c = await OpenAsync();
        var days = await ExpandAsync(c, Recurrence.Trigger("after lunch"), "2026-01-01", "2026-01-01", "2026-12-31");
        Assert.Empty(days);
    }

    [Fact]
    public async Task Service_round_trip_stores_a_jsonb_object_the_expander_can_read()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var recurrence = provider.GetRequiredService<RecurrenceService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Commitment, $"weekly thing {Guid.NewGuid():N}", cancellationToken: Ct);
        await recurrence.SetAsync(subject.Urn, Recurrence.Weekly(["mon"]), Ct);

        await using var c = await OpenAsync();

        // Stored as an object (freq is readable via ->>), not a quoted string.
        var freq = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT attributes->'recurrence'->>'freq' FROM bsk.subject WHERE id = @id;",
            new { id = subject.Id }, cancellationToken: Ct));
        Assert.Equal("weekly", freq);

        // And the stored object expands. 2026-01-05 is a Monday.
        var mondays = (await c.QueryAsync<string>(new CommandDefinition(
                """
                SELECT to_char(d, 'YYYY-MM-DD') FROM bsk.subject s,
                     bsk.recurrence_occurrences(s.attributes->'recurrence', '2026-01-01', '2026-01-01', '2026-01-14') d
                WHERE s.id = @id ORDER BY d;
                """,
                new { id = subject.Id }, cancellationToken: Ct)))
            .ToList();
        Assert.Equal(["2026-01-05", "2026-01-12"], mondays);

        await recurrence.ClearAsync(subject.Urn, Ct);
        var gone = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT NOT jsonb_exists(attributes, 'recurrence') FROM bsk.subject WHERE id = @id;",
            new { id = subject.Id }, cancellationToken: Ct));
        Assert.True(gone);
    }

    [Fact]
    public void Builder_validates_inputs()
    {
        Assert.Throws<ArgumentException>(() => Recurrence.Weekly([]));
        Assert.Throws<ArgumentException>(() => Recurrence.Weekly(["funday"]));
        Assert.Throws<ArgumentException>(() => Recurrence.Interval(0, "days"));
        Assert.Throws<ArgumentException>(() => Recurrence.Interval(3, "fortnights"));
        Assert.Throws<ArgumentException>(() => Recurrence.MonthlyDay(32));
        Assert.Throws<ArgumentException>(() => Recurrence.Trigger("  "));

        // Weekly canonicalizes to Sunday-first order regardless of input order.
        Assert.Contains("\"sun\",\"wed\"", Recurrence.Weekly(["wed", "sun"]));
    }
}

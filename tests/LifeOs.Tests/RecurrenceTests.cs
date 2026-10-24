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
    [Fact]
    public void Canonicalize_takes_the_object_or_text_holding_it()
    {
        Assert.Equal(Recurrence.Weekly(["wed"]), Recurrence.Canonicalize("""{"freq":"weekly","on":["wed"]}"""));
        Assert.Equal(Recurrence.Daily(), Recurrence.Canonicalize("\"{\\\"freq\\\": \\\"daily\\\"}\""));
        Assert.Equal(Recurrence.MonthlyLast(), Recurrence.Canonicalize("""{"freq":"monthly","on":"last"}"""));
        Assert.Equal(Recurrence.MonthlyDay(15), Recurrence.Canonicalize("""{"freq":"monthly","day":15}"""));
        Assert.Equal(Recurrence.Interval(2, "weeks"), Recurrence.Canonicalize("""{"freq":"interval","every":"2","unit":"weeks"}"""));

        Assert.Throws<ArgumentException>(() => Recurrence.Canonicalize("daily"));
        Assert.Throws<ArgumentException>(() => Recurrence.Canonicalize("""{"kind":"daily"}"""));
        Assert.Throws<ArgumentException>(() => Recurrence.Canonicalize("""{"freq":"weekly","on":[]}"""));
        Assert.Throws<ArgumentException>(() => Recurrence.Canonicalize("[1,2]"));
    }

    [Fact]
    public async Task A_recurrence_written_as_text_is_stored_as_the_object()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var attributes = provider.GetRequiredService<AttributeService>();

        // How the web and `bsk new --attr` sent it: a string attribute holding the object.
        var created = await subjects.CreateAsync(
            SubjectTypes.Habit, $"text habit {Guid.NewGuid():N}",
            attributesJson: """{"recurrence":"{\"freq\":\"weekly\",\"on\":[\"wed\",\"mon\"]}"}""",
            cancellationToken: Ct);
        var edited = await subjects.CreateAsync(
            SubjectTypes.Appointment, $"text appointment {Guid.NewGuid():N}", cancellationToken: Ct);
        await attributes.SetAsync(
            edited.Urn, [new AttributeAssignment("recurrence", """{"freq":"monthly","day":1}""")], Ct);

        await using var c = await OpenAsync();
        var stored = (await c.QueryAsync<(string Kind, string Json)>(new CommandDefinition(
                """
                SELECT jsonb_typeof(attributes->'recurrence'), (attributes->'recurrence')::text
                FROM bsk.subject WHERE id = ANY(@ids) ORDER BY type DESC;
                """,
                new { ids = new[] { created.Id, edited.Id } }, cancellationToken: Ct)))
            .ToList();
        Assert.All(stored, s => Assert.Equal("object", s.Kind));
        Assert.Equal("""{"on": ["mon", "wed"], "freq": "weekly"}""", stored[0].Json);
        Assert.Equal("""{"day": 1, "freq": "monthly"}""", stored[1].Json);

        await Assert.ThrowsAsync<ArgumentException>(() => attributes.SetAsync(
            edited.Urn, [new AttributeAssignment("recurrence", "fortnightly")], Ct));
    }

    [Theory]
    [InlineData("""{"freq": "daily"}""", """{"freq": "daily"}""")]
    [InlineData("""{"kind":"daily"}""", """{"freq": "daily"}""")]
    [InlineData("""{"kind":"weekly","weekdays":[3,1,3]}""", """{"on": ["mon", "wed"], "freq": "weekly"}""")]
    [InlineData("""{"kind":"weekly","weekdays":[]}""", """{"freq": "daily"}""")]
    [InlineData("""{"kind":"interval","intervalDays":3}""", """{"freq": "interval", "unit": "days", "every": 3}""")]
    [InlineData("weekdays", """{"on": ["mon", "tue", "wed", "thu", "fri"], "freq": "weekly"}""")]
    [InlineData("weekly", """{"on": ["sun"], "freq": "weekly"}""")]
    [InlineData("every so often", null)]
    [InlineData("""{"kind":"lunar"}""", null)]
    public async Task Text_recurrences_are_read_back_as_objects(string text, string? expected)
    {
        await using var c = await OpenAsync();
        var fixedJson = await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT bsk.recurrence_from_text(@text)::text;", new { text }, cancellationToken: Ct));
        Assert.Equal(expected, fixedJson);
    }
}

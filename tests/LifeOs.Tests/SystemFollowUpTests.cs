using Dapper;
using LifeOs.Application.Home;
using LifeOs.Application.Measurements;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR69: a change the system makes because of another is recorded as <c>derived</c>,
/// citing its cause, as the ontology asks of every derived event (CR51's rule): a series' start set
/// when a status move restarts it, a restore brings it back or its pattern is set without one, and
/// a metric's range taken from its newest lab result. A start Chris chose stays <c>declared</c>. A
/// metric's or a space's edit of its name and the rest lands whole or not at all.
/// </summary>
public sealed class SystemFollowUpTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_series_restarted_by_a_status_move_starts_again_derived_from_that_move()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var series = await SeriesAsync(db, "Take out recycling", start: (await db.TodayAsync()).AddDays(-14));
        var status = db.Get<StatusService>();
        await status.ChangeStatusAsync(series.Urn, "Cancelled", Ct);

        var restarted = await status.ChangeStatusAsync(series.Urn, "Not started", Ct);

        Assert.Equal((Provenances.Derived, $"{restarted.EventId}"), await NewestChangeAsync(db, series.Id, TaskSeries.StartKey));
    }

    [Fact]
    public async Task A_series_restored_from_the_archive_starts_again_derived_from_the_restore()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var series = await SeriesAsync(db, "Mow lawn", start: (await db.TodayAsync()).AddDays(-14));
        var archive = db.Get<ArchiveService>();
        await archive.ArchiveAsync(series.Urn, Ct);

        var restored = await archive.RestoreAsync(series.Urn, Ct);

        Assert.Equal((Provenances.Derived, $"{restored.EventId}"), await NewestChangeAsync(db, series.Id, TaskSeries.StartKey));
    }

    [Fact]
    public async Task A_pattern_set_without_a_start_gets_one_derived_from_the_pattern_and_a_chosen_start_is_declared()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);

        var worked = await SeriesAsync(db, "Water plants", start: null);
        var pattern = await ChangeAsync(db, worked.Id, Recurrence.AttributeKey);
        Assert.Equal(Provenances.Declared, pattern.Provenance);
        Assert.Equal((Provenances.Derived, $"{pattern.Id}"), await NewestChangeAsync(db, worked.Id, TaskSeries.StartKey));

        var chosen = await SeriesAsync(db, "Change filter", start: await db.TodayAsync());
        Assert.Equal((Provenances.Declared, ""), await NewestChangeAsync(db, chosen.Id, TaskSeries.StartKey));
    }

    [Fact]
    public async Task A_metrics_range_taken_from_its_newest_lab_result_is_derived_from_that_result()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var visits = db.Get<LabVisitService>();
        await visits.LogAsync(new LabVisitEntry(DateTimeOffset.UtcNow.AddDays(-40), "Quest", null, [new("glucose", 92m, 65m, 99m)]), Ct);

        var newer = await visits.LogAsync(
            new LabVisitEntry(DateTimeOffset.UtcNow.AddDays(-3), "Quest", null, [new("glucose", 104m, 70m, 100m)]), Ct);

        var result = Assert.Single(newer.Results);
        var glucose = Assert.Single(await db.Get<MetricService>().ListAsync(cancellationToken: Ct), m => m.Id == result.Metric.Id);
        Assert.Equal((70m, 100m), (glucose.Low!.Value, glucose.High!.Value));
        Assert.Equal((Provenances.Derived, $"{result.Reading.EventId}"), await NewestChangeAsync(db, glucose.Id, MetricLimits.LowKey));
    }

    [Fact]
    public async Task A_metrics_name_and_target_change_whole_or_not_at_all()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var metrics = db.Get<MetricService>();
        var weight = await metrics.AddAsync("Weight", cancellationToken: Ct);
        await RefuseAttributeChangesAsync(db, weight.Id);

        await Assert.ThrowsAsync<PostgresException>(() =>
            metrics.SetAsync(weight.Urn, new MetricChange(Name: "Body weight", Target: 180m), Ct));

        Assert.Equal("Weight", Assert.Single(await metrics.ListAsync(cancellationToken: Ct), m => m.Id == weight.Id).Name);
        Assert.Equal(0, await ChangesAsync(db, weight.Id));
    }

    [Fact]
    public async Task A_spaces_name_and_weight_change_whole_or_not_at_all()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var spaces = db.Get<SpaceService>();
        var garage = await spaces.AddAsync("Garage", weight: 1, cancellationToken: Ct);
        await RefuseAttributeChangesAsync(db, garage.Id);

        await Assert.ThrowsAsync<PostgresException>(() => spaces.SetAsync(garage.Urn, name: "Workshop", weight: 3, cancellationToken: Ct));

        Assert.Equal("Garage", Assert.Single(await spaces.ListAsync(cancellationToken: Ct), s => s.Id == garage.Id).Name);
        Assert.Equal(0, await ChangesAsync(db, garage.Id));
    }

    /// <summary>A repeating Task, its pattern set from <paramref name="start"/> (none: worked out).</summary>
    private async Task<SubjectRef> SeriesAsync(DomainTestDatabase db, string title, DateOnly? start)
    {
        var series = await db.Get<SubjectService>().CreateAsync(SubjectTypes.Task, $"{title} {Guid.NewGuid():N}", cancellationToken: Ct);
        await db.Get<RecurrenceService>().SetAsync(series.Urn, Recurrence.Weekly(["wed"]), start, Ct);
        return series;
    }

    /// <summary>The subject's newest <c>attribute_change</c> to <paramref name="key"/>: its provenance and the events it cites.</summary>
    private async Task<(string Provenance, string Cites)> NewestChangeAsync(DomainTestDatabase db, Guid subjectId, string key)
    {
        var change = await ChangeAsync(db, subjectId, key);
        return (change.Provenance, string.Join(",", change.DerivedFrom));
    }

    /// <summary>The subject's newest <c>attribute_change</c> to <paramref name="key"/>: its id, provenance and cause.</summary>

    private async Task<(Guid Id, string Provenance, Guid[] DerivedFrom)> ChangeAsync(DomainTestDatabase db, Guid subjectId, string key)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.QuerySingleAsync<(Guid, string, Guid[])>(new CommandDefinition(
            """
            SELECT e.id, e.provenance, e.derived_from
            FROM bsk.event e
            JOIN bsk.subject_event se ON se.event_id = e.id AND se.relation = 'concerns'
            WHERE se.subject_id = @subjectId AND e.kind = 'attribute_change'
              AND (jsonb_exists(e.payload -> 'attributes', @key) OR jsonb_exists(e.payload, @key))
            ORDER BY e.recorded_at DESC, e.id DESC
            LIMIT 1;
            """,
            new { subjectId, key }, cancellationToken: Ct));
    }

    private async Task<int> ChangesAsync(DomainTestDatabase db, Guid subjectId)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM bsk.event e
            JOIN bsk.subject_event se ON se.event_id = e.id
            WHERE se.subject_id = @subjectId AND e.kind = 'attribute_change';
            """,
            new { subjectId }, cancellationToken: Ct));
    }

    /// <summary>
    /// In this throwaway database, any change to the subject's attributes fails, as a write that
    /// fails partway would: its title can still change on its own.
    /// </summary>
    private async Task RefuseAttributeChangesAsync(DomainTestDatabase db, Guid subjectId)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            CREATE FUNCTION public.refuse_attribute_change() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.attributes IS DISTINCT FROM OLD.attributes THEN
                    RAISE EXCEPTION 'attributes refused for this test';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER refuse_attribute_change BEFORE UPDATE ON bsk.subject
                FOR EACH ROW WHEN (OLD.id = '{subjectId}') EXECUTE FUNCTION public.refuse_attribute_change();
            """,
            cancellationToken: Ct));
    }
}

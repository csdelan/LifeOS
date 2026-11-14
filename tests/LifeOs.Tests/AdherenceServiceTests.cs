using System.Text.Json;
using Dapper;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// GEN-3 / D2 (migration 0017): the Habit type and adherence recording. Adherence is
/// an append-only activity event carrying the result, plus an evidences (followed/
/// partial) or violates (missed) edge. Partial is gated by allows_partial; a
/// correction is another recording for the same date. Occurrence/streak projection
/// is the next phase.
/// </summary>
public sealed class AdherenceServiceTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => classDb.Kernel();

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<SubjectRef> MakeHabitAsync(IServiceProvider provider, bool allowsPartial = false, object? recurrence = null)
    {
        // Tracked daily from 2026-01-05, a Monday, so the days these tests record on are days it has.
        var attrs = new Dictionary<string, object>
        {
            ["start"] = "2026-01-05",
            ["recurrence"] = recurrence ?? new { freq = "daily" },
        };
        if (allowsPartial) attrs["allows_partial"] = "true";
        return await provider.CreateTrackedHabitAsync($"habit {Guid.NewGuid():N}", JsonSerializer.Serialize(attrs), Ct);
    }

    private async Task<(string Result, string Relation)> ReadAdherenceAsync(NpgsqlConnection c, Guid eventId)
        => await c.QuerySingleAsync<(string, string)>(new CommandDefinition(
            """
            SELECT e.payload->>'result', se.relation
            FROM bsk.event e JOIN bsk.subject_event se ON se.event_id = e.id
            WHERE e.id = @id;
            """,
            new { id = eventId }, cancellationToken: Ct));

    [Fact]
    public async Task Followed_records_an_activity_event_that_evidences_the_habit()
    {
        var provider = Provider();
        var habit = await MakeHabitAsync(provider);
        var adherence = provider.GetRequiredService<AdherenceService>();

        var result = await adherence.RecordAsync(habit.Urn, "followed", new DateOnly(2026, 1, 5), null, Ct);

        await using var c = await OpenAsync();
        var (storedResult, relation) = await ReadAdherenceAsync(c, result.EventId);
        Assert.Equal("followed", storedResult);
        Assert.Equal(SubjectEventRelations.Evidences, relation);
    }

    [Fact]
    public async Task The_days_note_reads_back_with_its_occurrence()
    {
        var provider = Provider();
        var habit = await MakeHabitAsync(provider);
        var adherence = provider.GetRequiredService<AdherenceService>();
        var today = await provider.GetRequiredService<IHomeCalendar>().DateAtAsync(DateTimeOffset.UtcNow, Ct);
        await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today, note: "  ran it early  ", cancellationToken: Ct);

        var reader = TestReaders.Subjects(classDb.ConnectionString);
        var day = Assert.Single(await reader.GetHabitOccurrencesAsync(habit.Id, on: today, cancellationToken: Ct));
        Assert.Equal("ran it early", day.Note);

        // A correction without a note clears it: the newest check-off is the day's record.
        await adherence.RecordAsync(habit.Urn, AdherenceResults.Missed, today, cancellationToken: Ct);
        Assert.Null(Assert.Single(await reader.GetHabitOccurrencesAsync(habit.Id, on: today, cancellationToken: Ct)).Note);
    }

    [Fact]
    public async Task Missed_records_a_violation()
    {
        var provider = Provider();
        var habit = await MakeHabitAsync(provider);
        var adherence = provider.GetRequiredService<AdherenceService>();

        var result = await adherence.RecordAsync(habit.Urn, "missed", new DateOnly(2026, 1, 5), null, Ct);

        await using var c = await OpenAsync();
        var (storedResult, relation) = await ReadAdherenceAsync(c, result.EventId);
        Assert.Equal("missed", storedResult);
        Assert.Equal(SubjectEventRelations.Violates, relation);
    }

    [Fact]
    public async Task A_day_that_hasnt_come_takes_no_recording()
    {
        var provider = Provider();
        var habit = await MakeHabitAsync(provider);
        var adherence = provider.GetRequiredService<AdherenceService>();
        var today = await provider.GetRequiredService<IHomeCalendar>().DateAtAsync(DateTimeOffset.UtcNow, Ct);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(1), cancellationToken: Ct));
        Assert.Contains("hasn't come yet", refused.Message);
    }

    [Fact]
    public async Task A_miss_is_only_of_a_day_the_habit_has()
    {
        var provider = Provider();
        var adherence = provider.GetRequiredService<AdherenceService>();
        var mondays = await MakeHabitAsync(provider, recurrence: new { freq = "weekly", on = new[] { "mon" } });
        var monday = new DateOnly(2026, 1, 12);
        var tuesday = monday.AddDays(1);

        // Not one of its days: there's nothing to miss, though a session done anyway can be logged.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await adherence.RecordAsync(mondays.Urn, AdherenceResults.Missed, tuesday, cancellationToken: Ct));
        Assert.Contains("isn't scheduled on 2026-01-13", refused.Message);
        await adherence.RecordAsync(mondays.Urn, AdherenceResults.Followed, tuesday, cancellationToken: Ct);
        // Once logged the day is the habit's, and a miss corrects it; a scheduled day can be missed.
        await adherence.RecordAsync(mondays.Urn, AdherenceResults.Missed, tuesday, cancellationToken: Ct);
        await adherence.RecordAsync(mondays.Urn, AdherenceResults.Missed, monday, cancellationToken: Ct);

        // A habit with no pattern is scheduled on no day.
        var unpatterned = await provider.CreateTrackedHabitAsync(
            $"habit {Guid.NewGuid():N}", """{"start":"2026-01-05"}""", Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await adherence.RecordAsync(unpatterned.Urn, AdherenceResults.Missed, monday, cancellationToken: Ct));
    }

    [Fact]
    public async Task Partial_is_rejected_unless_the_habit_allows_it()
    {
        var provider = Provider();
        var adherence = provider.GetRequiredService<AdherenceService>();

        var binary = await MakeHabitAsync(provider, allowsPartial: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await adherence.RecordAsync(binary.Urn, "partial", new DateOnly(2026, 1, 5), null, Ct));

        var malleable = await MakeHabitAsync(provider, allowsPartial: true);
        var ok = await adherence.RecordAsync(malleable.Urn, "partial", new DateOnly(2026, 1, 5), null, Ct);
        await using var c = await OpenAsync();
        var (storedResult, relation) = await ReadAdherenceAsync(c, ok.EventId);
        Assert.Equal("partial", storedResult);
        Assert.Equal(SubjectEventRelations.Evidences, relation); // partial still supports the habit
    }

    [Fact]
    public async Task Adherence_can_only_be_recorded_against_a_habit()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var adherence = provider.GetRequiredService<AdherenceService>();

        var notHabit = await subjects.CreateAsync(SubjectTypes.Goal, $"not a habit {Guid.NewGuid():N}", cancellationToken: Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await adherence.RecordAsync(notHabit.Urn, "followed", new DateOnly(2026, 1, 5), null, Ct));
    }

    [Fact]
    public async Task A_correction_is_a_second_append_only_recording_for_the_same_date()
    {
        var provider = Provider();
        var habit = await MakeHabitAsync(provider);
        var adherence = provider.GetRequiredService<AdherenceService>();
        var date = new DateOnly(2026, 1, 5);

        await adherence.RecordAsync(habit.Urn, "missed", date, null, Ct);
        await adherence.RecordAsync(habit.Urn, "followed", date, "actually did it, forgot to log", Ct);

        await using var c = await OpenAsync();
        var count = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT count(*) FROM bsk.event
            WHERE kind = 'activity' AND payload->>'subject_id' = @id AND payload->>'occurrence' = '2026-01-05';
            """,
            new { id = habit.Id.ToString() }, cancellationToken: Ct));
        Assert.Equal(2, count); // both retained; the projection will take the latest
    }
}

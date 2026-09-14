using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
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
[Collection(PostgresCollection.Name)]
public sealed class AdherenceServiceTests(PostgresFixture postgres)
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

    private async Task<SubjectRef> MakeHabitAsync(SubjectService subjects, bool allowsPartial = false)
    {
        var attrs = allowsPartial ? """{"allows_partial":"true"}""" : "{}";
        return await subjects.CreateAsync(
            SubjectTypes.Habit, $"habit {Guid.NewGuid():N}", attributesJson: attrs, cancellationToken: Ct);
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
        var habit = await MakeHabitAsync(provider.GetRequiredService<SubjectService>());
        var adherence = provider.GetRequiredService<AdherenceService>();

        var result = await adherence.RecordAsync(habit.Urn, "followed", new DateOnly(2026, 1, 5), null, Ct);

        await using var c = await OpenAsync();
        var (storedResult, relation) = await ReadAdherenceAsync(c, result.EventId);
        Assert.Equal("followed", storedResult);
        Assert.Equal(SubjectEventRelations.Evidences, relation);
    }

    [Fact]
    public async Task Missed_records_a_violation()
    {
        var provider = Provider();
        var habit = await MakeHabitAsync(provider.GetRequiredService<SubjectService>());
        var adherence = provider.GetRequiredService<AdherenceService>();

        var result = await adherence.RecordAsync(habit.Urn, "missed", new DateOnly(2026, 1, 5), null, Ct);

        await using var c = await OpenAsync();
        var (storedResult, relation) = await ReadAdherenceAsync(c, result.EventId);
        Assert.Equal("missed", storedResult);
        Assert.Equal(SubjectEventRelations.Violates, relation);
    }

    [Fact]
    public async Task Partial_is_rejected_unless_the_habit_allows_it()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var adherence = provider.GetRequiredService<AdherenceService>();

        var binary = await MakeHabitAsync(subjects, allowsPartial: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await adherence.RecordAsync(binary.Urn, "partial", new DateOnly(2026, 1, 5), null, Ct));

        var malleable = await MakeHabitAsync(subjects, allowsPartial: true);
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
        var habit = await MakeHabitAsync(provider.GetRequiredService<SubjectService>());
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

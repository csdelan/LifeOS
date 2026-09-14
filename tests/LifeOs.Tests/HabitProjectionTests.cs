using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// GEN-3 / GEN-5 (migration 0018): the derived habit occurrence + streak projection.
/// Occurrences come from the recurrence expansion joined to the latest adherence per
/// date; past unrecorded occurrences read as missed, today's as unrecorded; the streak
/// counts consecutive fully-followed concluded occurrences (partial/missed break it);
/// corrections and cue-based habits are handled; archived habits drop out.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HabitProjectionTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    // A daily habit whose start is `startOffset` days before today.
    private async Task<SubjectRef> MakeDailyHabitAsync(
        SubjectService subjects, int startOffset, bool allowsPartial = false)
    {
        var start = Today.AddDays(-startOffset).ToString("yyyy-MM-dd");
        var partial = allowsPartial ? ",\"allows_partial\":\"true\"" : "";
        var attrs = "{\"start\":\"" + start + "\",\"recurrence\":{\"freq\":\"daily\"}" + partial + "}";
        return await subjects.CreateAsync(
            SubjectTypes.Habit, $"daily habit {Guid.NewGuid():N}", attributesJson: attrs, cancellationToken: Ct);
    }

    private async Task<string?> StateAsync(NpgsqlConnection c, Guid habitId, DateOnly date)
        => await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT state FROM bsk.v_habit_occurrence WHERE habit_id = @id AND occurrence_date = @d::date;",
            new { id = habitId, d = date.ToString("yyyy-MM-dd") }, cancellationToken: Ct));

    private async Task<long?> StreakAsync(NpgsqlConnection c, Guid habitId)
        => await c.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT current_streak FROM bsk.v_habit_streak WHERE habit_id = @id;",
            new { id = habitId }, cancellationToken: Ct));

    [Fact]
    public async Task Past_unrecorded_occurrence_is_missed_but_today_is_unrecorded()
    {
        var provider = Provider();
        var habit = await MakeDailyHabitAsync(provider.GetRequiredService<SubjectService>(), startOffset: 3);

        await using var c = await OpenAsync();
        Assert.Equal("missed", await StateAsync(c, habit.Id, Today.AddDays(-1)));  // concluded, no record
        Assert.Equal("unrecorded", await StateAsync(c, habit.Id, Today));          // not a failure yet
    }

    [Fact]
    public async Task Consecutive_followed_days_build_the_streak()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var adherence = provider.GetRequiredService<AdherenceService>();
        var habit = await MakeDailyHabitAsync(subjects, startOffset: 3);

        foreach (var offset in new[] { 3, 2, 1 })
        {
            await adherence.RecordAsync(habit.Urn, "followed", Today.AddDays(-offset), null, Ct);
        }

        await using var c = await OpenAsync();
        Assert.Equal(3, await StreakAsync(c, habit.Id));
    }

    [Fact]
    public async Task A_miss_breaks_the_streak_before_it()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var adherence = provider.GetRequiredService<AdherenceService>();
        var habit = await MakeDailyHabitAsync(subjects, startOffset: 3);

        await adherence.RecordAsync(habit.Urn, "followed", Today.AddDays(-3), null, Ct);
        await adherence.RecordAsync(habit.Urn, "missed", Today.AddDays(-2), null, Ct);
        await adherence.RecordAsync(habit.Urn, "followed", Today.AddDays(-1), null, Ct);

        // Most recent concluded is followed (-1), then a miss at (-2): streak = 1.
        await using var c = await OpenAsync();
        Assert.Equal(1, await StreakAsync(c, habit.Id));
    }

    [Fact]
    public async Task Partial_breaks_the_binary_streak()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var adherence = provider.GetRequiredService<AdherenceService>();
        var habit = await MakeDailyHabitAsync(subjects, startOffset: 2, allowsPartial: true);

        await adherence.RecordAsync(habit.Urn, "followed", Today.AddDays(-2), null, Ct);
        await adherence.RecordAsync(habit.Urn, "partial", Today.AddDays(-1), null, Ct);

        await using var c = await OpenAsync();
        Assert.Equal("partial", await StateAsync(c, habit.Id, Today.AddDays(-1)));
        Assert.Equal(0, await StreakAsync(c, habit.Id)); // most recent concluded is partial
    }

    [Fact]
    public async Task Correcting_a_missed_day_to_followed_recalculates_the_streak()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var adherence = provider.GetRequiredService<AdherenceService>();
        var habit = await MakeDailyHabitAsync(subjects, startOffset: 1);

        await adherence.RecordAsync(habit.Urn, "missed", Today.AddDays(-1), null, Ct);
        await adherence.RecordAsync(habit.Urn, "followed", Today.AddDays(-1), "did it, logged late", Ct);

        await using var c = await OpenAsync();
        Assert.Equal("followed", await StateAsync(c, habit.Id, Today.AddDays(-1))); // latest wins
        Assert.Equal(1, await StreakAsync(c, habit.Id));
    }

    [Fact]
    public async Task A_cue_based_habit_shows_only_recorded_days_and_is_never_auto_missed()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var adherence = provider.GetRequiredService<AdherenceService>();

        var attrs = """{"recurrence":{"freq":"trigger","cue":"after lunch"}}""";
        var habit = await subjects.CreateAsync(
            SubjectTypes.Habit, $"cue habit {Guid.NewGuid():N}", attributesJson: attrs, cancellationToken: Ct);
        await adherence.RecordAsync(habit.Urn, "followed", Today.AddDays(-1), null, Ct);

        await using var c = await OpenAsync();
        // The recorded day shows; an arbitrary earlier day has no occurrence row at all.
        Assert.Equal("followed", await StateAsync(c, habit.Id, Today.AddDays(-1)));
        Assert.Null(await StateAsync(c, habit.Id, Today.AddDays(-5)));
    }

    [Fact]
    public async Task An_archived_habit_drops_out_of_the_projection()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var adherence = provider.GetRequiredService<AdherenceService>();
        var archive = provider.GetRequiredService<ArchiveService>();
        var habit = await MakeDailyHabitAsync(subjects, startOffset: 2);
        await adherence.RecordAsync(habit.Urn, "followed", Today.AddDays(-1), null, Ct);

        await archive.ArchiveAsync(habit.Urn, Ct);

        await using var c = await OpenAsync();
        Assert.Null(await StateAsync(c, habit.Id, Today.AddDays(-1)));
        Assert.Null(await StreakAsync(c, habit.Id));
    }
}

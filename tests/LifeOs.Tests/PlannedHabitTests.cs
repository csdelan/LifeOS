using System.Net;
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
/// Migration 0049: a habit can be Planned, made but not tracked yet, and a new habit starts
/// there. While Planned nothing is scheduled, so no day is missed; it has no establishment
/// progress; and the kernel refuses to record its days. Moving it to Building starts tracking
/// that day. A habit with no status, made before Planned existed, still reads as Building.
/// <para>
/// A status change is always now, so the spells here are status events written days back on
/// habits made with no status. The tests' database dates the views in UTC
/// (<see cref="PostgresFixture.HabitViewZone"/>), and each event is at midday UTC, so its day
/// is the same whichever zone reads it.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PlannedHabitTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    private ServiceProvider Kernel()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private static string Daily(int startOffset)
        => JsonSerializer.Serialize(new
        {
            start = Today.AddDays(-startOffset).ToString("yyyy-MM-dd"),
            recurrence = new { freq = "daily" }
        });

    /// <summary>A status change at midday UTC <paramref name="daysAgo"/> days ago, written behind reconcile's back.</summary>
    private static Task<Guid> StatusDaysAgoAsync(ServiceProvider kernel, SubjectRef habit, string status, int daysAgo)
        => kernel.GetRequiredService<IEventStore>().AppendAsync(
            new NewEvent(EventKinds.StateChange, Provenances.Declared,
                new DateTimeOffset(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
                DateTimeOffset.UtcNow, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new { subject_id = habit.Id.ToString(), status })),
            Ct);

    private async Task<string?> StatusAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current_source WHERE subject_id = @id;",
            new { id }, cancellationToken: Ct));
    }

    /// <summary>The habit's occurrences, days ago → state, oldest first.</summary>
    private async Task<List<(int DaysAgo, string State)>> OccurrencesAsync(Guid habitId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var rows = await connection.QueryAsync<(string Day, string State)>(new CommandDefinition(
            """
            SELECT to_char(occurrence_date, 'YYYY-MM-DD'), state FROM bsk.v_habit_occurrence
            WHERE habit_id = @habitId ORDER BY occurrence_date;
            """,
            new { habitId }, cancellationToken: Ct));
        return rows
            .Select(r => (Today.DayNumber - DateOnly.ParseExact(r.Day, "yyyy-MM-dd").DayNumber, r.State))
            .ToList();
    }

    /// <summary>The habit's establishment row as (age, missed), or null when it has none.</summary>
    private async Task<(int AgeDays, long Missed)?> EstablishmentAsync(Guid habitId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var rows = (await connection.QueryAsync<(int, long)>(new CommandDefinition(
            "SELECT age_days, missed FROM bsk.v_habit_establishment WHERE habit_id = @habitId;",
            new { habitId }, cancellationToken: Ct))).ToList();
        return rows.Count == 0 ? null : rows[0];
    }

    private async Task<long> RecordingsAsync(Guid habitId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'activity' AND payload->>'subject_id' = @id;",
            new { id = habitId.ToString() }, cancellationToken: Ct));
    }

    [Fact]
    public void Planned_is_a_habits_first_status_and_a_new_habits_only()
    {
        Assert.Equal(["Planned", "Building", "Established", "Abandoned"], StatusVocabulary.For(SubjectTypes.Habit));
        Assert.Equal("Planned", StatusVocabulary.InitialStatusFor(SubjectTypes.Habit));
        Assert.All(
            StatusVocabulary.StatusBearingTypes.Where(t => t != SubjectTypes.Habit),
            t => Assert.Null(StatusVocabulary.InitialStatusFor(t)));
        Assert.False(StatusVocabulary.IsTerminal("Planned"));
        Assert.True(StatusVocabulary.IsPlanned(" planned "));
        Assert.False(StatusVocabulary.IsPlanned(null));
        Assert.False(StatusVocabulary.IsPlanned("Building"));
    }

    [Fact]
    public async Task A_new_habit_is_planned_from_every_door_and_other_types_start_with_no_status()
    {
        await using var kernel = Kernel();
        var subjects = kernel.GetRequiredService<SubjectService>();
        var goal = await subjects.CreateAsync(SubjectTypes.Goal, Unique("planned goal"), cancellationToken: Ct);

        var made = await subjects.CreateAsync(SubjectTypes.Habit, Unique("made habit"), cancellationToken: Ct);
        var (child, _) = await subjects.CreateWithParentEdgeAsync(
            SubjectTypes.Habit, Unique("child habit"), "{}", SubjectRelations.Serves, goal.Id, Ct);
        var task = await subjects.CreateAsync(SubjectTypes.Task, Unique("plain task"), cancellationToken: Ct);

        Assert.Equal("Planned", await StatusAsync(made.Id));
        Assert.Equal("Planned", await StatusAsync(child.Id));
        Assert.Null(await StatusAsync(task.Id));
        Assert.Null(await StatusAsync(goal.Id));

        // The web's New: the habit reads back Planned, with no progress toward establishing.
        await using var host = await ApiTestHost.StartAsync(postgres.ConnectionString, Ct);
        var created = await host.Client.PostAsJsonAsync(
            "/api/subjects", new { type = SubjectTypes.Habit, title = Unique("web habit") }, Ct);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var row = (await host.Client.GetFromJsonAsync<JsonElement>("/api/habits", Ct)).EnumerateArray()
            .Single(h => h.GetProperty("id").GetGuid() == id);
        Assert.Equal("Planned", row.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("establishment").ValueKind);
    }

    [Fact]
    public async Task A_planned_habit_has_no_schedule_and_no_progress_even_with_a_start_behind_it()
    {
        await using var kernel = Kernel();
        var habit = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, Unique("planned habit"), Daily(startOffset: 5), cancellationToken: Ct);

        Assert.Empty(await OccurrencesAsync(habit.Id));
        Assert.Null(await EstablishmentAsync(habit.Id));
    }

    [Fact]
    public async Task A_planned_habits_days_are_refused_and_nothing_is_written()
    {
        await using var kernel = Kernel();
        var habit = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, Unique("unstarted habit"), Daily(startOffset: 2), cancellationToken: Ct);
        var adherence = kernel.GetRequiredService<AdherenceService>();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, Today.AddDays(-1), cancellationToken: Ct));
        Assert.Contains("is Planned, so it isn't tracked yet", refused.Message);

        await using var host = await ApiTestHost.StartAsync(postgres.ConnectionString, Ct);
        var response = await host.Client.PostAsJsonAsync("/api/adhere", new
        {
            habit = habit.Id.ToString(), state = "followed", on = Today.ToString("yyyy-MM-dd")
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Move it to Building",
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());

        Assert.Equal(0, await RecordingsAsync(habit.Id));
    }

    [Fact]
    public async Task Starting_to_track_a_habit_schedules_it_from_that_day()
    {
        await using var kernel = Kernel();
        var habit = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, Unique("starting habit"), """{"recurrence":{"freq":"daily"}}""", cancellationToken: Ct);

        await kernel.GetRequiredService<StatusService>().ChangeStatusAsync(habit.Urn, "Building", Ct);

        Assert.Equal([(0, "unrecorded")], await OccurrencesAsync(habit.Id));
        Assert.Equal((0, 0L), await EstablishmentAsync(habit.Id));
        var followed = await kernel.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, Today, cancellationToken: Ct);
        Assert.Contains(followed.Awards, a => a.Trigger == BountyTriggers.Followed);
    }

    [Fact]
    public async Task The_days_a_habit_spent_planned_are_neither_scheduled_nor_counted_toward_its_age()
    {
        await using var kernel = Kernel();
        var habit = await kernel.CreateHabitWithoutStatusAsync(Unique("paused habit"), Daily(startOffset: 10), Ct);
        await StatusDaysAgoAsync(kernel, habit, "Planned", daysAgo: 8);
        await StatusDaysAgoAsync(kernel, habit, "Building", daysAgo: 3);

        // Tracked from its start until it was Planned, then again from the day it was started.
        Assert.Equal(
            [(10, "missed"), (9, "missed"), (3, "missed"), (2, "missed"), (1, "missed"), (0, "unrecorded")],
            await OccurrencesAsync(habit.Id));
        // Ten days since its start, five of them Planned.
        Assert.Equal((5, 5L), await EstablishmentAsync(habit.Id));
    }

    [Fact]
    public async Task Moving_a_tracked_habit_back_to_planned_stops_its_schedule_and_keeps_its_history()
    {
        await using var kernel = Kernel();
        var habit = await kernel.CreateHabitWithoutStatusAsync(Unique("shelved habit"), Daily(startOffset: 4), Ct);
        await kernel.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, Today.AddDays(-3), cancellationToken: Ct);
        await StatusDaysAgoAsync(kernel, habit, "Planned", daysAgo: 1);

        Assert.Equal([(4, "missed"), (3, "followed"), (2, "missed")], await OccurrencesAsync(habit.Id));
        Assert.Null(await EstablishmentAsync(habit.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => kernel.GetRequiredService<AdherenceService>()
                .RecordAsync(habit.Urn, AdherenceResults.Followed, Today, cancellationToken: Ct));
    }

    [Fact]
    public async Task A_habit_with_no_status_was_made_before_planned_and_is_still_tracked()
    {
        await using var kernel = Kernel();
        var habit = await kernel.CreateHabitWithoutStatusAsync(Unique("older habit"), Daily(startOffset: 2), Ct);

        Assert.Null(await StatusAsync(habit.Id));
        Assert.Equal([(2, "missed"), (1, "missed"), (0, "unrecorded")], await OccurrencesAsync(habit.Id));
        Assert.Equal((2, 2L), await EstablishmentAsync(habit.Id));
        await kernel.GetRequiredService<AdherenceService>()
            .RecordAsync(habit.Urn, AdherenceResults.Followed, Today, cancellationToken: Ct);
    }
}

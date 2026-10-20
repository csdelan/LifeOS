using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>A clock the test moves by hand, so segment lengths are exact.</summary>
internal sealed class TimerTestClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>
/// TASKS-2 (migration 0024): the optional Task timer. Every action is an append-only
/// activity event with a concerns edge; state and recorded time are folded by
/// bsk.v_task_timer. One timer runs at a time — starting one pauses the other.
/// Each test ends with no timer left running, so tests sharing the database never
/// pause one another's timers.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TimerServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private (TimerService Timers, SubjectService Subjects, TimerTestClock Clock) Build()
    {
        var provider = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var clock = new TimerTestClock(DateTimeOffset.UtcNow);
        var timers = new TimerService(
            subjects,
            new NpgsqlTimerRepository(postgres.ConnectionString),
            provider.GetRequiredService<StatusService>(),
            clock,
            KernelSources.Cli);
        return (timers, subjects, clock);
    }

    private Task<SubjectRef> TaskAsync(SubjectService subjects)
        => subjects.CreateAsync(SubjectTypes.Task, $"timed task {Guid.NewGuid():N}", cancellationToken: Ct);

    [Fact]
    public async Task Time_adds_up_across_pauses_and_sessions()
    {
        var (timers, subjects, clock) = Build();
        var task = await TaskAsync(subjects);

        await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);
        clock.Advance(TimeSpan.FromSeconds(60));
        var paused = await timers.ApplyAsync(task.Urn, TimerActions.Pause, Ct);
        Assert.Equal(TimerStates.Paused, paused.Timer.State);
        Assert.Equal(60, paused.Timer.TotalSeconds);

        clock.Advance(TimeSpan.FromSeconds(40)); // a break: not counted
        await timers.ApplyAsync(task.Urn, TimerActions.Resume, Ct);
        clock.Advance(TimeSpan.FromSeconds(30));
        var stopped = await timers.ApplyAsync(task.Urn, TimerActions.Stop, Ct);
        Assert.Equal(TimerStates.Stopped, stopped.Timer.State);
        Assert.Equal(90, stopped.Timer.TotalSeconds);
        Assert.Equal(90, stopped.Timer.SessionSeconds);

        // A second session adds to the total; the session figure starts over.
        clock.Advance(TimeSpan.FromMinutes(5));
        await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);
        clock.Advance(TimeSpan.FromSeconds(10));
        var second = await timers.ApplyAsync(task.Urn, TimerActions.Stop, Ct);
        Assert.Equal(100, second.Timer.TotalSeconds);
        Assert.Equal(10, second.Timer.SessionSeconds);
    }

    [Fact]
    public async Task A_running_segment_is_left_out_of_the_total_and_reported_by_its_start()
    {
        var (timers, subjects, clock) = Build();
        var task = await TaskAsync(subjects);

        var started = await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);
        Assert.Equal(TimerStates.Running, started.Timer.State);
        Assert.Equal(0, started.Timer.TotalSeconds);
        Assert.NotNull(started.Timer.RunningSince);
        Assert.Equal(clock.UtcNow.ToUnixTimeSeconds(), started.Timer.RunningSince!.Value.ToUnixTimeSeconds());

        clock.Advance(TimeSpan.FromSeconds(5));
        await timers.ApplyAsync(task.Urn, TimerActions.Stop, Ct);
    }

    [Fact]
    public async Task Each_action_is_an_activity_event_that_concerns_the_task()
    {
        var (timers, subjects, clock) = Build();
        var task = await TaskAsync(subjects);

        await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);
        clock.Advance(TimeSpan.FromSeconds(1));
        await timers.ApplyAsync(task.Urn, TimerActions.Stop, Ct);

        await using var c = new NpgsqlConnection(postgres.ConnectionString);
        await c.OpenAsync(Ct);
        var rows = (await c.QueryAsync<(string Kind, string Action, string Relation)>(new CommandDefinition(
            """
            SELECT e.kind, e.payload->>'timer', se.relation
            FROM bsk.event e JOIN bsk.subject_event se ON se.event_id = e.id
            WHERE se.subject_id = @id
            ORDER BY e.occurred_at;
            """,
            new { id = task.Id }, cancellationToken: Ct))).ToList();

        Assert.Equal(
            new[] { (EventKinds.Activity, "start", "concerns"), (EventKinds.Activity, "stop", "concerns") },
            rows.Select(r => (r.Kind, r.Action, r.Relation)));
    }

    [Fact]
    public async Task Starting_a_timer_pauses_the_one_already_running()
    {
        var (timers, subjects, clock) = Build();
        var first = await TaskAsync(subjects);
        var second = await TaskAsync(subjects);

        await timers.ApplyAsync(first.Urn, TimerActions.Start, Ct);
        clock.Advance(TimeSpan.FromSeconds(20));
        var switched = await timers.ApplyAsync(second.Urn, TimerActions.Start, Ct);

        Assert.Contains(switched.Paused, p => p.Id == first.Id);
        var firstTimer = await timers.GetAsync(first.Urn, Ct);
        Assert.Equal(TimerStates.Paused, firstTimer.State);
        Assert.Equal(20, firstTimer.TotalSeconds);

        // Resuming the first pauses the second in turn.
        clock.Advance(TimeSpan.FromSeconds(7));
        var back = await timers.ApplyAsync(first.Urn, TimerActions.Resume, Ct);
        Assert.Contains(back.Paused, p => p.Id == second.Id);
        Assert.Equal(7, (await timers.GetAsync(second.Urn, Ct)).TotalSeconds);

        clock.Advance(TimeSpan.FromSeconds(1));
        await timers.ApplyAsync(first.Urn, TimerActions.Stop, Ct);
        await timers.ApplyAsync(second.Urn, TimerActions.Stop, Ct);
    }

    [Fact]
    public async Task Complete_stops_the_timer_and_marks_the_task_completed()
    {
        var (timers, subjects, clock) = Build();
        var task = await TaskAsync(subjects);

        await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);
        clock.Advance(TimeSpan.FromSeconds(45));
        var done = await timers.CompleteAsync(task.Urn, Ct);

        Assert.True(done.Completed);
        Assert.Equal(TimerStates.Stopped, done.Timer.State);
        Assert.Equal(45, done.Timer.TotalSeconds);

        await using var c = new NpgsqlConnection(postgres.ConnectionString);
        await c.OpenAsync(Ct);
        var status = await c.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current_source WHERE subject_id = @id;",
            new { id = task.Id }, cancellationToken: Ct));
        Assert.Equal(TimerService.CompletedStatus, status);
    }

    [Fact]
    public async Task Complete_works_on_a_task_that_was_never_timed()
    {
        var (timers, subjects, _) = Build();
        var task = await TaskAsync(subjects);

        var done = await timers.CompleteAsync(task.Urn, Ct);

        Assert.True(done.Completed);
        Assert.Equal(0, done.Timer.TotalSeconds);
    }

    [Fact]
    public async Task Actions_that_do_not_fit_the_current_state_are_refused_and_write_nothing()
    {
        var (timers, subjects, clock) = Build();
        var task = await TaskAsync(subjects);

        await Assert.ThrowsAsync<InvalidOperationException>(() => timers.ApplyAsync(task.Urn, TimerActions.Pause, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => timers.ApplyAsync(task.Urn, TimerActions.Resume, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => timers.ApplyAsync(task.Urn, TimerActions.Stop, Ct));

        await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => timers.ApplyAsync(task.Urn, TimerActions.Start, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => timers.ApplyAsync(task.Urn, TimerActions.Resume, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => timers.ApplyAsync(task.Urn, "lap", Ct));

        clock.Advance(TimeSpan.FromSeconds(3));
        await timers.ApplyAsync(task.Urn, TimerActions.Stop, Ct);
        Assert.Equal(3, (await timers.GetAsync(task.Urn, Ct)).TotalSeconds);
    }

    [Fact]
    public async Task The_timer_is_kept_on_tasks_only()
    {
        var (timers, subjects, _) = Build();
        var project = await subjects.CreateAsync(SubjectTypes.Project, $"not a task {Guid.NewGuid():N}", cancellationToken: Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => timers.ApplyAsync(project.Urn, TimerActions.Start, Ct));
    }

    [Fact]
    public async Task A_write_decided_against_a_stale_state_is_a_conflict_and_lands_nothing()
    {
        var (timers, subjects, clock) = Build();
        var task = await TaskAsync(subjects);
        await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);

        // Another window still believes the timer is stopped and tries to start it.
        var repository = new NpgsqlTimerRepository(postgres.ConnectionString);
        var stale = new TimerWrite(task.Id, TimerStates.Stopped, new NewEvent(
            EventKinds.Activity, Provenances.Declared, clock.UtcNow, clock.UtcNow, KernelSources.Cli,
            PayloadJson: $$"""{"subject_id":"{{task.Id}}","timer":"start"}"""));
        await Assert.ThrowsAsync<TimerConflictException>(() => repository.AppendAsync([stale], Ct));

        await using var c = new NpgsqlConnection(postgres.ConnectionString);
        await c.OpenAsync(Ct);
        var starts = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE payload->>'subject_id' = @id AND payload->>'timer' = 'start';",
            new { id = task.Id.ToString() }, cancellationToken: Ct));
        Assert.Equal(1, starts);

        clock.Advance(TimeSpan.FromSeconds(1));
        await timers.ApplyAsync(task.Urn, TimerActions.Stop, Ct);
    }

    [Fact]
    public async Task The_reader_role_can_read_the_timer_view()
    {
        var (timers, subjects, clock) = Build();
        var task = await TaskAsync(subjects);
        await timers.ApplyAsync(task.Urn, TimerActions.Start, Ct);
        clock.Advance(TimeSpan.FromSeconds(2));
        await timers.ApplyAsync(task.Urn, TimerActions.Stop, Ct);

        await using var reader = new NpgsqlConnection(BskReader.ConnectionStringFrom(postgres.ConnectionString));
        await reader.OpenAsync(Ct);
        var total = await reader.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT total_seconds FROM bsk.v_task_timer WHERE subject_id = @id;",
            new { id = task.Id }, cancellationToken: Ct));
        Assert.Equal(2, total);
    }
}

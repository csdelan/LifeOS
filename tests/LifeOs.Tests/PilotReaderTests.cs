using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using PilotSubjectReader = LifeOs.Pilot.Reader.SubjectReader;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR110 (fifth review R3): the Pilot reads a status the moment any door sets it. It
/// read <c>bsk.v_subject_current</c>, which read the table only <c>bsk rebuild</c> writes, so a
/// status set on the web, by an agent or by a timer's Complete didn't show in its Browse, Tasks or
/// detail, and Flag to Inbox offered to re-open an item it wrongly thought was Completed. The view
/// now reads the live fold, and the Pilot no longer runs a rebuild after its own writes.
/// </summary>
public sealed class PilotReaderTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_status_set_through_the_kernel_reads_back_in_the_Pilot_with_no_rebuild()
    {
        DapperConventions.Apply();
        var provider = classDb.Services()
            .AddSingleton<IClock>(TestClock.Ticking(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)))
            .BuildServiceProvider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var status = provider.GetRequiredService<StatusService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"fix the gate {ns}", cancellationToken: Ct);
        await status.ChangeStatusAsync(task.Urn, "In progress", Ct);
        await status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        // As the Pilot reads: the bsk_reader role, and nothing has run bsk rebuild.
        var pilot = new PilotSubjectReader(BskReader.ConnectionStringFrom(classDb.ConnectionString));

        Assert.Equal("Completed", pilot.GetSubject(task.Id)?.Status);
        Assert.Equal("Completed", pilot.GetSubjectByUrn(task.Urn)?.Status);
        var listed = Assert.Single(pilot.QuerySubjects(SubjectTypes.Task), s => s.Id == task.Id);
        Assert.Equal("Completed", listed.Status);
    }

    /// <summary>
    /// Code review CR152: the Pilot reads one Task's timer by its key as text, so the lookup goes
    /// below the view's window (EventIndexTests plans the same SQL); here it still finds the timer.
    /// </summary>
    [Fact]
    public async Task The_Pilot_reads_one_tasks_running_timer_and_none_for_a_task_never_timed()
    {
        DapperConventions.Apply();
        var provider = classDb.Services().BuildServiceProvider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var ns = Guid.NewGuid().ToString("N");
        var timed = await subjects.CreateAsync(SubjectTypes.Task, $"paint the fence {ns}", cancellationToken: Ct);
        var untimed = await subjects.CreateAsync(SubjectTypes.Task, $"oil the hinge {ns}", cancellationToken: Ct);
        await provider.GetRequiredService<TimerService>().ApplyAsync(timed.Urn, "start", Ct);

        var pilot = new PilotSubjectReader(BskReader.ConnectionStringFrom(classDb.ConnectionString));

        var timer = pilot.GetTimer(timed.Id);
        Assert.Equal((timed.Id, "running"), (timer?.SubjectId, timer?.State));
        Assert.Null(pilot.GetTimer(untimed.Id));
    }
}

using System.Reflection;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR113 (fifth review R6): a write that committed never answers as a refusal. The
/// status, attribute, archive and adherence services ran the reward reconcile and a repeating
/// Task's follow-ups after their commit, and a failure there (the reconcile's last lost race, a
/// series that kept changing) escaped as "Nothing was written" though the event was in the log, so
/// a retry wrote it twice. Each test makes the follow-up's port throw and checks the write answers,
/// pending, with its event in the log and the failure told to the log.
/// </summary>
public sealed class AfterCommitTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string EveryDay = Recurrence.Weekly(["mon", "tue", "wed", "thu", "fri", "sat", "sun"]);

    [Fact]
    public async Task A_status_whose_reconcile_fails_is_written_and_answered_pending()
    {
        var (kernel, ports, log) = Build();
        var task = await kernel.GetRequiredService<SubjectService>().CreateAsync(SubjectTypes.Task, Unique("Fix the gate"), cancellationToken: Ct);

        ports.Fail(nameof(ILedgerRepository));
        var result = await kernel.GetRequiredService<StatusService>().ChangeStatusAsync(task.Urn, "Completed", Ct);

        Assert.True(result.FollowUpsPending);
        Assert.Empty(result.Awards);
        Assert.Equal("state_change", await KindOfAsync(result.EventId));
        Assert.Equal(["the reward reconcile"], log.Failed);
    }

    [Fact]
    public async Task A_check_off_whose_reconcile_fails_is_written_and_answered_pending()
    {
        var (kernel, ports, log) = Build();
        var habit = await kernel.CreateTrackedHabitAsync(Unique("Stretch"), cancellationToken: Ct);

        ports.Fail(nameof(ILedgerRepository));
        var result = await kernel.GetRequiredService<AdherenceService>().RecordAsync(habit.Urn, AdherenceResults.Followed, cancellationToken: Ct);

        Assert.True(result.FollowUpsPending);
        Assert.Equal("activity", await KindOfAsync(result.EventId));
        Assert.Equal(["the reward reconcile"], log.Failed);
    }

    [Fact]
    public async Task A_series_edit_whose_follow_to_its_repetitions_fails_is_written_and_answered_pending()
    {
        var (kernel, ports, log) = Build();
        var series = await RepeatingAsync(kernel, "Vacuum");

        ports.Fail($"{nameof(ITaskSeriesRepository)}.{nameof(ITaskSeriesRepository.FollowAttributeAsync)}");
        var result = await kernel.GetRequiredService<AttributeService>().SetAsync(
            series.Urn, [new AttributeAssignment("priority", "Low")], Ct);

        Assert.True(result.FollowUpsPending);
        Assert.Equal("Low", await AttributeAsync(series.Id, "priority"));
        Assert.Equal(["carrying the edit to the open repetitions"], log.Failed);
    }

    [Fact]
    public async Task A_restore_whose_repeating_again_fails_is_written_and_answered_pending()
    {
        var (kernel, ports, log) = Build();
        var series = await RepeatingAsync(kernel, "Water the plants");
        var archive = kernel.GetRequiredService<ArchiveService>();
        await archive.ArchiveAsync(series.Urn, Ct);

        ports.Fail($"{nameof(ITaskSeriesRepository)}.{nameof(ITaskSeriesRepository.GetAsync)}");
        var result = await archive.RestoreAsync(series.Urn, Ct);

        Assert.True(result.FollowUpsPending);
        Assert.Equal("archive_change", await KindOfAsync(result.EventId));
        Assert.Equal(["repeating the restored task again"], log.Failed);
    }

    [Fact]
    public async Task With_nothing_failing_a_write_is_not_pending()
    {
        var (kernel, _, log) = Build();
        var task = await kernel.GetRequiredService<SubjectService>().CreateAsync(SubjectTypes.Task, Unique("Sweep"), cancellationToken: Ct);

        var result = await kernel.GetRequiredService<StatusService>().ChangeStatusAsync(task.Urn, "In progress", Ct);

        Assert.False(result.FollowUpsPending);
        Assert.Empty(log.Failed);
    }

    private (ServiceProvider Kernel, FailingPorts Ports, RecordingLog Log) Build()
    {
        var ports = new FailingPorts();
        var log = new RecordingLog();
        var services = classDb.Services();
        ports.Decorate<ILedgerRepository>(services);
        ports.Decorate<ITaskSeriesRepository>(services);
        services.AddSingleton<IFollowUpLog>(log);
        return (services.BuildServiceProvider(), ports, log);
    }

    private async Task<SubjectRef> RepeatingAsync(ServiceProvider kernel, string title)
    {
        var series = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Task, Unique(title), """{"priority":"High"}""", cancellationToken: Ct);
        var made = await kernel.GetRequiredService<RecurrenceService>().SetAsync(series.Urn, EveryDay, start: null, Ct);
        Assert.NotEmpty(made.Made);
        return series;
    }

    private static string Unique(string title) => $"{title} {Guid.NewGuid():N}";

    private async Task<string?> KindOfAsync(Guid eventId)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT kind FROM bsk.event WHERE id = @eventId;", new { eventId }, cancellationToken: Ct));
    }

    private async Task<string?> AttributeAsync(Guid id, string key)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT attributes->>@key FROM bsk.subject WHERE id = @id;", new { id, key }, cancellationToken: Ct));
    }

    private sealed class RecordingLog : IFollowUpLog
    {
        public List<string> Failed { get; } = [];

        void IFollowUpLog.Failed(string followUp, string about, Exception exception) => Failed.Add(followUp);
    }

    /// <summary>Ports that pass every call through until told to fail a port, or one method of it.</summary>
    public sealed class FailingPorts
    {
        private readonly HashSet<string> failing = [];

        public void Fail(string portOrMethod) => failing.Add(portOrMethod);

        internal bool Fails(Type port, string method)
            => failing.Contains(port.Name) || failing.Contains($"{port.Name}.{method}");

        public void Decorate<T>(IServiceCollection services)
            where T : class
        {
            var original = services.Last(d => d.ServiceType == typeof(T));
            services.Add(ServiceDescriptor.Singleton(sp => FailingProxy<T>.Wrap(Build<T>(original, sp), this)));
        }

        private static T Build<T>(ServiceDescriptor descriptor, IServiceProvider sp)
            where T : class
            => (T)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
    }

    public class FailingProxy<T> : DispatchProxy
        where T : class
    {
        private T inner = null!;
        private FailingPorts ports = null!;

        internal static T Wrap(T inner, FailingPorts ports)
        {
            var proxy = Create<T, FailingProxy<T>>();
            var self = (FailingProxy<T>)(object)proxy;
            self.inner = inner;
            self.ports = ports;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (ports.Fails(typeof(T), targetMethod!.Name))
            {
                throw new InvalidOperationException($"{typeof(T).Name}.{targetMethod.Name} failed.");
            }

            try
            {
                return targetMethod.Invoke(inner, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw e.InnerException;
            }
        }
    }
}

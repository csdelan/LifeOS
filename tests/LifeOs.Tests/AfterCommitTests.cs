using System.Reflection;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Application.Home;
using LifeOs.Application.Rewards;
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
        Assert.Equal("state_change", await KindOfAsync(result.EventId!.Value));
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
    public async Task A_meal_whose_settle_fails_is_written_and_answered_pending()
    {
        var (kernel, ports, log) = Build();

        ports.Fail(nameof(ILedgerRepository));
        var result = await kernel.GetRequiredService<FoodLogService>().LogAsync(
            new FoodEntryInput(Unique("Oatmeal"), 4, 300), cancellationToken: Ct);

        Assert.True(result.FollowUpsPending);
        Assert.Empty(result.Earned.Awards);
        Assert.Equal("measurement", await KindOfMealAsync(result.Meal!.Id));
        Assert.Equal(["settling the food days"], log.Failed);
    }

    [Fact]
    public async Task A_room_rating_whose_rank_up_fails_is_written_and_answered_pending()
    {
        var (kernel, ports, log) = Build();
        var space = await kernel.GetRequiredService<SpaceService>().AddAsync(Unique("Den"), 3, 3, Ct);

        ports.Fail($"{nameof(IDomainCreditReader)}.{nameof(IDomainCreditReader.RereadBesideAsync)}");
        var result = await kernel.GetRequiredService<SpaceRatingService>().RateAsync(
            null, [new RatingInput(space.Name, 4, 3.5m, 4)], Ct);

        Assert.True(result.FollowUpsPending);
        Assert.Single(result.Ratings);
        Assert.Equal(["paying the rank-ups the change reached"], log.Failed);
    }

    [Fact]
    public async Task An_exception_day_whose_rank_up_fails_is_written_and_answered_pending()
    {
        var (kernel, ports, log) = Build();

        ports.Fail($"{nameof(IDomainCreditReader)}.{nameof(IDomainCreditReader.RereadBesideAsync)}");
        var result = await kernel.GetRequiredService<ExceptionDayService>().MarkAsync(null, Unique("Flu"), Ct);

        Assert.True(result.FollowUpsPending);
        Assert.NotNull(result.Exception);
        Assert.Equal(["paying the rank-ups the change reached"], log.Failed);
    }

    [Fact]
    public async Task A_repeating_task_whose_first_repetition_fails_is_made_and_answered_pending()
    {
        var (kernel, ports, log) = Build();

        ports.Fail($"{nameof(ITaskSeriesRepository)}.{nameof(ITaskSeriesRepository.CreateAsync)}");
        var made = await kernel.GetRequiredService<SubjectCreationService>().CreateAsync(
            SubjectTypes.Task, Unique("Take out the bins"), $$"""{"recurrence":{{EveryDay}}}""", cancellationToken: Ct);

        Assert.True(made.FollowUpsPending);
        Assert.Equal(SubjectTypes.Task, await TypeOfAsync(made.Subject.Id));
        Assert.Equal(["making the new task's first repetition"], log.Failed);
    }

    [Fact]
    public async Task A_recurrence_whose_repetitions_fail_is_set_and_answered_pending()
    {
        var (kernel, ports, log) = Build();
        var task = await kernel.GetRequiredService<SubjectService>().CreateAsync(SubjectTypes.Task, Unique("Mop"), cancellationToken: Ct);

        ports.Fail($"{nameof(ITaskSeriesRepository)}.{nameof(ITaskSeriesRepository.CreateAsync)}");
        var result = await kernel.GetRequiredService<RecurrenceService>().SetAsync(task.Urn, EveryDay, start: null, Ct);

        Assert.True(result.FollowUpsPending);
        Assert.Empty(result.Made);
        Assert.NotNull(await AttributeAsync(task.Id, "recurrence"));
        Assert.Equal(["making the repeating task's repetitions"], log.Failed);
    }

    [Fact]
    public async Task A_tag_whose_follow_to_the_repetitions_fails_is_added_and_answered_pending()
    {
        var (kernel, ports, log) = Build();
        var series = await RepeatingAsync(kernel, "Dust");

        ports.Fail($"{nameof(ITaskSeriesRepository)}.{nameof(ITaskSeriesRepository.FollowTagsAsync)}");
        var result = await kernel.GetRequiredService<TagService>().ApplyAsync(series.Urn, ["chores"], [], Ct);

        Assert.True(result.FollowUpsPending);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(["carrying the tags to the open repetitions"], log.Failed);
    }

    /// <summary>
    /// The class (code review CR139): every call in Application to a repeating Task's follow-up runs
    /// inside <see cref="AfterCommit"/>, whose work gets the token <c>ct</c>; one called with the
    /// caller's token runs bare after the commit, and its failure would read as "Nothing was
    /// written". The guards and the rename, which run before or inside the write, aren't follow-ups.
    /// </summary>
    [Fact]
    public void Every_repeating_task_follow_up_in_Application_runs_after_the_commit_through_AfterCommit()
    {
        var call = new System.Text.RegularExpressions.Regex(
            @"\b(taskSeries|triage)\.(AfterPatternAsync|FollowAsync|FollowTagsAsync|AfterStatusAsync|AfterRestoreAsync|FlagItemAsync)\((?<args>[^;]*)");
        var source = Path.Combine(RepoRoot.Path, "src", "LifeOs.Application", "Subjects");
        var calls = Directory.EnumerateFiles(source, "*Service.cs")
            .Where(f => !f.EndsWith("TaskSeriesService.cs", StringComparison.Ordinal)
                && !f.EndsWith("TriageService.cs", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Select(l => (l.File, l.Line, Match: call.Match(l.Text)))
            .Where(l => l.Match.Success)
            .ToList();

        Assert.NotEmpty(calls);
        Assert.Empty(calls
            .Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l.Match.Groups["args"].Value, @"\bct\)"))
            .Select(l => $"{l.File}:{l.Line}"));
    }

    /// <summary>
    /// The class, not the cases above (code review CR129): every result in Application that answers
    /// what a write earned also says whether what ran after its commit is pending, so a write that
    /// pays after it commits is built through <see cref="AfterCommit"/> rather than throwing.
    /// </summary>
    [Fact]
    public void Every_write_result_that_answers_what_it_earned_says_when_its_follow_ups_are_pending()
    {
        var missing = typeof(AfterCommit).Assembly.GetExportedTypes()
            .Where(t => t.GetProperties().Any(p => p.PropertyType == typeof(Earned)))
            .Where(t => t.GetProperty("FollowUpsPending")?.PropertyType != typeof(bool))
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(missing);
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
        ports.Decorate<IDomainCreditReader>(services);
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

    private async Task<string?> KindOfMealAsync(Guid mealId)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT kind FROM bsk.event WHERE payload->'meal'->>'id' = @id;", new { id = mealId.ToString() }, cancellationToken: Ct));
    }

    private async Task<string?> TypeOfAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT type FROM bsk.subject WHERE id = @id;", new { id }, cancellationToken: Ct));
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

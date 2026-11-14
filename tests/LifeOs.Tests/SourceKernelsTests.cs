using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Reviews;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// An agent writes through a kernel of its own (code review CR45): the host's settings and clock,
/// with only the source changed, so a service the agent's tools reach, such as a status change,
/// can't write as the host.
/// </summary>
public sealed class SourceKernelsTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_source_gets_the_hosts_kernel_with_only_its_source_changed()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var host = classDb.Services(sourceId: KernelSources.Api)
            .AddSingleton<IClock>(clock)
            .BuildServiceProvider();
        await using var kernels = new SourceKernels(host.GetRequiredService<KernelSettings>(), host.GetRequiredService<IClock>());
        const string agentSource = "mcp:source-kernel-test";

        var agent = kernels.For(agentSource);

        Assert.Same(agent, kernels.For(agentSource));
        Assert.Equal(agentSource, agent.GetRequiredService<EventSource>().Id);
        Assert.Same(clock, agent.GetRequiredService<IClock>());
        Assert.Equal(KernelSources.Api, host.GetRequiredService<EventSource>().Id);

        // Every service an agent's write tools use is the kernel's own.
        foreach (var service in new[]
                 {
                     typeof(RelateService), typeof(TriageService), typeof(InboxFilingService),
                     typeof(JournalService), typeof(ReviewService), typeof(ReflectionService),
                 })
        {
            Assert.NotNull(agent.GetRequiredService(service));
        }

        var task = await agent.GetRequiredService<SubjectService>()
            .CreateAsync(SubjectTypes.Task, $"source kernel task {Guid.NewGuid():N}", cancellationToken: Ct);
        await agent.GetRequiredService<StatusService>().ChangeStatusAsync(task.Urn, "Completed", Ct);

        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        var sources = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT source_id FROM bsk.event
            WHERE payload->>'subject_id' = @id
               OR id IN (SELECT event_id FROM bsk.subject_event WHERE subject_id = @subject)
            """,
            new { id = task.Id.ToString(), subject = task.Id }, cancellationToken: Ct))).ToList();
        Assert.Equal([agentSource], sources);
    }
}

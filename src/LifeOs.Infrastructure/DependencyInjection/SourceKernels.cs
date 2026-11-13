using System.Collections.Concurrent;
using LifeOs.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Infrastructure.DependencyInjection;

/// <summary>
/// The host's kernel again for each other source it writes as, such as an agent's
/// <c>mcp:&lt;client_id&gt;</c> (agent-access plan §5.3, code review CR45): the same settings, data
/// source and clock from <see cref="ServiceCollectionExtensions.AddLifeOsKernel(IServiceCollection, KernelSettings)"/>,
/// with only <see cref="KernelSettings.SourceId"/> changed, so every service, and every event it
/// writes, is the kernel's own and says who wrote it. One kernel per source, built when first
/// asked for and kept while the host runs.
/// </summary>
public sealed class SourceKernels(KernelSettings settings, IClock clock) : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<ServiceProvider>> kernels = new(StringComparer.Ordinal);

    /// <summary>The kernel whose events carry <paramref name="sourceId"/>.</summary>
    public IServiceProvider For(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return kernels.GetOrAdd(sourceId, id => new Lazy<ServiceProvider>(() => Build(id))).Value;
    }

    private ServiceProvider Build(string sourceId)
        => new ServiceCollection()
            .AddLifeOsKernel(settings with { SourceId = sourceId })
            .AddSingleton(clock)
            .BuildServiceProvider();

    public async ValueTask DisposeAsync()
    {
        foreach (var kernel in kernels.Values.Where(k => k.IsValueCreated))
        {
            await kernel.Value.DisposeAsync();
        }

        kernels.Clear();
    }

    public void Dispose()
    {
        foreach (var kernel in kernels.Values.Where(k => k.IsValueCreated))
        {
            kernel.Value.Dispose();
        }

        kernels.Clear();
    }
}

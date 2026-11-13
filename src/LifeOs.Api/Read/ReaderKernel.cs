using LifeOs.Api.Mcp;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Api.Read;

/// <summary>
/// The API's readers, built by the container from a kernel on the reader connection
/// (<c>bsk_reader</c>) with the host's settings, clock and object storage (code review CR81, the
/// read side of CR18): the clock and the storage are the ones the host's own container holds, so
/// what a host or a test puts there reaches the readers too. A reader takes the kernel's repositories, reads, calendar and data source, so its home
/// zone, coin value, clock and connection pool are the host's: no reader has a default to fall
/// back on, and none opens a pool of its own. The host's container hands out each reader from
/// here (<see cref="ReaderKernelHosting.AddLifeOsReaders"/>).
/// </summary>
public sealed class ReaderKernel : IAsyncDisposable, IDisposable
{
    /// <summary>Every reader the kernel builds; the host's container hands out each of them.</summary>
    public static readonly IReadOnlyList<Type> Readers =
    [
        typeof(ActivityReader),
        typeof(ArtifactReader),
        typeof(ChangeReader),
        typeof(EnvironmentReader),
        typeof(FoodReader),
        typeof(MeasurementReader),
        typeof(McpReads),
        typeof(ReviewReader),
        typeof(RewardReader),
        typeof(SubjectReader),
    ];

    private readonly ServiceProvider provider;

    /// <param name="settings">The host's kernel settings: its home zone, coin value, storage and integrations.</param>
    /// <param name="readerConnectionString">The connection every reader reads on.</param>
    /// <param name="clock">The host's clock.</param>
    /// <param name="objectStorage">The host's object storage, or null when it has none.</param>
    public ReaderKernel(KernelSettings settings, string readerConnectionString, IClock clock, IObjectStorage? objectStorage)
        => provider = Services(settings, readerConnectionString, clock, objectStorage).BuildServiceProvider();

    /// <summary>The kernel on <paramref name="readerConnectionString"/>, on the host's clock and storage, with every reader.</summary>
    public static IServiceCollection Services(
        KernelSettings settings, string readerConnectionString, IClock clock, IObjectStorage? objectStorage)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(readerConnectionString);
        var services = new ServiceCollection()
            .AddLifeOsKernel(settings with { ConnectionString = readerConnectionString })
            .AddSingleton(clock);
        if (objectStorage is not null)
        {
            services.AddSingleton(objectStorage);
        }

        foreach (var reader in Readers)
        {
            services.AddSingleton(reader);
        }

        return services;
    }

    /// <summary>The reader (or kernel piece) <typeparamref name="T"/>.</summary>
    public T Get<T>()
        where T : notnull
        => provider.GetRequiredService<T>();

    /// <summary>The reader (or kernel piece) of <paramref name="type"/>.</summary>
    public object Get(Type type) => provider.GetRequiredService(type);

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    public void Dispose() => provider.Dispose();
}

/// <summary>Registers the <see cref="ReaderKernel"/> in a host and hands out its readers.</summary>
public static class ReaderKernelHosting
{
    /// <summary>
    /// The readers on <paramref name="readerConnectionString"/>, from a <see cref="ReaderKernel"/>
    /// built on this host's <see cref="KernelSettings"/>, <see cref="IClock"/> and <see cref="IObjectStorage"/>; call it after
    /// <see cref="ServiceCollectionExtensions.AddLifeOsKernel(IServiceCollection, KernelSettings)"/>.
    /// The kernel is built when a reader is first asked for, so a clock or storage a test registers later is the one it gets.
    /// </summary>
    public static IServiceCollection AddLifeOsReaders(this IServiceCollection services, string readerConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(readerConnectionString);
        services.AddSingleton(sp => new ReaderKernel(
            sp.GetRequiredService<KernelSettings>(), readerConnectionString, sp.GetRequiredService<IClock>(),
            sp.GetService<IObjectStorage>()));
        foreach (var reader in ReaderKernel.Readers)
        {
            services.AddSingleton(reader, sp => sp.GetRequiredService<ReaderKernel>().Get(reader));
        }

        return services;
    }
}

using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The container builds the kernel's services from their constructors (code review CR18). These
/// check that every one it registers can be built, and that a setting reaches the service that
/// takes it rather than the parameter's default. Nothing here opens a connection.
/// </summary>
public sealed class KernelCompositionTests
{
    private const string NoDatabase = "Host=localhost;Database=lifeos_composition_check";

    [Fact]
    public void Every_registered_service_can_be_built()
    {
        var services = new ServiceCollection().AddLifeOsKernel(new KernelSettings(NoDatabase));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        foreach (var descriptor in services)
        {
            Assert.NotNull(provider.GetRequiredService(descriptor.ServiceType));
        }
    }

    // Code review CR45: a service the kernel doesn't register can only be built by hand, so a kernel
    // built for another source, such as an agent's (SourceKernels), can't reach it. A *Reads class
    // may be built on the API's reader connection instead, so only services are checked.
    [Fact]
    public void Every_application_service_is_registered()
    {
        var registered = new ServiceCollection().AddLifeOsKernel(new KernelSettings(NoDatabase))
            .Select(d => d.ServiceType).ToHashSet();

        var missing = typeof(SubjectService).Assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Service", StringComparison.Ordinal))
            .Where(t => !registered.Contains(t))
            .Select(t => t.Name)
            .Order()
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void The_host_settings_reach_the_services_that_take_them()
    {
        var settings = new KernelSettings(NoDatabase)
        {
            SourceId = KernelSources.Api,
            CoinValueUsd = 2.5m,
            AllowPhotosInDatabase = false,
        };
        using var provider = new ServiceCollection().AddLifeOsKernel(settings).BuildServiceProvider();

        Assert.Equal(KernelSources.Api, provider.GetRequiredService<EventSource>().Id);
        Assert.Equal(2.5m, provider.GetRequiredService<RewardService>().CoinValueUsd);
        Assert.False(provider.GetRequiredService<FoodPhotoService>().Enabled);
        Assert.Same(KernelDataSource.For(NoDatabase), provider.GetRequiredService<NpgsqlDataSource>());
    }

    [Fact]
    public void Resolve_takes_what_the_host_gave_over_the_environment()
    {
        var settings = KernelSettings.Resolve(NoDatabase, KernelSources.Api, coinValueUsd: 1m, homeTimeZone: "UTC");

        Assert.Equal(KernelSources.Api, settings.SourceId);
        Assert.Equal(1m, settings.CoinValueUsd);
        Assert.Equal("UTC", settings.HomeTimeZone);
    }
}

using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Application.Rewards;
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

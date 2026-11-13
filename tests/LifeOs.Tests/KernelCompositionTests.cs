using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Application.Home;
using LifeOs.Application.Measurements;
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

    // Code review CR71: bsk kept food, space and lab-report bytes in any database it was pointed
    // at, staging's included, since the setting defaulted to true; only the API refused them, and
    // only outside Development. Now both keep them only in a database on this machine, unless
    // object storage is set up or the host says otherwise.
    [Theory]
    [InlineData("Host=localhost;Database=lifeos;Username=lifeos", true)]
    [InlineData("Host=127.0.0.1;Port=55432;Database=lifeos_test", true)]
    [InlineData("Host=aws-0-us-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.ref", false)]
    [InlineData("Host=localhost,db.example.com;Database=lifeos", false)]
    public void Photos_are_kept_in_the_database_only_when_it_is_on_this_machine(string connectionString, bool kept)
    {
        Assert.Equal(kept, new KernelSettings(connectionString).AllowPhotosInDatabase);
        Assert.Equal(kept, KernelSettings.Resolve(connectionString, homeTimeZone: "UTC").AllowPhotosInDatabase);

        // bsk as it's built, unless this shell sets up object storage, which keeps photos anywhere.
        if (LifeOs.Infrastructure.Storage.SupabaseStorageOptions.FromEnvironment() is null)
        {
            using var bsk = LifeOs.Cli.Cli.BuildServices(connectionString);
            Assert.Equal(kept, bsk.GetRequiredService<FoodPhotoService>().Enabled);
            Assert.Equal(kept, bsk.GetRequiredService<SpacePhotoService>().Enabled);
            Assert.Equal(kept, bsk.GetRequiredService<LabVisitService>().ReportsEnabled);
        }

        using var allowed = new ServiceCollection()
            .AddLifeOsKernel(connectionString, homeTimeZone: "UTC", allowPhotosInDatabase: true)
            .BuildServiceProvider();
        Assert.True(allowed.GetRequiredService<FoodPhotoService>().Enabled);
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

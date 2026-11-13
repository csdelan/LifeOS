using LifeOs.Api.Read;
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

    // Code review CR80: a feature's follow-up on a generic verb (a repeating Task's next repetition,
    // a timer stopped when its Task closes, a space's or metric's name checked on restore) was an
    // optional parameter defaulting to null, so a verb built without it skipped the follow-up
    // silently. A dependency the kernel registers is always required, so a missing one fails to build.
    [Fact]
    public void No_service_takes_a_registered_dependency_as_optional()
    {
        var registered = new ServiceCollection().AddLifeOsKernel(new KernelSettings(NoDatabase))
            .Select(d => d.ServiceType).ToHashSet();

        var optional = new[] { typeof(SubjectService).Assembly, typeof(NpgsqlEventStore).Assembly }
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => (Type: t, Parameter: p)))
            .Where(x => x.Parameter.HasDefaultValue && registered.Contains(x.Parameter.ParameterType))
            .Select(x => $"{x.Type.Name}({x.Parameter.ParameterType.Name} {x.Parameter.Name})")
            .Distinct()
            .Order()
            .ToList();

        Assert.Empty(optional);
    }

    // Code review CR81: the API built its readers by hand from the reader's connection string, each
    // with a home zone and a clock that defaulted to Pacific and the system clock when not given, and
    // four of them opened connections outside the kernel's pool. The container builds them now, from
    // a kernel on the reader connection with the host's settings and clock.
    private const string NoReader = "Host=localhost;Database=lifeos_composition_check;Username=bsk_reader";

    [Fact]
    public void Every_reader_is_built_from_the_reader_kernel_with_nothing_left_to_a_default()
    {
        var services = ReaderKernel.Services(new KernelSettings(NoDatabase), NoReader, new TestClock(DateTimeOffset.UtcNow), objectStorage: null);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        foreach (var reader in ReaderKernel.Readers)
        {
            Assert.NotNull(provider.GetRequiredService(reader));
        }

        var defaulted = ReaderKernel.Readers
            .SelectMany(r => r.GetConstructors().SelectMany(c => c.GetParameters()).Where(p => p.HasDefaultValue).Select(p => $"{r.Name}({p.Name})"))
            .ToList();
        Assert.Empty(defaulted);
    }

    [Fact]
    public void Every_reader_in_the_api_is_one_the_reader_kernel_builds()
    {
        var missing = typeof(ReaderKernel).Assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && (t.Name.EndsWith("Reader", StringComparison.Ordinal) || t.Name.EndsWith("Reads", StringComparison.Ordinal)))
            .Except(ReaderKernel.Readers)
            .Select(t => t.Name)
            .Order()
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void The_host_hands_out_the_reader_kernels_readers_on_its_own_settings_clock_and_storage()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
        var storage = new MemoryObjectStorage();
        var settings = new KernelSettings(NoDatabase) { HomeTimeZone = "Pacific/Kiritimati", CoinValueUsd = 2.5m };
        // The clock and storage go in after the readers, as a test host's do.
        using var host = new ServiceCollection()
            .AddLifeOsKernel(settings)
            .AddLifeOsReaders(NoReader)
            .AddSingleton<IClock>(clock)
            .AddSingleton<IObjectStorage>(storage)
            .BuildServiceProvider();
        var kernel = host.GetRequiredService<ReaderKernel>();

        foreach (var reader in ReaderKernel.Readers)
        {
            Assert.Same(kernel.Get(reader), host.GetRequiredService(reader));
        }

        Assert.Same(KernelDataSource.For(NoReader), kernel.Get<NpgsqlDataSource>());
        Assert.Same(clock, kernel.Get<IClock>());
        Assert.Same(storage, kernel.Get<IObjectStorage>());
        Assert.Equal(("Pacific/Kiritimati", 2.5m), (kernel.Get<KernelSettings>().HomeTimeZone, kernel.Get<KernelSettings>().CoinValueUsd));
        Assert.Equal("Pacific/Kiritimati", kernel.Get<LifeOs.Api.Mcp.McpReads>().HomeTimeZone);
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

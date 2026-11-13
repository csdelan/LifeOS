using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Application.Capture;
using LifeOs.Application.Covers;
using LifeOs.Application.Food;
using LifeOs.Application.Home;
using LifeOs.Application.Measurements;
using LifeOs.Application.Reviews;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.Diagnostics;
using LifeOs.Infrastructure.Integrations;
using LifeOs.Infrastructure.Migrations;
using LifeOs.Infrastructure.Persistence;
using LifeOs.Infrastructure.Rebuild;
using LifeOs.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Infrastructure.DependencyInjection;

/// <summary>
/// Composition root: registers the application services and their Postgres-backed
/// implementations for one <see cref="KernelSettings"/>. This is the only place wiring lives;
/// the CLI, the API and the tests build on it.
/// <para>
/// The container builds each service from its constructor (code review CR18), from the
/// repositories, the clock, the shared <see cref="NpgsqlDataSource"/> and the host's
/// <see cref="EventSource"/>. Only a service that takes a setting is built by hand here, so that
/// setting can't fall back to its parameter's default unseen.
/// </para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The kernel on <paramref name="connectionString"/>, with the settings a host gave and the
    /// rest from the environment (<see cref="KernelSettings.Resolve"/>).
    /// </summary>
    public static IServiceCollection AddLifeOsKernel(
        this IServiceCollection services, string connectionString, string sourceId = KernelSources.Cli,
        decimal? coinValueUsd = null, string? homeTimeZone = null,
        SupabaseStorageOptions? objectStorage = null, bool allowPhotosInDatabase = true,
        IntegrationSettings? integrations = null)
        => services.AddLifeOsKernel(KernelSettings.Resolve(
            connectionString, sourceId, coinValueUsd, homeTimeZone, objectStorage, allowPhotosInDatabase, integrations));

    public static IServiceCollection AddLifeOsKernel(this IServiceCollection services, KernelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DapperConventions.Apply();

        services.AddSingleton(settings);
        services.AddSingleton(new EventSource(settings.SourceId));
        services.AddSingleton(KernelDataSource.For(settings.ConnectionString));
        services.AddSingleton<IClock, SystemClock>();

        AddStore(services, settings);
        AddServices(services, settings);

        // Operational services that work directly against the store.
        services.AddSingleton(_ => new MigrationRunner(settings.ConnectionString));
        services.AddSingleton(_ => new DerivedRebuilder(settings.ConnectionString));
        services.AddSingleton(_ => new DiagnosticRunner(settings.ConnectionString));

        return services;
    }

    /// <summary>The repositories, the artifact stores and the outside services.</summary>
    private static void AddStore(IServiceCollection services, KernelSettings settings)
    {
        if (settings.ObjectStorage is { } storage)
        {
            services.AddSingleton<IObjectStorage>(_ =>
                new SupabaseObjectStorage(SupabaseObjectStorage.CreateHttpClient(), storage));
        }

        services.AddSingleton<IArtifactStore, NpgsqlArtifactStore>();
        services.AddSingleton<IArtifactBlobStore, NpgsqlArtifactBlobStore>();
        services.AddSingleton<IEventStore, NpgsqlEventStore>();
        services.AddSingleton<IEventReader, NpgsqlEventReader>();
        services.AddSingleton<ISubjectRepository, NpgsqlSubjectRepository>();
        services.AddSingleton<IRelationRepository, NpgsqlRelationRepository>();
        services.AddSingleton<IActivityWriter, NpgsqlActivityWriter>();
        services.AddSingleton<ISubjectEventRepository, NpgsqlSubjectEventRepository>();
        services.AddSingleton<ITagRepository, NpgsqlTagRepository>();
        services.AddSingleton<IAppointmentRepository, NpgsqlAppointmentRepository>();
        services.AddSingleton<ITimerRepository, NpgsqlTimerRepository>();
        services.AddSingleton<ITaskSeriesRepository, NpgsqlTaskSeriesRepository>();
        services.AddSingleton<IFoodLogRepository, NpgsqlFoodLogRepository>();
        services.AddSingleton<ISpaceRepository, NpgsqlSpaceRepository>();
        services.AddSingleton<IMetricRepository, NpgsqlMetricRepository>();
        services.AddSingleton<IReviewRepository, NpgsqlReviewRepository>();
        services.AddSingleton<IActivityRepository, NpgsqlActivityRepository>();

        // These take the home time zone or the token key.
        services.AddSingleton<ILedgerRepository>(sp =>
            new NpgsqlLedgerRepository(sp.GetRequiredService<NpgsqlDataSource>(), settings.HomeTimeZone));
        services.AddSingleton<IHomeCalendar>(sp =>
            new NpgsqlHomeCalendar(sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<IClock>(), settings.HomeTimeZone));
        // The reads beside the ledger (code review CR19, CR47): the catalog and the domain credit.
        services.AddSingleton<IRewardCatalogReader, NpgsqlRewardCatalogReader>();
        services.AddSingleton<IDomainCreditReader>(sp =>
            new NpgsqlDomainCreditReader(sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<IClock>(), settings.HomeTimeZone));
        services.AddSingleton<IIntegrationRepository>(sp =>
            new NpgsqlIntegrationRepository(sp.GetRequiredService<NpgsqlDataSource>(), settings.Integrations?.TokenCipher));

        if (settings.Integrations?.Unsplash is { } unsplash)
        {
            services.AddSingleton<IUnsplashClient>(_ =>
                new UnsplashHttpClient(UnsplashHttpClient.CreateHttpClient(), unsplash));
        }

        if (settings.Integrations?.Oura is { } oura)
        {
            services.AddSingleton<IOuraClient>(sp =>
                new OuraHttpClient(OuraHttpClient.CreateHttpClient(), oura, sp.GetRequiredService<IClock>()));
        }
    }

    /// <summary>
    /// Photos and lab reports can be kept: in object storage (the host's, or one a test
    /// registers), or in the database where the host allows it.
    /// </summary>
    private static bool StoresPhotos(IServiceProvider services, KernelSettings settings)
        => settings.AllowPhotosInDatabase || services.GetService<IObjectStorage>() is not null;

    private static void AddServices(IServiceCollection services, KernelSettings settings)
    {
        // Subjects, their relations, statuses and repeats (D16).
        services.AddSingleton<SubjectService>();
        services.AddSingleton<TaskSeriesService>();
        services.AddSingleton<ChildCreationService>();
        services.AddSingleton<RelationService>();
        services.AddSingleton<AttributeService>();
        services.AddSingleton<RenameService>();
        services.AddSingleton<RecurrenceService>();
        services.AddSingleton<StatusService>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<PromotionService>();
        services.AddSingleton<SubjectCreationService>();
        services.AddSingleton<DecisionService>();
        services.AddSingleton<ActivityService>();
        services.AddSingleton<AdherenceService>();
        services.AddSingleton<TimerService>();
        services.AddSingleton<HabitTargetService>();
        services.AddSingleton<RelateService>();
        services.AddSingleton<TagService>();
        services.AddSingleton<PeopleService>();
        services.AddSingleton<AppointmentService>();
        services.AddSingleton<TriageService>();

        // Capture, the Inbox and journals (CAP-1 to CAP-4, JOURNAL-1).
        services.AddSingleton<CaptureService>();
        services.AddSingleton<InboxFilingService>();
        services.AddSingleton<JournalService>();
        services.AddSingleton(sp => new AttachmentService(
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<EventSource>(),
            settings.MaxArtifactBytes));

        // Rewards, domains and exception days (D11, D12, REWARD-8).
        services.AddSingleton<RankUpService>();
        services.AddSingleton<ItemPrices>();
        services.AddSingleton(sp => new RewardService(
            sp.GetRequiredService<ILedgerRepository>(),
            sp.GetRequiredService<RankUpService>(),
            sp.GetRequiredService<ItemPrices>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<EventSource>(),
            settings.CoinValueUsd));
        services.AddSingleton<RedemptionService>();
        services.AddSingleton<DomainService>();
        services.AddSingleton<ExceptionDayService>();
        services.AddSingleton<RewardCatalogService>();
        services.AddSingleton<BountyService>();

        // The Food Log (D13).
        services.AddSingleton<FoodRewards>();
        services.AddSingleton<FoodReads>();
        services.AddSingleton<FoodLogService>();
        services.AddSingleton(sp => new FoodPhotoService(
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<FoodLogService>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<EventSource>(),
            enabled: StoresPhotos(sp, settings)));

        // The home's spaces, their ratings and photos (D18, HOME-1 to HOME-4).
        services.AddSingleton<SpaceService>();
        services.AddSingleton<SpaceRatingService>();
        services.AddSingleton<HomeService>();
        services.AddSingleton(sp => new SpacePhotoService(
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<ISpaceRepository>(),
            sp.GetRequiredService<SpaceService>(),
            sp.GetRequiredService<ISubjectEventRepository>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<EventSource>(),
            enabled: StoresPhotos(sp, settings)));

        // Metrics, readings and lab visits (D20): information only, so nothing touches the ledger.
        services.AddSingleton<MetricService>();
        services.AddSingleton<MetricReadingService>();
        services.AddSingleton<MetricReads>();
        services.AddSingleton(sp => new LabVisitService(
            sp.GetRequiredService<IMetricRepository>(),
            sp.GetRequiredService<MetricService>(),
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<EventSource>(),
            reportsEnabled: StoresPhotos(sp, settings)));

        // Daily and weekly reviews (REVIEW-1, REVIEW-2) and their vault reflections (§4.1).
        services.AddSingleton<ReviewService>();
        services.AddSingleton<ReflectionService>();

        // Covers (D15) and Oura (D14): each takes an outside client that may not be set up.
        services.AddSingleton(sp => new CoverService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetService<IUnsplashClient>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new OuraConnectionService(
            sp.GetRequiredService<IIntegrationRepository>(),
            sp.GetService<IOuraClient>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new OuraSyncService(
            sp.GetRequiredService<OuraConnectionService>(),
            sp.GetService<IOuraClient>(),
            sp.GetRequiredService<IActivityRepository>(),
            sp.GetRequiredService<IIntegrationRepository>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton<WorkoutNoteService>();
        services.AddSingleton<ActivityReads>();
        services.AddSingleton<ActivityRewards>();
        services.AddSingleton<ActivitySyncService>();

        // Settling everything at once (code review CR46): the food and activity days, rank-ups.
        services.AddSingleton<SettlementService>();
    }
}

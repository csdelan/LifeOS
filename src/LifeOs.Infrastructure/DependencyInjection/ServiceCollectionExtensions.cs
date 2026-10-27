using System.Globalization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Application.Capture;
using LifeOs.Application.Covers;
using LifeOs.Application.Food;
using LifeOs.Application.Home;
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

namespace LifeOs.Infrastructure.DependencyInjection;

/// <summary>
/// Composition root: registers the application services and their Postgres-backed
/// implementations against a single connection string. This is the only place
/// wiring lives; the CLI and any future host build on it.
/// <para>
/// <c>coinValueUsd</c> is what a reward Coin is worth in dollars. A host with
/// configuration passes its <c>Rewards:CoinValueUsd</c>; otherwise it comes from the
/// <c>Rewards__CoinValueUsd</c> environment variable (the same setting, as ASP.NET names it
/// there), else <see cref="CoinRates.DefaultUsd"/>.
/// </para>
/// <para>
/// <c>homeTimeZone</c> decides which local day an earn counts on (<see cref="HomeZone"/>).
/// It comes from the host's <c>Rewards:HomeTimeZone</c> the same way, else the
/// <c>Rewards__HomeTimeZone</c> environment variable, else <see cref="HomeZone.Default"/>.
/// </para>
/// <para>
/// <c>objectStorage</c>, when given, keeps new artifact bytes in Supabase Storage rather than
/// the database (migration 0035); artifacts already in the database still read from there. A
/// host passes it explicitly (<see cref="SupabaseStorageOptions.Resolve"/>) — nothing here reads
/// it from the environment, so a test never uploads. <c>allowPhotosInDatabase</c> false refuses
/// food photos when there is no object storage, so they never fill a small hosted database.
/// </para>
/// <para>
/// <c>integrations</c> connects outside services (activity plan §3.1): with an Oura app it
/// registers <see cref="IOuraClient"/>, and with a token key the repository can store tokens.
/// A host passes it explicitly (<see cref="IntegrationSettings.Resolve"/>), so a test never
/// calls Oura. Without it, <see cref="OuraConnectionService"/> says Oura isn't set up. With an
/// Unsplash key it registers <see cref="IUnsplashClient"/>; without one,
/// <see cref="CoverService"/> says Unsplash isn't set up.
/// </para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The environment variable that carries <see cref="CoinRates.ConfigurationKey"/>.</summary>
    public const string CoinValueVariable = "Rewards__CoinValueUsd";

    /// <summary>The environment variable that carries <see cref="HomeZone.ConfigurationKey"/>.</summary>
    public const string HomeTimeZoneVariable = "Rewards__HomeTimeZone";

    public static IServiceCollection AddLifeOsKernel(
        this IServiceCollection services, string connectionString, string sourceId = KernelSources.Cli,
        decimal? coinValueUsd = null, string? homeTimeZone = null,
        SupabaseStorageOptions? objectStorage = null, bool allowPhotosInDatabase = true,
        IntegrationSettings? integrations = null)
    {
        var coinValue = coinValueUsd ?? ParseCoinValueUsd(Environment.GetEnvironmentVariable(CoinValueVariable));
        var homeZone = ParseHomeTimeZone(homeTimeZone ?? Environment.GetEnvironmentVariable(HomeTimeZoneVariable));

        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<IArtifactStore>(_ => new NpgsqlArtifactStore(connectionString));
        if (objectStorage is not null)
        {
            services.AddSingleton<IObjectStorage>(_ =>
                new SupabaseObjectStorage(SupabaseObjectStorage.CreateHttpClient(), objectStorage));
        }

        services.AddSingleton<IArtifactBlobStore>(sp =>
            new NpgsqlArtifactBlobStore(connectionString, sp.GetService<IObjectStorage>()));
        services.AddSingleton<IEventStore>(_ => new NpgsqlEventStore(connectionString));
        services.AddSingleton<IEventReader>(_ => new NpgsqlEventReader(connectionString));
        services.AddSingleton<ISubjectRepository>(_ => new NpgsqlSubjectRepository(connectionString));
        services.AddSingleton<IRelationRepository>(_ => new NpgsqlRelationRepository(connectionString));
        services.AddSingleton<IActivityWriter>(_ => new NpgsqlActivityWriter(connectionString));
        services.AddSingleton<ISubjectEventRepository>(_ => new NpgsqlSubjectEventRepository(connectionString));
        services.AddSingleton<ITagRepository>(_ => new NpgsqlTagRepository(connectionString));
        services.AddSingleton<IAppointmentRepository>(_ => new NpgsqlAppointmentRepository(connectionString));
        services.AddSingleton<ITimerRepository>(_ => new NpgsqlTimerRepository(connectionString));
        services.AddSingleton<ITaskSeriesRepository>(_ => new NpgsqlTaskSeriesRepository(connectionString));
        services.AddSingleton<ILedgerRepository>(_ => new NpgsqlLedgerRepository(connectionString, homeZone));
        services.AddSingleton<IHomeCalendar>(_ => new NpgsqlHomeCalendar(connectionString, homeZone));
        services.AddSingleton<IFoodLogRepository>(_ => new NpgsqlFoodLogRepository(connectionString));
        services.AddSingleton<ISpaceRepository>(_ => new NpgsqlSpaceRepository(connectionString));
        services.AddSingleton<IIntegrationRepository>(_ =>
            new NpgsqlIntegrationRepository(connectionString, integrations?.TokenCipher));
        services.AddSingleton<IActivityRepository>(_ => new NpgsqlActivityRepository(connectionString));
        if (integrations?.Unsplash is { } unsplash)
        {
            services.AddSingleton<IUnsplashClient>(_ =>
                new UnsplashHttpClient(UnsplashHttpClient.CreateHttpClient(), unsplash));
        }

        if (integrations?.Oura is { } oura)
        {
            services.AddSingleton<IOuraClient>(sp =>
                new OuraHttpClient(OuraHttpClient.CreateHttpClient(), oura, sp.GetRequiredService<IClock>()));
        }

        services.AddSingleton(sp => new CaptureService(
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<IArtifactStore>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        // Binary captures (CAP-2 voice / CAP-4 attachments). The size guard is
        // configurable via BSK_MAX_ARTIFACT_BYTES, defaulting to ArtifactLimits.DefaultMaxBytes.
        var maxArtifactBytes = ResolveMaxArtifactBytes();
        services.AddSingleton(sp => new AttachmentService(
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<IClock>(),
            sourceId,
            maxArtifactBytes));

        services.AddSingleton(sp => new SubjectService(sp.GetRequiredService<ISubjectRepository>()));

        // Repeating Tasks (D16): the series service the status, pattern, edit, tag, archive and
        // timer paths consult.
        services.AddSingleton(sp => new TaskSeriesService(
            sp.GetRequiredService<ITaskSeriesRepository>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>()));

        services.AddSingleton(sp => new ChildCreationService(sp.GetRequiredService<SubjectService>()));

        services.AddSingleton(sp => new RelationService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IRelationRepository>()));

        services.AddSingleton(sp => new AttributeService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<TaskSeriesService>()));

        services.AddSingleton(sp => new RenameService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<IAppointmentRepository>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ITaskSeriesRepository>()));

        services.AddSingleton(sp => new RecurrenceService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<TaskSeriesService>()));

        services.AddSingleton(sp => new StatusService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<RewardService>(),
            sp.GetRequiredService<IClock>(),
            sourceId,
            sp.GetRequiredService<TaskSeriesService>()));

        services.AddSingleton(sp => new ArchiveService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<IClock>(),
            sourceId,
            sp.GetRequiredService<TaskSeriesService>(),
            sp.GetRequiredService<ISpaceRepository>()));

        services.AddSingleton(sp => new PromotionService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IEventReader>(),
            sp.GetRequiredService<StatusService>(),
            sp.GetRequiredService<ITagRepository>()));

        services.AddSingleton(sp => new DecisionService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<RelationService>()));

        services.AddSingleton(sp => new ActivityService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IActivityWriter>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        services.AddSingleton(sp => new AdherenceService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<IActivityWriter>(),
            sp.GetRequiredService<RewardService>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sourceId));

        services.AddSingleton(sp => new TimerService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ITimerRepository>(),
            sp.GetRequiredService<StatusService>(),
            sp.GetRequiredService<IClock>(),
            sourceId,
            sp.GetRequiredService<TaskSeriesService>()));

        services.AddSingleton(sp => new FoodRewards(
            sp.GetRequiredService<IFoodLogRepository>(),
            sp.GetRequiredService<ILedgerRepository>(),
            sp.GetRequiredService<RewardService>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        services.AddSingleton(sp => new FoodReads(
            sp.GetRequiredService<IFoodLogRepository>(),
            sp.GetRequiredService<ILedgerRepository>()));

        services.AddSingleton(sp => new FoodLogService(
            sp.GetRequiredService<IFoodLogRepository>(),
            sp.GetRequiredService<FoodRewards>(),
            sp.GetRequiredService<FoodReads>(),
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        services.AddSingleton(sp => new FoodPhotoService(
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<FoodLogService>(),
            sp.GetRequiredService<IClock>(),
            sourceId,
            enabled: allowPhotosInDatabase || sp.GetService<IObjectStorage>() is not null));

        services.AddSingleton(sp => new RewardService(
            sp.GetRequiredService<ILedgerRepository>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IClock>(),
            sourceId,
            coinValue));

        services.AddSingleton(sp => new DomainService(sp.GetRequiredService<ILedgerRepository>()));

        // Exception days (REWARD-8, xp-domains plan §7.4.3).
        services.AddSingleton(sp => new ExceptionDayService(
            sp.GetRequiredService<ILedgerRepository>(),
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<RewardService>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        // The home's spaces and their ratings (HOME-1, HOME-2, environment plan §4.1–§4.2).
        services.AddSingleton(sp => new SpaceService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<ISpaceRepository>(),
            sp.GetRequiredService<ArchiveService>()));
        services.AddSingleton(sp => new SpaceRatingService(
            sp.GetRequiredService<ISpaceRepository>(),
            sp.GetRequiredService<SpaceService>(),
            sp.GetRequiredService<RewardService>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>(),
            sourceId));
        services.AddSingleton(sp => new HomeService(sp.GetRequiredService<ILedgerRepository>()));

        // Photos of each space, from a baseline (HOME-4, environment plan §8): the Food Log's
        // photo storage, so the same hosts take them.
        services.AddSingleton(sp => new SpacePhotoService(
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<ISpaceRepository>(),
            sp.GetRequiredService<SpaceService>(),
            sp.GetRequiredService<ISubjectEventRepository>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>(),
            sourceId,
            enabled: allowPhotosInDatabase || sp.GetService<IObjectStorage>() is not null));

        services.AddSingleton(sp => new RewardCatalogService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<ILedgerRepository>()));

        services.AddSingleton(sp => new BountyService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>()));

        services.AddSingleton(sp => new CoverService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetService<IUnsplashClient>(),
            sp.GetRequiredService<IClock>()));

        services.AddSingleton(sp => new HabitTargetService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>()));

        services.AddSingleton(sp => new RelateService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IEventReader>(),
            sp.GetRequiredService<ISubjectEventRepository>()));

        services.AddSingleton(sp => new TagService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IEventReader>(),
            sp.GetRequiredService<ITagRepository>(),
            sp.GetRequiredService<TaskSeriesService>()));

        services.AddSingleton(sp => new PeopleService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<ISubjectRepository>()));

        services.AddSingleton(sp => new AppointmentService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IAppointmentRepository>()));

        services.AddSingleton(sp => new TriageService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IEventReader>(),
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<IClock>(),
            sourceId,
            sp.GetRequiredService<StatusService>()));

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
        services.AddSingleton(sp => new WorkoutNoteService(
            sp.GetRequiredService<IActivityRepository>(),
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<IClock>(),
            sourceId));
        services.AddSingleton(sp => new ActivityReads(
            sp.GetRequiredService<IActivityRepository>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new ActivityRewards(
            sp.GetRequiredService<IActivityRepository>(),
            sp.GetRequiredService<ILedgerRepository>(),
            sp.GetRequiredService<RewardService>(),
            sp.GetRequiredService<IHomeCalendar>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        // Operational services that work directly against the store.
        services.AddSingleton(_ => new MigrationRunner(connectionString));
        services.AddSingleton(_ => new DerivedRebuilder(connectionString));
        services.AddSingleton(_ => new DiagnosticRunner(connectionString));

        return services;
    }

    /// <summary>
    /// A configured coin value: <see cref="CoinRates.DefaultUsd"/> when blank, otherwise a
    /// dollar amount above zero. Anything else fails loudly — Coins are real money, so a
    /// typo must not silently fall back to the default.
    /// </summary>
    public static decimal ParseCoinValueUsd(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return CoinRates.DefaultUsd;
        }

        return decimal.TryParse(
                   configured.Trim().TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
               && value > 0
            ? value
            : throw new InvalidOperationException(
                $"{CoinRates.ConfigurationKey} must be a dollar amount above zero, e.g. 0.50; got '{configured}'.");
    }

    /// <summary>
    /// A configured home time zone: <see cref="HomeZone.Default"/> when blank, otherwise an
    /// IANA zone name. Anything else fails loudly — a misread zone would file earns on the
    /// wrong days. (Postgres checks the name itself on first use.)
    /// </summary>
    public static string ParseHomeTimeZone(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return HomeZone.Default;
        }

        var zone = configured.Trim();
        return HomeZone.IsWellFormed(zone)
            ? zone
            : throw new InvalidOperationException(
                $"{HomeZone.ConfigurationKey} must be an IANA time zone name, e.g. {HomeZone.Default}; got '{configured}'.");
    }

    /// <summary>
    /// The binary-artifact size guard, from <c>BSK_MAX_ARTIFACT_BYTES</c> when it holds
    /// a positive integer, otherwise <see cref="ArtifactLimits.DefaultMaxBytes"/>.
    /// </summary>
    private static long ResolveMaxArtifactBytes()
    {
        var configured = Environment.GetEnvironmentVariable("BSK_MAX_ARTIFACT_BYTES");
        return long.TryParse(configured, out var bytes) && bytes > 0
            ? bytes
            : ArtifactLimits.DefaultMaxBytes;
    }
}

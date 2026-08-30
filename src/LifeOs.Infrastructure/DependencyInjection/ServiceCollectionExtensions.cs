using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.Diagnostics;
using LifeOs.Infrastructure.Migrations;
using LifeOs.Infrastructure.Persistence;
using LifeOs.Infrastructure.Rebuild;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Infrastructure.DependencyInjection;

/// <summary>
/// Composition root: registers the application services and their Postgres-backed
/// implementations against a single connection string. This is the only place
/// wiring lives; the CLI and any future host build on it.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLifeOsKernel(
        this IServiceCollection services, string connectionString, string sourceId = KernelSources.Cli)
    {
        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<IArtifactStore>(_ => new NpgsqlArtifactStore(connectionString));
        services.AddSingleton<IEventStore>(_ => new NpgsqlEventStore(connectionString));
        services.AddSingleton<IEventReader>(_ => new NpgsqlEventReader(connectionString));
        services.AddSingleton<ISubjectRepository>(_ => new NpgsqlSubjectRepository(connectionString));
        services.AddSingleton<IRelationRepository>(_ => new NpgsqlRelationRepository(connectionString));
        services.AddSingleton<IActivityWriter>(_ => new NpgsqlActivityWriter(connectionString));

        services.AddSingleton(sp => new CaptureService(
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<IArtifactStore>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        services.AddSingleton(sp => new SubjectService(sp.GetRequiredService<ISubjectRepository>()));

        services.AddSingleton(sp => new RelationService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IRelationRepository>()));

        services.AddSingleton(sp => new StatusService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        services.AddSingleton(sp => new PromotionService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IEventReader>()));

        services.AddSingleton(sp => new DecisionService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<RelationService>()));

        services.AddSingleton(sp => new ActivityService(
            sp.GetRequiredService<SubjectService>(),
            sp.GetRequiredService<IActivityWriter>(),
            sp.GetRequiredService<IClock>(),
            sourceId));

        // Operational services that work directly against the store.
        services.AddSingleton(_ => new MigrationRunner(connectionString));
        services.AddSingleton(_ => new DerivedRebuilder(connectionString));
        services.AddSingleton(_ => new DiagnosticRunner(connectionString));

        return services;
    }
}

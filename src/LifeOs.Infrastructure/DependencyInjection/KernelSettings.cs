using System.Globalization;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure.Integrations;
using LifeOs.Infrastructure.Storage;

namespace LifeOs.Infrastructure.DependencyInjection;

/// <summary>
/// Everything a host decides about its kernel, in one record (code review CR18): the hosts build
/// one and <see cref="ServiceCollectionExtensions.AddLifeOsKernel(Microsoft.Extensions.DependencyInjection.IServiceCollection, KernelSettings)"/>
/// registers it, so a new setting is a property here rather than another parameter on every
/// host's call.
/// </summary>
/// <param name="ConnectionString">The owner connection the kernel writes on.</param>
public sealed record KernelSettings(string ConnectionString)
{
    /// <summary>The environment variable that carries <see cref="CoinRates.ConfigurationKey"/>.</summary>
    public const string CoinValueVariable = "Rewards__CoinValueUsd";

    /// <summary>The environment variable that carries <see cref="HomeZone.ConfigurationKey"/>.</summary>
    public const string HomeTimeZoneVariable = "Rewards__HomeTimeZone";

    /// <summary>The environment variable that sets <see cref="MaxArtifactBytes"/>.</summary>
    public const string MaxArtifactBytesVariable = "BSK_MAX_ARTIFACT_BYTES";

    /// <summary>Who the kernel's events say wrote them (<see cref="KernelSources"/>).</summary>
    public string SourceId { get; init; } = KernelSources.Cli;

    /// <summary>What a reward Coin is worth in dollars (REWARD-3).</summary>
    public decimal CoinValueUsd { get; init; } = CoinRates.DefaultUsd;

    /// <summary>Which local day an earn counts on (<see cref="HomeZone"/>).</summary>
    public string HomeTimeZone { get; init; } = HomeZone.Default;

    /// <summary>
    /// When given, new artifact bytes go to Supabase Storage rather than the database (migration
    /// 0035); artifacts already in the database still read from there. Never read from the
    /// environment here, so a test never uploads.
    /// </summary>
    public SupabaseStorageOptions? ObjectStorage { get; init; }

    /// <summary>
    /// False refuses food, space and lab-report bytes when there is no object storage, so they
    /// never fill a small hosted database. By default only a database on this machine keeps them
    /// (<see cref="KernelConnectionString.IsLocal"/>), whichever door writes: <c>bsk</c> pointed at
    /// staging without object storage refuses a photo as the API does (code review CR71).
    /// </summary>
    public bool AllowPhotosInDatabase { get; init; } = KernelConnectionString.IsLocal(ConnectionString);

    /// <summary>
    /// Outside services (activity plan §3.1): with an Oura app the kernel registers
    /// <see cref="IOuraClient"/>, with a token key it can store tokens, and with an Unsplash key it
    /// registers <see cref="IUnsplashClient"/>. Never read from the environment here, so a test
    /// never calls them.
    /// </summary>
    public IntegrationSettings? Integrations { get; init; }

    /// <summary>The binary-capture size guard (CAP-2, CAP-4).</summary>
    public long MaxArtifactBytes { get; init; } = ArtifactLimits.DefaultMaxBytes;

    /// <summary>
    /// The settings a host gave, with the coin value, home time zone and artifact size guard
    /// taken from the environment where it gave none (<see cref="CoinValueVariable"/>,
    /// <see cref="HomeTimeZoneVariable"/>, <see cref="MaxArtifactBytesVariable"/>). A malformed
    /// coin value or zone fails loudly.
    /// </summary>
    public static KernelSettings Resolve(
        string connectionString, string sourceId = KernelSources.Cli,
        decimal? coinValueUsd = null, string? homeTimeZone = null,
        SupabaseStorageOptions? objectStorage = null, bool? allowPhotosInDatabase = null,
        IntegrationSettings? integrations = null)
        => new(connectionString)
        {
            SourceId = sourceId,
            CoinValueUsd = coinValueUsd ?? ParseCoinValueUsd(Environment.GetEnvironmentVariable(CoinValueVariable)),
            HomeTimeZone = ParseHomeTimeZone(homeTimeZone ?? Environment.GetEnvironmentVariable(HomeTimeZoneVariable)),
            ObjectStorage = objectStorage,
            AllowPhotosInDatabase = allowPhotosInDatabase ?? KernelConnectionString.IsLocal(connectionString),
            Integrations = integrations,
            MaxArtifactBytes = ParseMaxArtifactBytes(Environment.GetEnvironmentVariable(MaxArtifactBytesVariable)),
        };

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

    /// <summary>A positive byte count, else <see cref="ArtifactLimits.DefaultMaxBytes"/>.</summary>
    public static long ParseMaxArtifactBytes(string? configured)
        => long.TryParse(configured, out var bytes) && bytes > 0 ? bytes : ArtifactLimits.DefaultMaxBytes;
}

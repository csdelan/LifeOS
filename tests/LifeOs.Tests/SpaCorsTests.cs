using LifeOs.Api.Http;
using Microsoft.Extensions.Configuration;

namespace LifeOs.Tests;

public sealed class SpaCorsTests
{
    [Fact]
    public void Empty_origins_means_same_origin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var origins = SpaCors.ResolveOrigins(configuration);
        Assert.Empty(origins);
        SpaCors.EnsureSafe(origins);
    }

    [Fact]
    public void Splits_CORS_ALLOWED_ORIGINS()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CORS_ALLOWED_ORIGINS"] = "https://a.example, https://b.example",
            })
            .Build();
        var origins = SpaCors.ResolveOrigins(configuration);
        Assert.Equal(["https://a.example", "https://b.example"], origins);
        SpaCors.EnsureSafe(origins);
    }

    [Fact]
    public void Wildcard_origins_are_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SpaCors.EnsureSafe(["*"]));
        Assert.Contains("Wildcard", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => SpaCors.EnsureSafe(["https://*.example"]));
    }
}

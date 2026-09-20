using LifeOs.Api.Hosting;
using LifeOs.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace LifeOs.Tests;

public sealed class ApiConnectionsTests
{
    [Fact]
    public void Development_falls_back_to_local_docker_when_unset()
    {
        var (configuration, environment) = Host("Development");
        Assert.Equal(
            KernelConnectionString.LocalDevelopmentDefault,
            ApiConnections.ResolveOwner(configuration, environment, _ => null));
        Assert.Equal(
            ReaderConnectionString.LocalDevelopmentDefault,
            ApiConnections.ResolveReader(configuration, environment, _ => null));
    }

    [Fact]
    public void Production_throws_when_owner_connection_is_missing()
    {
        var (configuration, environment) = Host("Production");
        var ex = Assert.Throws<InvalidOperationException>(
            () => ApiConnections.ResolveOwner(configuration, environment, _ => null));
        Assert.Contains("BSK_CONNECTION_STRING", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_throws_when_reader_connection_is_missing()
    {
        var (configuration, environment) = Host("Production");
        var ex = Assert.Throws<InvalidOperationException>(
            () => ApiConnections.ResolveReader(configuration, environment, _ => null));
        Assert.Contains("BSK_READER_CONNECTION_STRING", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prefers_configuration_then_environment_variable()
    {
        var (configuration, environment) = Host(
            "Production",
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Owner"] = "Host=from-config",
            });

        Assert.Equal(
            "Host=from-config",
            ApiConnections.ResolveOwner(configuration, environment, _ => "Host=from-env"));
        Assert.Equal(
            "Host=from-env",
            ApiConnections.ResolveOwner(
                new ConfigurationBuilder().Build(),
                environment,
                name => name == KernelConnectionString.EnvironmentVariable ? "Host=from-env" : null));
    }

    private static (IConfiguration Configuration, Microsoft.Extensions.Hosting.IHostEnvironment Environment) Host(
        string environmentName,
        Dictionary<string, string?>? values = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ApplicationName = "LifeOs.Api.ConnectionTests",
        });
        if (values is not null)
        {
            builder.Configuration.AddInMemoryCollection(values);
        }

        return (builder.Configuration, builder.Environment);
    }
}

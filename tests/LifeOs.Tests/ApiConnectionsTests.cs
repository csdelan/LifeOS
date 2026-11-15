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
            ApiConnections.ResolveWriter(configuration, environment, _ => null));
        Assert.Equal(
            ReaderConnectionString.LocalDevelopmentDefault,
            ApiConnections.ResolveReader(configuration, environment, _ => null));
    }

    [Fact]
    public void Production_throws_when_no_write_connection_is_set()
    {
        var (configuration, environment) = Host("Production");
        var ex = Assert.Throws<InvalidOperationException>(
            () => ApiConnections.ResolveWriter(configuration, environment, _ => null));
        Assert.Contains("BSK_WRITER_CONNECTION_STRING", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_comes_before_the_owner_which_is_read_only_without_one()
    {
        var (configuration, environment) = Host(
            "Production",
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Owner"] = "Host=owner-from-config",
                ["ConnectionStrings:Writer"] = "Host=writer-from-config",
            });
        var environmentVariables = new Dictionary<string, string?>
        {
            [KernelConnectionString.EnvironmentVariable] = "Host=owner-from-env",
            [BskWriter.EnvironmentVariable] = "Host=writer-from-env",
        };

        Assert.Equal(
            "Host=writer-from-config",
            ApiConnections.ResolveWriter(configuration, environment, name => environmentVariables.GetValueOrDefault(name)));
        Assert.Equal(
            "Host=writer-from-env",
            ApiConnections.ResolveWriter(new ConfigurationBuilder().Build(), environment, name => environmentVariables.GetValueOrDefault(name)));

        // Until an environment gives the role a password, the API still writes as the owner
        // (code review CR126), and once it has, the owner's string can go.
        Assert.Equal(
            "Host=owner-from-env",
            ApiConnections.ResolveWriter(
                new ConfigurationBuilder().Build(),
                environment,
                name => name == KernelConnectionString.EnvironmentVariable ? "Host=owner-from-env" : null));
        Assert.Equal(
            "Host=writer-from-env",
            ApiConnections.ResolveWriter(
                new ConfigurationBuilder().Build(),
                environment,
                name => name == BskWriter.EnvironmentVariable ? "Host=writer-from-env" : null));
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
                ["ConnectionStrings:Reader"] = "Host=from-config",
            });

        Assert.Equal(
            "Host=from-config",
            ApiConnections.ResolveReader(configuration, environment, _ => "Host=from-env"));
        Assert.Equal(
            "Host=from-env",
            ApiConnections.ResolveReader(
                new ConfigurationBuilder().Build(),
                environment,
                name => name == ReaderConnectionString.EnvironmentVariable ? "Host=from-env" : null));
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

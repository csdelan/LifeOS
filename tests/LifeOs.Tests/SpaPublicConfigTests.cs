using LifeOs.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace LifeOs.Tests;

public sealed class SpaPublicConfigTests
{
    [Fact]
    public void Production_throws_when_supabase_public_values_are_missing()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ApplicationName = "LifeOs.Api.SpaPublicConfigTests",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VITE_SUPABASE_URL"] = "",
            ["VITE_SUPABASE_ANON_KEY"] = "",
            ["Public:SupabaseUrl"] = "",
            ["Public:SupabaseAnonKey"] = "",
        });
        var ex = Assert.Throws<InvalidOperationException>(
            () => SpaPublicConfig.Resolve(builder.Configuration, builder.Environment));
        Assert.Contains("VITE_SUPABASE_URL", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_a_js_assignment_without_connection_strings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lifeos-spa-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            SpaPublicConfig.Write(dir, "/", "https://abc.supabase.co", "anon-key");
            var body = File.ReadAllText(Path.Combine(dir, SpaPublicConfig.FileName));
            Assert.StartsWith("window.__LIFEOS_PUBLIC_CONFIG__=", body, StringComparison.Ordinal);
            Assert.Contains("https://abc.supabase.co", body, StringComparison.Ordinal);
            Assert.Contains("anon-key", body, StringComparison.Ordinal);
            Assert.DoesNotContain("BSK_CONNECTION_STRING", body, StringComparison.Ordinal);
            Assert.DoesNotContain("service_role", body, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Development_allows_empty_public_values()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = "LifeOs.Api.SpaPublicConfigTests",
        });
        var resolved = SpaPublicConfig.Resolve(
            new ConfigurationBuilder().Build(),
            builder.Environment);
        Assert.Equal("/", resolved.ApiBaseUrl);
        Assert.Equal("", resolved.SupabaseUrl);
    }
}

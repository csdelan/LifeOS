using LifeOs.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace LifeOs.Tests;

public sealed class SupabaseAuthOptionsTests
{
    [Fact]
    public void Reads_ALLOWED_EMAILS_and_splits_on_commas()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = "https://abc.supabase.co/auth/v1/",
                ["Auth:Audience"] = "authenticated",
                ["Auth:JwtSecret"] = "unit-test-hmac-secret-32bytes-min!",
                ["ALLOWED_EMAILS"] = " owner@example.com , Other@Example.com ",
            })
            .Build();

        var options = SupabaseAuthOptions.From(configuration);
        options.Validate();

        Assert.Equal("https://abc.supabase.co/auth/v1", options.Issuer);
        Assert.True(options.IsAllowed("OWNER@example.com"));
        Assert.True(options.IsAllowed("other@example.com"));
        Assert.False(options.IsAllowed("stranger@example.com"));
        Assert.True(string.IsNullOrEmpty(options.JwksUrl));
    }

    [Fact]
    public void Derives_jwks_from_issuer_when_no_hmac_secret()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = "https://abc.supabase.co/auth/v1",
                ["Auth:Audience"] = "authenticated",
                ["ALLOWED_EMAILS"] = "owner@example.com",
            })
            .Build();

        var options = SupabaseAuthOptions.From(configuration);
        Assert.Equal("https://abc.supabase.co/auth/v1/.well-known/jwks.json", options.JwksUrl);
    }

    [Fact]
    public void Validate_rejects_empty_allowlist()
    {
        var options = new SupabaseAuthOptions
        {
            Issuer = "https://abc.supabase.co/auth/v1",
            Audience = "authenticated",
            JwtSecret = "unit-test-hmac-secret-32bytes-min!",
            AllowedEmails = "  ,  ",
        };
        options.Normalize();
        var ex = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("ALLOWED_EMAILS", ex.Message, StringComparison.Ordinal);
    }
}

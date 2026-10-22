using System.Security.Cryptography;
using LifeOs.Infrastructure.Integrations;

namespace LifeOs.Tests;

/// <summary>
/// The Oura settings and the token key (activity plan A1, §3.1): Oura is optional, a half-set or
/// malformed setting fails startup in words, and no secret ever prints.
/// </summary>
public sealed class OuraSettingsTests
{
    private const string ClientId = "4f1c2d3e-0000-4000-8000-0123456789ab";
    private const string Secret = "oura-client-secret-value";
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static Func<string, string?> Settings(params (string Key, string? Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        return key => map.GetValueOrDefault(key);
    }

    [Fact]
    public void Nothing_set_leaves_oura_off()
    {
        var settings = IntegrationSettings.Resolve(Settings());

        Assert.Null(settings.Oura);
        Assert.Null(settings.TokenCipher);
    }

    [Fact]
    public void Full_settings_resolve_with_oura_endpoints_by_default()
    {
        var settings = IntegrationSettings.Resolve(Settings(
            (OuraOptions.ClientIdKey, ClientId), (OuraOptions.ClientSecretKey, Secret), (TokenCipher.KeyKey, Key)));

        var oura = Assert.IsType<OuraOptions>(settings.Oura);
        Assert.NotNull(settings.TokenCipher);
        Assert.Equal(ClientId, oura.ClientId);
        Assert.Equal(OuraOptions.DefaultAuthorizeUrl, oura.AuthorizeUrl);
        Assert.Equal(OuraOptions.DefaultTokenUrl, oura.TokenUrl);
        Assert.Equal(OuraOptions.DefaultTokenUrl, oura.RefreshUrl);
        Assert.Equal(OuraOptions.DefaultRevokeUrl, oura.RevokeUrl);
    }

    [Fact]
    public void The_refresh_url_can_move_on_its_own()
    {
        var moved = new Uri("https://moi.ouraring.com/oauth/v2/ext/oauth-token");
        var oura = OuraOptions.Resolve(Settings(
            (OuraOptions.ClientIdKey, ClientId), (OuraOptions.ClientSecretKey, Secret),
            (OuraOptions.RefreshUrlKey, moved.AbsoluteUri)))!;

        Assert.Equal(OuraOptions.DefaultTokenUrl, oura.TokenUrl);
        Assert.Equal(moved, oura.RefreshUrl);
    }

    [Theory]
    [InlineData(ClientId, null)]
    [InlineData(null, Secret)]
    [InlineData("has space", Secret)]
    public void A_half_set_or_spaced_client_fails(string? clientId, string? secret)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationSettings.Resolve(Settings(
            (OuraOptions.ClientIdKey, clientId), (OuraOptions.ClientSecretKey, secret), (TokenCipher.KeyKey, Key))));

        Assert.Contains("Oura:Client", ex.Message);
        Assert.DoesNotContain(Secret, ex.Message);
    }

    [Fact]
    public void A_client_without_a_token_key_fails()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationSettings.Resolve(Settings(
            (OuraOptions.ClientIdKey, ClientId), (OuraOptions.ClientSecretKey, Secret))));

        Assert.Contains(TokenCipher.KeyKey, ex.Message);
        Assert.Contains("RandomNumberGenerator", ex.Message);
    }

    [Theory]
    [InlineData("not base64 at all!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")] // 16 bytes
    public void A_malformed_token_key_fails(string key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationSettings.Resolve(Settings(
            (TokenCipher.KeyKey, key))));

        Assert.Contains(TokenCipher.KeyKey, ex.Message);
    }

    [Theory]
    [InlineData("ftp://example.com/token")]
    [InlineData("/oauth/token")]
    public void A_malformed_endpoint_fails(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OuraOptions.Resolve(Settings(
            (OuraOptions.ClientIdKey, ClientId), (OuraOptions.ClientSecretKey, Secret), (OuraOptions.TokenUrlKey, url))));

        Assert.Contains(OuraOptions.TokenUrlKey, ex.Message);
    }

    [Fact]
    public void Secrets_never_print()
    {
        var settings = IntegrationSettings.Resolve(Settings(
            (OuraOptions.ClientIdKey, ClientId), (OuraOptions.ClientSecretKey, Secret), (TokenCipher.KeyKey, Key)));

        var printed = settings.ToString();
        Assert.Contains(ClientId, printed);
        Assert.DoesNotContain(Secret, printed);
        Assert.DoesNotContain(Key, printed);
    }
}

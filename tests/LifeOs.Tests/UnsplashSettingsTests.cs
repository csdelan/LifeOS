using LifeOs.Domain;
using LifeOs.Infrastructure.Integrations;

namespace LifeOs.Tests;

/// <summary>
/// The Unsplash settings (cover-images plan §3.2, C1): optional, a malformed one fails startup
/// in words, and the Access Key never prints.
/// </summary>
public sealed class UnsplashSettingsTests
{
    private const string Key = "unsplash-access-key-123";

    private static Func<string, string?> Settings(params (string Key, string? Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        return key => map.GetValueOrDefault(key);
    }

    [Fact]
    public void No_key_leaves_covers_off()
    {
        Assert.Null(UnsplashOptions.Resolve(Settings()));
        Assert.Null(UnsplashOptions.Resolve(Settings((UnsplashOptions.AccessKeyKey, "  "))));
        Assert.Null(IntegrationSettings.Resolve(Settings()).Unsplash);
    }

    [Fact]
    public void A_key_alone_uses_unsplash_and_the_default_app_name()
    {
        var options = UnsplashOptions.Resolve(Settings((UnsplashOptions.AccessKeyKey, $" {Key} ")))!;

        Assert.Equal(Key, options.AccessKey);
        Assert.Equal(Cover.DefaultAppName, options.AppName);
        Assert.Equal(UnsplashOptions.DefaultApiUrl, options.ApiUrl);
    }

    [Fact]
    public void Unsplash_resolves_alongside_integrations_without_oura()
    {
        var settings = IntegrationSettings.Resolve(Settings(
            (UnsplashOptions.AccessKeyKey, Key), (UnsplashOptions.AppNameKey, "lifeos-dev"),
            (UnsplashOptions.ApiUrlKey, "http://127.0.0.1:5399/unsplash")));

        Assert.Null(settings.Oura);
        var unsplash = Assert.IsType<UnsplashOptions>(settings.Unsplash);
        Assert.Equal("lifeos-dev", unsplash.AppName);
        // A base the paths resolve against.
        Assert.Equal("http://127.0.0.1:5399/unsplash/", unsplash.ApiUrl.AbsoluteUri);
    }

    [Fact]
    public void A_key_with_spaces_fails_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            UnsplashOptions.Resolve(Settings((UnsplashOptions.AccessKeyKey, "two words"))));
        Assert.Contains("can't contain spaces", ex.Message);
        Assert.DoesNotContain("two words", ex.Message);
    }

    [Theory]
    [InlineData("LifeOS")]
    [InlineData("life os")]
    public void An_app_name_that_cant_be_a_utm_source_fails_startup(string appName)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => UnsplashOptions.Resolve(Settings(
            (UnsplashOptions.AccessKeyKey, Key), (UnsplashOptions.AppNameKey, appName))));
        Assert.Contains("utm_source", ex.Message);
    }

    [Theory]
    [InlineData("api.unsplash.com")]
    [InlineData("ftp://api.unsplash.com/")]
    public void An_api_url_that_isnt_http_fails_startup(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => UnsplashOptions.Resolve(Settings(
            (UnsplashOptions.AccessKeyKey, Key), (UnsplashOptions.ApiUrlKey, url))));
        Assert.Contains(UnsplashOptions.ApiUrlKey, ex.Message);
    }

    [Fact]
    public void The_key_never_prints()
    {
        var options = UnsplashOptions.Resolve(Settings((UnsplashOptions.AccessKeyKey, Key)))!;
        var settings = IntegrationSettings.Resolve(Settings((UnsplashOptions.AccessKeyKey, Key)));

        Assert.DoesNotContain(Key, options.ToString());
        Assert.DoesNotContain(Key, settings.ToString());
    }
}

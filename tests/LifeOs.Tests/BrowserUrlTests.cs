using LifeOs.Pilot.Shell;

namespace LifeOs.Tests;

public sealed class BrowserUrlTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("http://example.com/path?q=1", "http://example.com/path?q=1")]
    [InlineData("www.example.com", "https://www.example.com/")]
    [InlineData("HTTPS://Example.COM", "https://example.com/")]
    public void TryGet_accepts_http_and_https(string linkText, string expected)
    {
        Assert.True(BrowserUrl.TryGet(linkText, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("ftp://example.com")]
    [InlineData("mailto:user@example.com")]
    [InlineData("javascript:alert(1)")]
    public void TryGet_rejects_non_browser_urls(string? linkText)
        => Assert.False(BrowserUrl.TryGet(linkText, out _));
}

namespace LifeOs.Pilot.Shell;

/// <summary>
/// Filters auto-detected RichTextBox links down to http(s) URLs the shell
/// can hand to the default browser. Kept free of WinForms so tests can compile it.
/// </summary>
internal static class BrowserUrl
{
    /// <summary>
    /// Accepts only http(s) URLs. Bare <c>www.</c> hosts get an https scheme so
    /// the shell can hand them to the default browser.
    /// </summary>
    public static bool TryGet(string? linkText, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(linkText))
        {
            return false;
        }

        var text = linkText.Trim();
        if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }
}

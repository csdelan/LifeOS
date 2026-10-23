using LifeOs.Api.Auth;
using LifeOs.Domain;

namespace LifeOs.Api.Http;

public static class SecurityHeaders
{
    public static void UseSecurityHeaders(this WebApplication app)
    {
        var auth = app.Services.GetRequiredService<SupabaseAuthOptions>();
        var csp = BuildContentSecurityPolicy(auth, includeStrictCsp: !app.Environment.IsDevelopment());

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers.XFrameOptions = "DENY";
                // The app's own pages may use the camera: a food photo is taken from the Log food sheet.
                headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
                // An endpoint that set its own policy (a sandboxed download) keeps it.
                if (!string.IsNullOrEmpty(csp) && string.IsNullOrEmpty(headers.ContentSecurityPolicy))
                {
                    headers.ContentSecurityPolicy = csp;
                }

                return Task.CompletedTask;
            });

            await next();
        });
    }

    public static string BuildContentSecurityPolicy(SupabaseAuthOptions auth, bool includeStrictCsp)
    {
        if (!includeStrictCsp)
        {
            return "";
        }

        var connect = new List<string> { "'self'", "https://accounts.google.com" };
        if (Uri.TryCreate(auth.Issuer, UriKind.Absolute, out var issuer))
        {
            var origin = issuer.GetLeftPart(UriPartial.Authority);
            connect.Add(origin);
            connect.Add("wss://" + issuer.Host);
        }

        return string.Join("; ",
        [
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self' 'unsafe-inline'",
            // Cover images (COVER-1, D15) load straight from Unsplash's image host, as its API
            // terms ask. The browser never calls api.unsplash.com, so connect-src doesn't gain it.
            "img-src 'self' data: blob: https://" + Cover.ImageHost,
            "font-src 'self' data:",
            "connect-src " + string.Join(' ', connect),
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "object-src 'none'",
            "upgrade-insecure-requests",
        ]);
    }
}

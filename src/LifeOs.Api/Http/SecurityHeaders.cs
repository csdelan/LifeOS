using LifeOs.Api.Auth;

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
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                if (!string.IsNullOrEmpty(csp))
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
            "img-src 'self' data: blob:",
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

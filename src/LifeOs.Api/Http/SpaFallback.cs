namespace LifeOs.Api.Http;

public static class SpaFallback
{
    public static void UseSpaStaticFiles(this WebApplication app)
    {
        var webRoot = app.Environment.WebRootPath;
        if (string.IsNullOrEmpty(webRoot) || !Directory.Exists(webRoot))
        {
            return;
        }

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                var path = ctx.Context.Request.Path.Value ?? "";
                if (path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    return;
                }

                if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                }
            },
        });
    }

    public static void MapSpaFallback(this WebApplication app)
    {
        var webRoot = app.Environment.WebRootPath;
        if (string.IsNullOrEmpty(webRoot))
        {
            return;
        }

        var index = Path.Combine(webRoot, "index.html");
        if (!File.Exists(index))
        {
            return;
        }

        app.MapFallback((HttpContext context) =>
        {
            if (IsReserved(context.Request.Path))
            {
                return Results.NotFound();
            }

            return Results.File(index, "text/html");
        }).AllowAnonymous();
    }

    public static bool IsReserved(PathString path)
        => path.StartsWithSegments("/api")
            || path.StartsWithSegments("/health")
            || path.StartsWithSegments("/openapi")
            || path.StartsWithSegments("/swagger");
}

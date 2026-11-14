namespace LifeOs.Api.Http;

public static class SpaFallback
{
    /// <summary>
    /// The app's page names the files of the build it came from, and a deploy removes the old
    /// ones, so a cached copy of it would ask for files that are gone (code review CR93).
    /// </summary>
    private const string NoCache = "no-cache, no-store, must-revalidate";

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
                if (path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("/public-config.js", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.Headers.CacheControl = NoCache;
                    return;
                }

                if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                }
            },
        });

        // A file of the build that the static files didn't serve is one a deploy removed. The
        // fallback never sees a file's name, so it reached no endpoint and got the sign-in's 401,
        // which says the wrong thing to the page asking for it (code review CR93). This can't be an
        // endpoint: the static files stand aside for any request an endpoint matched.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/assets"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
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

            // A link straight to a page (/browse?selected=...) is answered here, not by the static
            // files, so it needs the same header the static index.html gets.
            context.Response.Headers.CacheControl = NoCache;
            return Results.File(index, "text/html");
        }).AllowAnonymous();
    }

    /// <summary>
    /// Paths the app's page never answers. <c>/assets</c> is the build's own files, which answer
    /// 404 when a deploy removed them (code review CR93).
    /// </summary>
    public static bool IsReserved(PathString path)
        => path.StartsWithSegments("/assets")
            || path.StartsWithSegments("/api")
            || path.StartsWithSegments("/mcp")
            || path.StartsWithSegments("/.well-known")
            || path.StartsWithSegments("/health")
            || path.StartsWithSegments("/openapi")
            || path.StartsWithSegments("/swagger");
}

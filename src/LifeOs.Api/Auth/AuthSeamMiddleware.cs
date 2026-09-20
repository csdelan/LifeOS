namespace LifeOs.Api.Auth;

/// <summary>
/// Seam for Supabase JWT validation. Not implemented this pass (local/dev only).
/// </summary>
public sealed class AuthSeamMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        // TODO(auth): validate the Supabase JWT against GoTrue JWKS and reject
        // unauthenticated requests. This pass is local/dev only — every request
        // is allowed through so the Vite app can talk to the API on localhost.
        return next(context);
    }
}

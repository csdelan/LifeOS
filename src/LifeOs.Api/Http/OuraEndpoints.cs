using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Activity;

namespace LifeOs.Api.Http;

/// <summary>
/// The Oura connection (ACTIVITY-1, activity plan §3.1) — the HTTP side of
/// <see cref="OuraConnectionService"/>.
/// <list type="number">
/// <item><c>POST /api/oura/authorize</c> with the SPA's <c>/activity/connected</c> URL answers
/// Oura's authorize URL; the page sends the browser there.</item>
/// <item>Oura sends the browser back to <c>/activity/connected?code=…&amp;state=…</c>, an SPA
/// route, and the page posts both to <c>POST /api/oura/connect</c>.</item>
/// </list>
/// The connect finishes on an API call rather than a redirect to the API, because the API
/// authenticates by the bearer token the SPA sends. Nothing here ever answers a token. Oura not
/// set up, not connected, or refusing a sign-in is a 409 in words; Oura down is a 502.
/// </summary>
public static class OuraEndpoints
{
    public static void MapOuraEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/oura")
            .WithTags("Oura")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/status", GetStatus).WithName("GetOuraStatus")
            .Produces<OuraStatusRead>();
        api.MapPost("/authorize", Authorize).WithName("AuthorizeOura")
            .Produces<OuraAuthorizeRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status409Conflict);
        api.MapPost("/connect", Connect).WithName("ConnectOura")
            .Produces<OuraStatusRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status409Conflict)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
        api.MapPost("/disconnect", Disconnect).WithName("DisconnectOura")
            .Produces<OuraStatusRead>();
    }

    private static async Task<IResult> GetStatus(OuraConnectionService oura, CancellationToken cancellationToken)
        => Results.Ok(OuraStatusRead.From(await oura.GetStatusAsync(cancellationToken)));

    private static async Task<IResult> Authorize(
        OuraAuthorizeBody? body, OuraConnectionService oura, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body?.RedirectUri)
            || !Uri.TryCreate(body.RedirectUri.Trim(), UriKind.Absolute, out var redirectUri))
        {
            return Results.BadRequest(new ApiError(
                $"Connecting Oura takes redirectUri: the page's {OuraConnectionService.CallbackPath} URL."));
        }

        var url = await oura.BeginConnectAsync(redirectUri, cancellationToken);
        return Results.Ok(new OuraAuthorizeRead { AuthorizeUrl = url.AbsoluteUri });
    }

    private static async Task<IResult> Connect(
        OuraConnectBody? body, OuraConnectionService oura, CancellationToken cancellationToken)
        => Results.Ok(OuraStatusRead.From(
            await oura.ConnectAsync(body?.Code ?? "", body?.State ?? "", cancellationToken)));

    private static async Task<IResult> Disconnect(OuraConnectionService oura, CancellationToken cancellationToken)
        => Results.Ok(OuraStatusRead.From(await oura.DisconnectAsync(cancellationToken)));
}

/// <summary><c>POST /api/oura/authorize</c>: the URL Oura sends the browser back to.</summary>
public sealed class OuraAuthorizeBody
{
    /// <summary>The SPA's <c>/activity/connected</c>, as registered with Oura (https, or http on localhost).</summary>
    public string? RedirectUri { get; set; }
}

/// <summary><c>POST /api/oura/connect</c>: what Oura sent back on <c>/activity/connected</c>.</summary>
public sealed class OuraConnectBody
{
    public string? Code { get; set; }
    public string? State { get; set; }
}

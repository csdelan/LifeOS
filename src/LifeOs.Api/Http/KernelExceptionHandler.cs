using Microsoft.AspNetCore.Diagnostics;

namespace LifeOs.Api.Http;

/// <summary>Answers an exception from a request with the status and words <see cref="KernelRefusals"/> gives it.</summary>
public static class KernelExceptionHandler
{
    public static void UseKernelExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var (status, payload) = Map(ex);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(payload);
        }));
    }

    /// <summary>The status and body for <paramref name="ex"/>: its refusal, or a 500 that doesn't repeat a fault's text.</summary>
    public static (int Status, ApiError Body) Map(Exception? ex)
        => KernelRefusals.Describe(ex) is { } refusal
            ? (refusal.Status, new ApiError(refusal.Message, refusal.Candidates))
            : (StatusCodes.Status500InternalServerError, new ApiError(KernelRefusals.FaultMessage));
}

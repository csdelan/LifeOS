using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using Microsoft.AspNetCore.Diagnostics;

namespace LifeOs.Api.Http;

/// <summary>Maps Application-layer exceptions to HTTP status codes.</summary>
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

    private static (int Status, ApiError Body) Map(Exception? ex) => ex switch
    {
        SubjectNotFoundException notFound => (StatusCodes.Status404NotFound, new ApiError(notFound.Message)),
        AmbiguousSubjectException ambiguous => (StatusCodes.Status409Conflict, new ApiError(
            ambiguous.Message,
            ambiguous.Candidates.Select(c => new { id = c.Id, urn = c.Urn, type = c.Type, title = c.Title }))),
        DuplicateSubjectException duplicate => (StatusCodes.Status409Conflict, new ApiError(duplicate.Message)),
        ArgumentException or InvalidOperationException => (
            StatusCodes.Status400BadRequest, new ApiError(ex.Message)),
        _ => (StatusCodes.Status500InternalServerError, new ApiError(ex?.Message ?? "Unexpected error."))
    };
}

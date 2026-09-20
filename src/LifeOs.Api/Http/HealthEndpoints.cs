using LifeOs.Api.Hosting;

namespace LifeOs.Api.Http;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Json(new LivenessResponse("ok")))
            .AllowAnonymous()
            .WithName("Liveness")
            .ExcludeFromDescription();

        app.MapGet("/health/ready", Ready)
            .AllowAnonymous()
            .WithName("Readiness")
            .ExcludeFromDescription();
    }

    private static async Task<IResult> Ready(
        ApiConnectionStrings connections,
        CancellationToken cancellationToken)
    {
        var owner = await PostgresProbe.CanQueryAsync(connections.Owner, cancellationToken);
        var reader = await PostgresProbe.CanQueryAsync(connections.Reader, cancellationToken);
        var body = new ReadinessResponse(owner && reader ? "ready" : "unready", owner, reader);
        return owner && reader
            ? Results.Json(body)
            : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

public sealed record LivenessResponse(string Status);

public sealed record ReadinessResponse(string Status, bool Owner, bool Reader);

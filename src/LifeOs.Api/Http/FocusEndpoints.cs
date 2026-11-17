using LifeOs.Api.Auth;
using LifeOs.Application.Focus;

namespace LifeOs.Api.Http;

/// <summary>
/// Choosing the Primary focus (TODAY-1, D6, focus plan G1), the HTTP twin of <c>bsk focus</c>.
/// Writes call <see cref="FocusService"/>; Focus itself reads it through <c>GET /api/focus</c>.
/// <list type="bullet">
/// <item><c>POST /api/focus/primary {goalId, reviewId?}</c>: make an Active Goal the focus, or clear
/// it with a null <c>goalId</c> (No focus). <c>reviewId</c> is the review it's chosen in, whose
/// Keep focus it turns off. Anything but an Active, unarchived Goal is a 400 in words.</item>
/// </list>
/// </summary>
public static class FocusEndpoints
{
    public static void MapFocusEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/focus")
            .WithTags("Focus")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapPost("/primary", SetPrimary).WithName("SetPrimaryFocus")
            .Produces<FocusChoiceRow>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    /// <summary>Makes the Goal the Primary focus, or clears it. Choosing the focus already held writes nothing.</summary>
    private static async Task<IResult> SetPrimary(
        SetPrimaryFocusBody body, FocusService focus, CancellationToken cancellationToken)
    {
        var change = await focus.SetAsync(body.GoalId?.ToString(), body.ReviewId, cancellationToken);
        return Results.Ok(FocusChoiceRow.From(change.Focus, change.Written));
    }
}

public sealed class SetPrimaryFocusBody
{
    /// <summary>The Goal to focus on; null for No focus.</summary>
    public Guid? GoalId { get; set; }

    /// <summary>The review it's chosen in, if any.</summary>
    public Guid? ReviewId { get; set; }
}

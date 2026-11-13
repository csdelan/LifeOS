using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// Daily and weekly reviews.

/// <summary>
/// Reviews (REVIEW-1, REVIEW-2, D3, D8, reviews plan §2) through <c>bsk.v_review</c>
/// (migration 0052). Writes go through <c>SubjectService</c> (opening one) and
/// <c>StatusService</c> (completing it), like any subject.
/// </summary>
public interface IReviewRepository
{
    /// <summary>The review with that id, or null (no such review).</summary>
    Task<Review?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The <paramref name="kind"/> review for <paramref name="period"/>, or null when it isn't open yet.</summary>
    Task<Review?> FindAsync(string kind, DateOnly period, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reviews whose period is from <paramref name="from"/> to <paramref name="to"/> (each end
    /// optional), of <paramref name="kind"/> when given, by period then kind (daily first);
    /// archived ones only when asked.
    /// </summary>
    Task<IReadOnlyList<Review>> ListAsync(
        string? kind = null, DateOnly? from = null, DateOnly? to = null, bool includeArchived = false,
        CancellationToken cancellationToken = default);
}
